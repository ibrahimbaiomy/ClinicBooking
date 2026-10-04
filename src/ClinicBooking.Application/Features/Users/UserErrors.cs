namespace ClinicBooking.Application.Features.Users;

/// <summary>Error keys of user management and of passwords (D10, D57).</summary>
public static class UserErrors
{
    public const string NotFound = "error.user.not_found";
    public const string UserNameRequired = "error.user.user_name_required";
    public const string UserNameTooShort = "error.user.user_name_too_short";
    public const string UserNameTooLong = "error.user.user_name_too_long";
    public const string UserNameInvalid = "error.user.user_name_invalid";
    public const string UserNameTaken = "error.user.user_name_taken";
    public const string PermissionUnknown = "error.user.permission_unknown";
    public const string CannotDisableSelf = "error.user.cannot_disable_self";
    public const string LastAdministrator = "error.user.last_administrator";
    public const string CannotResetOwnPassword = "error.user.cannot_reset_own_password";

    /// <summary>Used by every request that carries a new password; failures come back as field errors on that field.</summary>
    public const string ValidationFailed = "error.validation.failed";
}

/// <summary>Password-policy error keys (D48), shared by create, reset and change-password.</summary>
public static class PasswordErrors
{
    public const string Required = "error.password.required";
    public const string TooLong = "error.password.too_long";
    public const string TooShort = "error.password.too_short";
    public const string RequiresDigit = "error.password.requires_digit";
    public const string RequiresLowercase = "error.password.requires_lowercase";
    public const string RequiresUppercase = "error.password.requires_uppercase";
    public const string RequiresUniqueChars = "error.password.requires_unique_chars";

    public const int MaxLength = 128;
}
