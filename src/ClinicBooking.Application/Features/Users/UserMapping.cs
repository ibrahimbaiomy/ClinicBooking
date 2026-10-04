using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;

namespace ClinicBooking.Application.Features.Users;

internal static class UserMapping
{
    /// <summary>Groups the grants by clinic; the checker already orders them by clinic then permission.</summary>
    public static IReadOnlyList<UserClinicPermissionsResponse> GroupByClinic(IEnumerable<ClinicPermissionGrant> grants) =>
        grants
            .GroupBy(g => g.ClinicId)
            .Select(group => new UserClinicPermissionsResponse(
                group.Key,
                group.First().ClinicNameAr,
                group.First().ClinicNameEn,
                group.Select(g => g.Permission).ToList()))
            .ToList();
}
