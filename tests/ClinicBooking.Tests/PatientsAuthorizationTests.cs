using System.Net;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Each patients.* permission opens exactly its own endpoints (D63); reading needs one too. They are global:
/// no other global or clinic-scoped permission stands in for them.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PatientsAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public PatientsAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    public enum Action
    {
        List,
        Get,
        Create,
        Update,
        Delete
    }

    private static string Needed(Action action) => action switch
    {
        Action.List or Action.Get => Permissions.Patients.Read,
        Action.Create => Permissions.Patients.Create,
        Action.Update => Permissions.Patients.Edit,
        _ => Permissions.Patients.Delete
    };

    private async Task<HttpResponseMessage> SendAsync(Action action, string? token)
    {
        var manager = await PatientManagerAsync(_fixture, _client);
        var patient = await CreatePatientOkAsync(_client, manager);
        var id = patient.GetProperty("id").GetInt64();

        return action switch
        {
            Action.List => await _client.SendAsync(Json(HttpMethod.Get, "/api/patients", token)),
            Action.Get => await _client.SendAsync(Json(HttpMethod.Get, $"/api/patients/{id}", token)),
            Action.Create => await CreatePatientAsync(_client, token!, UniqueArabic(), UniqueMobile()),
            Action.Update => await UpdatePatientAsync(_client, token!, id, "اسم", patient.GetProperty("phone").GetString(), patient.GetProperty("rowVersion").GetString()),
            _ => await _client.SendAsync(Json(HttpMethod.Delete, $"/api/patients/{id}", token))
        };
    }

    public static TheoryData<Action> Actions => new() { Action.List, Action.Get, Action.Create, Action.Update, Action.Delete };

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Its_own_permission_opens_the_endpoint(Action action)
    {
        var (token, _) = await SignInAsync(_fixture, _client, Needed(action));

        var response = await SendAsync(action, token);

        Assert.True(response.IsSuccessStatusCode, $"{action}: {response.StatusCode}");
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Every_other_patient_permission_together_does_not(Action action)
    {
        var others = AllPatientPermissions.Where(p => p != Needed(action)).ToArray();
        var (token, _) = await SignInAsync(_fixture, _client, others);

        await AssertProblemAsync(await SendAsync(action, token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Every_other_global_permission_does_not_either(Action action)
    {
        var (token, _) = await SignInAsync(_fixture, _client, Permissions.Users.Manage, Permissions.Specialties.Manage, Permissions.Clinics.Manage);

        await AssertProblemAsync(await SendAsync(action, token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Without_a_token_it_is_401(Action action)
    {
        await AssertProblemAsync(await SendAsync(action, null), HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task The_patient_permissions_are_global_and_offered_as_such()
    {
        Assert.All(AllPatientPermissions, p => Assert.True(Permissions.IsGlobal(p)));
        Assert.All(AllPatientPermissions, p => Assert.False(Permissions.IsClinicScoped(p)));

        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var assignable = await ProblemAsync(await _client.SendAsync(Json(HttpMethod.Get, "/api/permissions", admin)));
        var global = UserApi.Strings(assignable.GetProperty("global"));
        Assert.All(AllPatientPermissions, p => Assert.Contains(p, global));
    }
}
