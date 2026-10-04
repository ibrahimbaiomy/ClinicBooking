using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

public interface IUserService
{
    Task<PagedResponse<UserSummaryResponse>> ListAsync(ListUsersQuery query, CancellationToken cancellationToken);

    Task<UserDetailResponse> GetAsync(long id, CancellationToken cancellationToken);

    Task<UserDetailResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<UserDetailResponse> DisableAsync(long id, CancellationToken cancellationToken);

    Task<UserDetailResponse> EnableAsync(long id, CancellationToken cancellationToken);

    Task<UserDetailResponse> ReplaceGlobalPermissionsAsync(
        long id,
        ReplaceGlobalPermissionsRequest request,
        CancellationToken cancellationToken);

    Task<UserDetailResponse> ReplaceClinicPermissionsAsync(
        long id,
        long clinicId,
        ReplaceClinicPermissionsRequest request,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(long id, ResetPasswordRequest request, CancellationToken cancellationToken);

    AssignablePermissionsResponse GetAssignablePermissions();
}
