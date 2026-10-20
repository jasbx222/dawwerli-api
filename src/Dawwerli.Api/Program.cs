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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config, runWorker: config.GetValue("Jobs:Enabled", true));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IRealtime, SignalRRealtime>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ValidationFilter>();
builder.Services.AddControllers(o => o.Filters.AddService<ValidationFilter>());
builder.Services.AddProblemDetails();

var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
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
builder.Services.AddAuthorization();

var limits = config.GetSection("RateLimits");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Otp, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.GetValue<int>("OtpPerMinute"), Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "u" : ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.GetValue<int>("GlobalPerMinute"), Window = TimeSpan.FromMinutes(1) }));
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
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

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
app.UseMiddleware<AppVersionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<LiveHub>(LiveHub.Path);
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

if (config.GetValue("Database:MigrateAndSeedOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<Seeder>().RunAsync();
}

app.Run();

public static class RateLimitPolicies
{
    public const string Otp = "otp";
}

public partial class Program;
