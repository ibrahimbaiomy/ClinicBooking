using ClinicBooking.Application.DTOs;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

/// <summary>Input shape only. Credentials themselves are checked by the auth service.</summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public const int MaxFieldLength = 256;

    public LoginRequestValidator()
    {
        RuleFor(r => r.UserName)
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("error.auth.user_name_required")
            .MaximumLength(MaxFieldLength).WithMessage("error.auth.field_too_long");

        RuleFor(r => r.Password)
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrEmpty(value)).WithMessage("error.auth.password_required")
            .MaximumLength(MaxFieldLength).WithMessage("error.auth.field_too_long");
    }
}
