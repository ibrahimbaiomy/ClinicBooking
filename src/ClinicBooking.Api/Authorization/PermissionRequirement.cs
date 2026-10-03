using Microsoft.AspNetCore.Authorization;

namespace ClinicBooking.Api.Authorization;

/// <summary>One requirement per permission; the policy is named after the permission (D34).</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
