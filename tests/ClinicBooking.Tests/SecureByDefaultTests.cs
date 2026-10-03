using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using ClinicBooking.Tests.Support;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

public class SecureByDefaultTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public SecureByDefaultTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task An_action_without_any_attribute_returns_401_through_the_fallback_policy()
    {
        var response = await _client.GetAsync("/test/unattributed");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_stay_anonymous(string path)
    {
        var response = await _client.GetAsync(path);

        // 503 for ready (no database in this factory) is still an anonymous answer, not a 401.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_stays_anonymous()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { userName = "", password = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var problem = await ProblemAsync(response);
        Assert.Equal("error.auth.user_name_required", problem.GetProperty("errors").GetProperty("userName")[0].GetString());
        Assert.Equal("error.auth.password_required", problem.GetProperty("errors").GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task A_validator_message_that_is_not_an_error_key_never_reaches_the_client()
    {
        var response = await _client.PostAsJsonAsync("/test/validated", new { name = "", other = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal("error.validation.invalid", errors.GetProperty("name")[0].GetString());
        Assert.Equal("error.test.other_required", errors.GetProperty("other")[0].GetString());
    }

    [Fact]
    public void Every_request_dto_of_a_production_action_has_a_validator()
    {
        var actions = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.Assembly == typeof(Program).Assembly)
            .ToList();
        Assert.NotEmpty(actions);

        var missing = new List<string>();
        foreach (var action in actions)
        {
            foreach (var parameter in action.Parameters)
            {
                var type = parameter.ParameterType;
                if (IsSimple(type) || parameter.BindingInfo?.BindingSource == BindingSource.Services
                    || parameter.BindingInfo?.BindingSource == BindingSource.Special)
                {
                    continue;
                }

                var validator = _factory.Services.GetService(typeof(IValidator<>).MakeGenericType(type));
                if (validator is null)
                {
                    missing.Add($"{action.ControllerTypeInfo.Name}.{action.ActionName}({type.Name} {parameter.Name})");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These action arguments have no FluentValidation validator (add one in Application/Validators): "
            + string.Join(", ", missing));
    }

    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeOnly)
            || type == typeof(CancellationToken);
    }
}
