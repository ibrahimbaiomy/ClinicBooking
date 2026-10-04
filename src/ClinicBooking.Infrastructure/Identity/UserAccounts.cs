using System.Globalization;
using System.Security.Claims;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>User administration through <c>UserManager</c>; the only place besides the seeder that touches Identity (D57).</summary>
public sealed class UserAccounts : IUserAccounts
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly AppDbContext _db;

    public UserAccounts(UserManager<ApplicationUser> users, IPasswordHasher<ApplicationUser> hasher, AppDbContext db)
    {
        _users = users;
        _hasher = hasher;
        _db = db;
    }

    public Task<AccountState?> GetStateAsync(long userId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AccountState(u.IsActive, u.MustChangePassword))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<UserSummaryResponse>> ListAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var users = _db.Users.AsNoTracking();

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        // Identity stores the user name upper-cased in NormalizedUserName.
        var search = query.Search?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(search))
        {
            users = users.Where(u => u.NormalizedUserName!.Contains(search));
        }

        var totalCount = await users.CountAsync(cancellationToken);

        var items = await Order(users, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new UserSummaryResponse(u.Id, u.UserName!, u.IsActive, u.MustChangePassword, u.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<UserSummaryResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<UserSummaryResponse?> FindAsync(long userId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummaryResponse(u.Id, u.UserName!, u.IsActive, u.MustChangePassword, u.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CreateUserOutcome> CreateAsync(
        string userName,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var user = new ApplicationUser { UserName = userName, IsActive = true, MustChangePassword = true };

        // Identity checks the password policy before it creates anything. A racing duplicate is caught
        // by the unique index and surfaces as a 409 (see ApplicationUserConfiguration).
        var result = await _users.CreateAsync(user, temporaryPassword);
        if (result.Succeeded)
        {
            return new CreateUserOutcome(user.Id, [], UserNameTaken: false);
        }

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return new CreateUserOutcome(null, [], UserNameTaken: true);
        }

        var keys = PasswordPolicyKeys.From(result.Errors);
        if (keys.Count == 0)
        {
            // Codes only, never the password.
            throw new InvalidOperationException(
                "The user could not be created: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        return new CreateUserOutcome(null, keys, UserNameTaken: false);
    }

    public async Task SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId);
        if (user.IsActive == isActive)
        {
            return;
        }

        user.IsActive = isActive;
        EnsureUpdated(await _users.UpdateAsync(user));
    }

    public async Task<IReadOnlyList<string>> ResetPasswordAsync(
        long userId,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId);

        var errors = new List<IdentityError>();
        foreach (var validator in _users.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(_users, user, temporaryPassword);
            if (!validation.Succeeded)
            {
                errors.AddRange(validation.Errors);
            }
        }

        if (errors.Count > 0)
        {
            var keys = PasswordPolicyKeys.From(errors);
            if (keys.Count == 0)
            {
                throw new InvalidOperationException(
                    "The password could not be reset: " + string.Join(", ", errors.Select(e => e.Code)));
            }

            return keys;
        }

        // One save: the new hash, the forced change and a clean lockout state go in together. The security
        // stamp changes too, so anything derived from the old credentials stops being valid.
        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.MustChangePassword = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        EnsureUpdated(await _users.UpdateAsync(user));

        return [];
    }

    public Task<bool> IsActiveAdministratorAsync(long userId, CancellationToken cancellationToken) =>
        ActiveAdministrators().AnyAsync(u => u.Id == userId, cancellationToken);

    public Task<int> CountOtherActiveAdministratorsAsync(long userId, CancellationToken cancellationToken) =>
        ActiveAdministrators().CountAsync(u => u.Id != userId, cancellationToken);

    public async Task ReplaceGlobalPermissionsAsync(
        long userId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId);
        var held = (await _users.GetClaimsAsync(user))
            .Where(c => c.Type == PermissionChecker.ClaimType)
            .ToList();

        var revoked = held.Where(c => !permissions.Contains(c.Value, StringComparer.Ordinal)).ToList();
        var granted = permissions
            .Where(p => held.All(c => c.Value != p))
            .Select(p => new Claim(PermissionChecker.ClaimType, p))
            .ToList();

        if (revoked.Count > 0)
        {
            EnsureUpdated(await _users.RemoveClaimsAsync(user, revoked));
        }

        if (granted.Count > 0)
        {
            EnsureUpdated(await _users.AddClaimsAsync(user, granted));
        }
    }

    private IQueryable<ApplicationUser> ActiveAdministrators() =>
        _db.Users.AsNoTracking().Where(u => u.IsActive
            && _db.UserClaims.Any(c => c.UserId == u.Id
                && c.ClaimType == PermissionChecker.ClaimType
                && c.ClaimValue == Permissions.Users.Manage));

    private async Task<ApplicationUser> FindUserAsync(long userId) =>
        await _users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture))
        ?? throw new NotFoundException(UserErrors.NotFound);

    private static void EnsureUpdated(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        // Two administrators changed the same user at once: the second must look again.
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
        {
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }

        throw new InvalidOperationException("The user could not be updated: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    private static IOrderedQueryable<ApplicationUser> Order(IQueryable<ApplicationUser> users, ListUsersQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(query.SortBy, UserSortFields.CreatedAt, StringComparison.OrdinalIgnoreCase))
        {
            return descending
                ? users.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
                : users.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id);
        }

        return descending
            ? users.OrderByDescending(u => u.NormalizedUserName).ThenBy(u => u.Id)
            : users.OrderBy(u => u.NormalizedUserName).ThenBy(u => u.Id);
    }
}
