using System.Text.RegularExpressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Domain.Permissions;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

internal static class UserRules
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 64;

    // ASCII only, so a user name is always typeable and is compared the same everywhere.
    private static readonly Regex UserNameCharacters = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IRuleBuilderOptions<T, string?> BeAUserName<T>(this IRuleBuilder<T, string?> builder) =>
        builder
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(UserErrors.UserNameRequired)
            .Must(value => value!.Length >= UserNameMinLength).WithMessage(UserErrors.UserNameTooShort)
            .Must(value => value!.Length <= UserNameMaxLength).WithMessage(UserErrors.UserNameTooLong)
            .Must(value => UserNameCharacters.IsMatch(value!)).WithMessage(UserErrors.UserNameInvalid);

    /// <summary>
    /// Required and bounded only. The policy itself (length, character classes) is Identity's, so there
    /// is one definition of it; its refusals come back as the same <c>error.password.*</c> keys.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> BeAPassword<T>(this IRuleBuilder<T, string?> builder) =>
        builder
            .Must(value => !string.IsNullOrEmpty(value)).WithMessage(PasswordErrors.Required)
            .Must(value => value!.Length <= PasswordErrors.MaxLength).WithMessage(PasswordErrors.TooLong);

    public static IRuleBuilderOptions<T, IReadOnlyList<string>?> BeKnownPermissions<T>(
        this IRuleBuilder<T, IReadOnlyList<string>?> builder,
        IReadOnlyList<string> allowed) =>
        builder.Must(list => list is not null && list.All(p => p is not null && allowed.Contains(p, StringComparer.Ordinal)))
            .WithMessage(UserErrors.PermissionUnknown);
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.UserName).Cascade(CascadeMode.Stop).BeAUserName();
        RuleFor(r => r.TemporaryPassword).Cascade(CascadeMode.Stop).BeAPassword();
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.TemporaryPassword).Cascade(CascadeMode.Stop).BeAPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrEmpty(value)).WithMessage("error.auth.current_password_required")
            .Must(value => value!.Length <= PasswordErrors.MaxLength).WithMessage(PasswordErrors.TooLong);
        RuleFor(r => r.NewPassword).Cascade(CascadeMode.Stop).BeAPassword();
    }
}

public sealed class ReplaceGlobalPermissionsRequestValidator : AbstractValidator<ReplaceGlobalPermissionsRequest>
{
    public ReplaceGlobalPermissionsRequestValidator()
    {
        RuleFor(r => r.Permissions).BeKnownPermissions(Permissions.Global);
    }
}

public sealed class ReplaceClinicPermissionsRequestValidator : AbstractValidator<ReplaceClinicPermissionsRequest>
{
    public ReplaceClinicPermissionsRequestValidator()
    {
        RuleFor(r => r.Permissions).BeKnownPermissions(Permissions.ClinicScoped);
    }
}

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithMessage("error.paging.page_invalid");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, ListUsersQuery.MaxPageSize).WithMessage("error.paging.page_size_invalid");

        RuleFor(q => q.SortBy)
            .Must(value => UserSortFields.All.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("error.sort.invalid");

        RuleFor(q => q.SortDirection)
            .Must(value => value is not null
                && (value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("desc", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("error.sort.invalid");

        RuleFor(q => q.Search)
            .MaximumLength(ListUsersQuery.MaxSearchLength).WithMessage("error.search.too_long");
    }
}
