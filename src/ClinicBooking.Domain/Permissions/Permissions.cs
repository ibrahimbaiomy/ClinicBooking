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

    /// <summary>Patients are personal data shared by every clinic, so every patient permission is global (D44, D63).</summary>
    public static class Patients
    {
        /// <summary>List, search and open patients. Reading needs a permission: this is personal data (D63).</summary>
        public const string Read = "patients.read";

        public const string Create = "patients.create";

        public const string Edit = "patients.edit";

        public const string Delete = "patients.delete";
    }

    public static class Doctors
    {
        /// <summary>Manage the doctors of one clinic. Clinic-scoped: granted per clinic (D57).</summary>
        public const string Manage = "doctors.manage";
    }

    /// <summary>Permissions that are not clinic-specific (D34): held once, for the whole system.</summary>
    public static IReadOnlyList<string> Global { get; } =
        [Users.Manage, Specialties.Manage, Clinics.Manage, Patients.Read, Patients.Create, Patients.Edit, Patients.Delete];

    /// <summary>Permissions granted per clinic (D34, D57): a grant is valid only in the clinic it names.</summary>
    public static IReadOnlyList<string> ClinicScoped { get; } = [Doctors.Manage];

    /// <summary>Every permission; one authorization policy exists for each.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Global, .. ClinicScoped];

    public static bool IsGlobal(string permission) => Global.Contains(permission);

    public static bool IsClinicScoped(string permission) => ClinicScoped.Contains(permission);
}
