namespace ClinicBooking.Application.Interfaces;

/// <summary>
/// The check for an endpoint that addresses a resource by its own id (a doctor, later an
/// appointment), where a policy attribute cannot know the clinic before the resource is loaded
/// (D6, D57). The service loads the resource, then asks this.
/// </summary>
public interface IClinicAccess
{
    /// <summary>
    /// Passes when the signed-in user holds <paramref name="permission"/> in at least one of
    /// <paramref name="clinicIds"/>. Otherwise throws: 404 with <paramref name="notFoundKey"/> when the
    /// user holds nothing at all in those clinics (the resource must look as if it did not exist), or
    /// 403 when the user holds some other clinic-scoped permission there.
    /// </summary>
    Task RequireAsync(
        IReadOnlyCollection<long> clinicIds,
        string permission,
        string notFoundKey,
        CancellationToken cancellationToken);
}
