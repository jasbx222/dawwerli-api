using Dawwerli.Application.Contracts;
using FluentValidation;

namespace Dawwerli.Application.Validation;

/// <summary>Shape validation (400). Business rules live in the services/domain (422/409).</summary>
public sealed class OtpRequestValidator : AbstractValidator<OtpRequestDto>
{
    public OtpRequestValidator() => RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+?[0-9]{8,15}$");
}

public sealed class OtpVerifyValidator : AbstractValidator<OtpVerifyDto>
{
    public OtpVerifyValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+?[0-9]{8,15}$");
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]+$");
        RuleFor(x => x.Role).NotEmpty();
    }
}

public sealed class ProfileValidator : AbstractValidator<ProfileDto>
{
    public ProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Area).NotEmpty().MaximumLength(60);
        RuleFor(x => x.GovernorateId).GreaterThan(0);
    }
}

public sealed class CreateRequestValidator : AbstractValidator<CreateRequestDto>
{
    public CreateRequestValidator()
    {
        RuleFor(x => x.Mode).NotEmpty();
        RuleFor(x => x.Condition).NotEmpty();
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.GovernorateId).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class SubmitOfferValidator : AbstractValidator<SubmitOfferDto>
{
    public SubmitOfferValidator()
    {
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.DeliveryHours).GreaterThan(0);
        RuleFor(x => x.PhotoKey).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public sealed class RegisterPageValidator : AbstractValidator<RegisterPageDto>
{
    public RegisterPageValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Instagram).NotEmpty().Matches(@"^@?[A-Za-z0-9._]{1,30}$");
        RuleFor(x => x.CategoryIds).NotEmpty();
        RuleFor(x => x.GovernorateIds).NotEmpty();
    }
}

public sealed class PageSettingsValidator : AbstractValidator<PageSettingsDto>
{
    public PageSettingsValidator()
    {
        RuleFor(x => x.From).InclusiveBetween(0, 23);
        RuleFor(x => x.To).InclusiveBetween(0, 23);
        RuleForEach(x => x.Days).InclusiveBetween(0, 6);
    }
}

public sealed class TopUpValidator : AbstractValidator<CreateTopUpDto>
{
    public TopUpValidator()
    {
        RuleFor(x => x.Method).NotEmpty();
        RuleFor(x => x.TransactionRef).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ReceiptKey).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class RatingValidator : AbstractValidator<RatingDto>
{
    public RatingValidator()
    {
        RuleFor(x => x.Stars).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(500);
    }
}

public sealed class CreateAdValidator : AbstractValidator<CreateAdDto>
{
    public CreateAdValidator()
    {
        RuleFor(x => x.Slot).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Headline).MaximumLength(120);
        RuleFor(x => x.Days).GreaterThan(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
    }
}

public sealed class CreateDisputeValidator : AbstractValidator<CreateDisputeDto>
{
    public CreateDisputeValidator()
    {
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.Text).MaximumLength(1000);
    }
}
