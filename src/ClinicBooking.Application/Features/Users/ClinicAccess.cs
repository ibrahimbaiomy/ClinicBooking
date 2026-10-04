using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Users;

/// <summary>The 404-versus-403 rule for resources addressed by their own id (D6, D57).</summary>
public sealed class ClinicAccess : IClinicAccess
{
    public const string ForbiddenKey = "error.auth.forbidden";

    private readonly IPermissionChecker _permissions;
    private readonly IUser _user;

    public ClinicAccess(IPermissionChecker permissions, IUser user)
    {
        _permissions = permissions;
        _user = user;
    }

    public async Task RequireAsync(
        IReadOnlyCollection<long> clinicIds,
        string permission,
        string notFoundKey,
        CancellationToken cancellationToken)
    {
        // Without a caller nothing is held, so the resource does not exist for them.
        if (_user.Id is not { } userId)
        {
            throw new NotFoundException(notFoundKey);
        }

        if (await _permissions.HasClinicPermissionInAnyAsync(userId, clinicIds, permission, cancellationToken))
        {
            return;
        }

        // Some other permission in the clinic means the caller can already see that the resource exists.
        if (await _permissions.HasAnyClinicPermissionInAnyAsync(userId, clinicIds, cancellationToken))
        {
            throw new ForbiddenException(ForbiddenKey);
        }

        throw new NotFoundException(notFoundKey);
    }
}
