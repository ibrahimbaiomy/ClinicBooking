using Microsoft.AspNetCore.Authorization;

namespace ClinicBooking.Api.Authorization;

/// <summary>
/// The policy requirement of a clinic-scoped permission (D34, D57): the caller must hold the
/// permission in the clinic the request addresses. The policy is named after the permission, as
/// for global ones; <see cref="ClinicPermissionAuthorizationHandler"/> finds the clinic.
/// </summary>
public sealed class ClinicPermissionRequirement : IAuthorizationRequirement
{
    public ClinicPermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
