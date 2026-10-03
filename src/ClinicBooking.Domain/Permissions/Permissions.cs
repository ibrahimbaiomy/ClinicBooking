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

    /// <summary>Permissions that are not clinic-specific (D34). All of them for now.</summary>
    public static IReadOnlyList<string> Global { get; } = [Users.Manage, Specialties.Manage];

    public static IReadOnlyList<string> All { get; } = Global;

    public static bool IsGlobal(string permission) => Global.Contains(permission);
}
