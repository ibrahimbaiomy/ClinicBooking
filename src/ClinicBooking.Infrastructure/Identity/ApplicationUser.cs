using ClinicBooking.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>
/// An application user, keyed by <c>long</c> (D6). Audited (D36) but not soft-deletable (D35).
/// Failed-login bookkeeping also touches <see cref="UpdatedAt"/>, which is accepted until
/// the audit trail of Phase 2.
/// </summary>
public class ApplicationUser : IdentityUser<long>, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }
}
