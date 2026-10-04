using System.Reflection;
using ClinicBooking.Api.Authorization;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicBooking.Tests;

/// <summary>
/// The exemption from the temporary-password gate is a hole in a wall: it must stay exactly two
/// endpoints wide (D57). A new endpoint that copies the attribute fails here.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AccountGateGuardTests : IClassFixture<AuthApiFixture>
{
    private static readonly string[] Allowed = ["GET api/auth/me", "POST api/auth/change-password"];

    private readonly AuthApiFixture _fixture;

    public AccountGateGuardTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _ = fixture.Factory.CreateClient();
    }

    [Fact]
    public void The_exemption_is_on_exactly_me_and_change_password_among_every_registered_endpoint()
    {
        var actions = _fixture.Factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToList();
        Assert.NotEmpty(actions);

        var exempt = actions
            .Where(a => a.EndpointMetadata.OfType<AllowWhilePasswordChangeRequiredAttribute>().Any())
            .SelectMany(a => a.ActionConstraints!.OfType<HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods)
                .Select(method => $"{method} {a.AttributeRouteInfo!.Template}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Allowed.Order(StringComparer.Ordinal).ToArray(), exempt);
    }

    [Fact]
    public void The_attribute_is_used_on_methods_only_in_every_loaded_assembly()
    {
        var usage = typeof(AllowWhilePasswordChangeRequiredAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);

        var assemblies = new[] { typeof(Program).Assembly, typeof(AccountGateGuardTests).Assembly };
        var users = assemblies
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<AllowWhilePasswordChangeRequiredAttribute>() is not null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["AuthController.ChangePassword", "AuthController.Me"], users);
    }
}
