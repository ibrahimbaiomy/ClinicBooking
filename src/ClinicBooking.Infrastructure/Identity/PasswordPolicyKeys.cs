using ClinicBooking.Application.Features.Users;
using Microsoft.AspNetCore.Identity;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>Maps Identity's password-policy error codes to error keys; the policy is defined once, in the Identity options (D48).</summary>
internal static class PasswordPolicyKeys
{
    public static IReadOnlyList<string> From(IEnumerable<IdentityError> errors) =>
        errors
            .Select(error => error.Code switch
            {
                nameof(IdentityErrorDescriber.PasswordTooShort) => PasswordErrors.TooShort,
                nameof(IdentityErrorDescriber.PasswordRequiresDigit) => PasswordErrors.RequiresDigit,
                nameof(IdentityErrorDescriber.PasswordRequiresLower) => PasswordErrors.RequiresLowercase,
                nameof(IdentityErrorDescriber.PasswordRequiresUpper) => PasswordErrors.RequiresUppercase,
                nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => PasswordErrors.RequiresUniqueChars,
                _ => null
            })
            .Where(key => key is not null)
            .Select(key => key!)
            .Distinct()
            .ToList();
}
