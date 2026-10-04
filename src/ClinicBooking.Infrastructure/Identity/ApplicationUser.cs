using ClinicBooking.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace ClinicBooking.Infrastructure.Identity;

/// <summary>
/// An application user, keyed by <c>long</c> (D6). Audited (D36) but not soft-deletable (D35): a user
/// is never deleted, only disabled (D57). Failed-login bookkeeping also touches <see cref="UpdatedAt"/>,
/// which is accepted until the audit trail of Phase 2.
/// </summary>
public class ApplicationUser : IdentityUser<long>, IAuditable
{
    /// <summary>
    /// False means disabled: no sign-in, no refresh, and the access tokens already issued stop working
    /// on the next request (D57). Defaults to true, so a user built in code or created by the seeder is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True while the password is a temporary one set by an administrator (create, reset). Until it is
    /// changed only change-password, <c>me</c>, refresh and logout work (D57).
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }
}
