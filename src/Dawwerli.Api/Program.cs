using System.Text;
using System.Threading.RateLimiting;
using Dawwerli.Api.Infrastructure;
using Dawwerli.Application;
using Dawwerli.Application.Abstractions;
using Dawwerli.Infrastructure;
using Dawwerli.Infrastructure.Persistence;
using Dawwerli.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
// Store defaults ship with the code; appsettings.json / environment still win when they set the same keys.
config.Sources.Insert(0, new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    Path = "appsettings.Store.json", Optional = false, ReloadOnChange = false,
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(builder.Environment.ContentRootPath),
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config, runWorker: config.GetValue("Jobs:Enabled", true));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SellerActingPage>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICurrentSeller, HttpCurrentSeller>();
builder.Services.AddSingleton<IRealtime, SignalRRealtime>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ValidationFilter>();
builder.Services.AddControllers(o => o.Filters.AddService<ValidationFilter>());
builder.Services.AddProblemDetails();

var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var jwtKey = Encoding.UTF8.GetBytes(jwt.SigningKey ?? "");
if (jwtKey.Length < 32)
    Console.Error.WriteLine("Jwt:SigningKey is missing or shorter than 32 characters. Authenticated requests will be rejected.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = jwtKey.Length >= 32,
        IssuerSigningKey = jwtKey.Length >= 32 ? new SymmetricSecurityKey(jwtKey) : null,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            if (ctx.HttpContext.Request.Path.StartsWithSegments(LiveHub.Path)) ctx.Token = ctx.Request.Query["access_token"];
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy(Roles.SellerPortalPolicy, p => p.RequireRole(Roles.SellerPortal)
    .RequireClaim(SellerPortalClaims.Scope, SellerPortalClaims.ScopeValue)));

var limits = config.GetSection("RateLimits");
int PositiveLimit(string name, int fallback)
{
    var value = limits.GetValue<int?>(name);
    return value is > 0 ? value.Value : fallback;
}
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var otpPerMinute = PositiveLimit("OtpPerMinute", 10);
    var adminLoginPerMinute = PositiveLimit("AdminLoginPerMinute", 5);
    var globalPerMinute = PositiveLimit("GlobalPerMinute", 300);
    o.AddPolicy(RateLimitPolicies.Otp, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = otpPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimitPolicies.AdminLogin, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = adminLoginPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "u" : ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Dawwerli API", Version = "v1" });
    o.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
    var scheme = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", scheme);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});
var corsOrigins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (corsOrigins.Any(o => o.Contains('*'))) throw new InvalidOperationException("Cors:Origins must list exact origins; wildcards are not allowed.");
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).WithHeaders("Authorization", "Content-Type").WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var conflict = ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } or DbUpdateConcurrencyException;
    var pd = new ProblemDetails { Status = conflict ? 409 : 500, Title = conflict ? "Conflict" : "Server error" };
    pd.Extensions["code"] = conflict ? "conflict" : "server_error";
    pd.Extensions["traceId"] = ctx.TraceIdentifier;
    ctx.Response.StatusCode = pd.Status.Value;
    await ctx.Response.WriteAsJsonAsync(pd);
}));

if (config.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseStaticFiles();
app.UseMiddleware<AppVersionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<LiveHub>(LiveHub.Path);
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
var sellerIndex = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "seller", "index.html");
IResult SellerHtml() => File.Exists(sellerIndex) ? Results.File(sellerIndex, "text/html; charset=utf-8") : Results.NotFound();
app.MapGet("/seller", SellerHtml);
app.MapFallback("/seller/{*path:nonfile}", SellerHtml);

if (config.GetValue("Database:MigrateAndSeedOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<Seeder>().RunAsync();
}

app.Run();

public static class RateLimitPolicies
{
    public const string Otp = "otp";
    public const string AdminLogin = "admin-login";
}

public partial class Program;
