using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Auth;
using ClinicBooking.Application.Features.Clinics;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using Microsoft.Extensions.Logging;

namespace ClinicBooking.Application.Features.Users;

/// <summary>
/// User administration (D57). Every method needs the global <c>users.manage</c> (the controller says so).
/// Log lines carry ids and permission names only: never a password, a user name or personal data.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IAppDbContext _db;
    private readonly IUserAccounts _accounts;
    private readonly IPermissionChecker _permissions;
    private readonly IUser _user;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IAppDbContext db,
        IUserAccounts accounts,
        IPermissionChecker permissions,
        IUser user,
        TimeProvider timeProvider,
        ILogger<UserService> logger)
    {
        _db = db;
        _accounts = accounts;
        _permissions = permissions;
        _user = user;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<PagedResponse<UserSummaryResponse>> ListAsync(ListUsersQuery query, CancellationToken cancellationToken) =>
        _accounts.ListAsync(query, cancellationToken);

    public async Task<UserDetailResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        var summary = await _accounts.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException(UserErrors.NotFound);

        var global = await _permissions.GetGlobalPermissionsAsync(id, cancellationToken);
        var clinics = await _permissions.GetClinicPermissionsAsync(id, cancellationToken);

        return new UserDetailResponse(
            summary.Id,
            summary.UserName,
            summary.IsActive,
            summary.MustChangePassword,
            summary.CreatedAt,
            global,
            UserMapping.GroupByClinic(clinics));
    }

    public async Task<UserDetailResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var outcome = await _accounts.CreateAsync(request.UserName!, request.TemporaryPassword!, cancellationToken);

        if (outcome.UserNameTaken)
        {
            throw new ConflictException(UserErrors.UserNameTaken);
        }

        if (outcome.PasswordErrorKeys.Count > 0)
        {
            throw PasswordRefused(nameof(CreateUserRequest.TemporaryPassword), outcome.PasswordErrorKeys);
        }

        var id = outcome.UserId!.Value;
        _logger.LogInformation("User {UserId} created by {ActorId}", id, _user.Id);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<UserDetailResponse> DisableAsync(long id, CancellationToken cancellationToken)
    {
        if (_user.Id == id)
        {
            throw new BusinessRuleException(UserErrors.CannotDisableSelf);
        }

        await InTransactionAsync(async () =>
        {
            var account = await _accounts.FindAsync(id, cancellationToken)
                ?? throw new NotFoundException(UserErrors.NotFound);
            if (!account.IsActive)
            {
                return;
            }

            await EnsureAnotherAdministratorAsync(id, cancellationToken);
            await _accounts.SetActiveAsync(id, false, cancellationToken);

            // A disabled user keeps no session: refresh tokens die here, access tokens at the next request (D57).
            var revoked = await SessionRevocation.RevokeUserSessionsAsync(
                _db, id, exceptFamilyId: null, _timeProvider.GetUtcNow(), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "User {UserId} disabled by {ActorId}; {RevokedSessions} sessions revoked", id, _user.Id, revoked);
        }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<UserDetailResponse> EnableAsync(long id, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException(UserErrors.NotFound);

        if (!account.IsActive)
        {
            await _accounts.SetActiveAsync(id, true, cancellationToken);
            _logger.LogInformation("User {UserId} enabled by {ActorId}", id, _user.Id);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<UserDetailResponse> ReplaceGlobalPermissionsAsync(
        long id,
        ReplaceGlobalPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var wanted = Distinct(request.Permissions);

        await InTransactionAsync(async () =>
        {
            _ = await _accounts.FindAsync(id, cancellationToken) ?? throw new NotFoundException(UserErrors.NotFound);

            if (!wanted.Contains(Permissions.Users.Manage))
            {
                await EnsureAnotherAdministratorAsync(id, cancellationToken);
            }

            var before = await _permissions.GetGlobalPermissionsAsync(id, cancellationToken);
            await _accounts.ReplaceGlobalPermissionsAsync(id, wanted, cancellationToken);

            _logger.LogInformation(
                "Global permissions of user {UserId} replaced by {ActorId}: granted {Granted}, revoked {Revoked}",
                id,
                _user.Id,
                wanted.Except(before, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                before.Except(wanted, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<UserDetailResponse> ReplaceClinicPermissionsAsync(
        long id,
        long clinicId,
        ReplaceClinicPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        _ = await _accounts.FindAsync(id, cancellationToken) ?? throw new NotFoundException(UserErrors.NotFound);

        // The filtered set hides soft-deleted clinics: a deleted clinic is as missing as one that never existed.
        if (!await _db.Clinics.AnyAsync(c => c.Id == clinicId, cancellationToken))
        {
            throw new NotFoundException(ClinicErrors.NotFound);
        }

        var wanted = Distinct(request.Permissions);
        var existing = await _db.UserClinicPermissions
            .Where(p => p.UserId == id && p.ClinicId == clinicId)
            .ToListAsync(cancellationToken);

        var revoked = existing.Where(p => !wanted.Contains(p.Permission)).ToList();
        var granted = wanted.Where(p => existing.All(e => e.Permission != p)).ToList();

        _db.UserClinicPermissions.RemoveRange(revoked);
        foreach (var permission in granted)
        {
            _db.UserClinicPermissions.Add(new UserClinicPermission
            {
                UserId = id,
                ClinicId = clinicId,
                Permission = permission
            });
        }

        if (revoked.Count > 0 || granted.Count > 0)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException(ConcurrencyErrors.Conflict);
            }

            _logger.LogInformation(
                "Permissions of user {UserId} in clinic {ClinicId} replaced by {ActorId}: granted {Granted}, revoked {Revoked}",
                id,
                clinicId,
                _user.Id,
                granted.Order(StringComparer.Ordinal).ToArray(),
                revoked.Select(p => p.Permission).Order(StringComparer.Ordinal).ToArray());
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(long id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (_user.Id == id)
        {
            throw new BusinessRuleException(UserErrors.CannotResetOwnPassword);
        }

        _ = await _accounts.FindAsync(id, cancellationToken) ?? throw new NotFoundException(UserErrors.NotFound);

        var errors = await _accounts.ResetPasswordAsync(id, request.TemporaryPassword!, cancellationToken);
        if (errors.Count > 0)
        {
            throw PasswordRefused(nameof(ResetPasswordRequest.TemporaryPassword), errors);
        }

        // Every old session ends, whether the user is active or not.
        var revoked = await SessionRevocation.RevokeUserSessionsAsync(
            _db, id, exceptFamilyId: null, _timeProvider.GetUtcNow(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Password of user {UserId} reset by {ActorId}; {RevokedSessions} sessions revoked", id, _user.Id, revoked);
    }

    public AssignablePermissionsResponse GetAssignablePermissions() =>
        new(Permissions.Global, Permissions.ClinicScoped);

    // The last active administrator can neither be disabled nor lose users.manage (D57). Runs inside the
    // serializable transaction, so two administrators cannot remove each other at the same moment.
    private async Task EnsureAnotherAdministratorAsync(long id, CancellationToken cancellationToken)
    {
        if (await _accounts.IsActiveAdministratorAsync(id, cancellationToken)
            && await _accounts.CountOtherActiveAdministratorsAsync(id, cancellationToken) == 0)
        {
            throw new BusinessRuleException(UserErrors.LastAdministrator);
        }
    }

    private Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken) =>
        _db.InSerializableTransactionAsync(
            async () =>
            {
                await work();
                return true;
            },
            cancellationToken);

    private static string[] Distinct(IReadOnlyList<string>? permissions) =>
        (permissions ?? []).Distinct(StringComparer.Ordinal).ToArray();

    private static InvalidRequestException PasswordRefused(string field, IReadOnlyList<string> keys) =>
        new(
            UserErrors.ValidationFailed,
            new Dictionary<string, string[]> { [char.ToLowerInvariant(field[0]) + field[1..]] = keys.ToArray() });
}
