using System.Linq;

namespace ClinicBooking.Domain.Permissions;

/// <summary>
/// Every permission name, in one place (D34). Policies are built from <see cref="All"/>
/// and are named after the permission. Never compare permission names as literals elsewhere.
/// </summary>
public static class Permissions
{
    public static class Users
    {
        public const string Manage = "users.manage";
    }

    public static class Specialties
    {
        public const string Manage = "specialties.manage";
    }

    public static class Clinics
    {
        /// <summary>Create, edit and delete clinics. Global: it cannot be scoped to a clinic that does not exist yet (D55).</summary>
        public const string Manage = "clinics.manage";
    }

    /// <summary>Permissions that are not clinic-specific (D34). All of them for now.</summary>
    public static IReadOnlyList<string> Global { get; } = [Users.Manage, Specialties.Manage, Clinics.Manage];

    public static IReadOnlyList<string> All { get; } = Global;

    public static bool IsGlobal(string permission) => Global.Contains(permission);
}
