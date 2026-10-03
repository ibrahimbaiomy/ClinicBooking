using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Tests.Support;

namespace ClinicBooking.Tests;

public class ErrorHandlingTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ErrorHandlingTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("invalid", HttpStatusCode.BadRequest, "error.test.invalid")]
    [InlineData("not-found", HttpStatusCode.NotFound, "error.test.not_found")]
    [InlineData("conflict", HttpStatusCode.Conflict, "error.test.conflict")]
    [InlineData("rule", HttpStatusCode.UnprocessableEntity, "error.test.rule")]
    [InlineData("unexpected", HttpStatusCode.InternalServerError, "error.unexpected")]
    public async Task Exception_is_mapped_to_problem_details_with_error_key(
        string kind,
        HttpStatusCode expectedStatus,
        string expectedKey)
    {
        var response = await _client.GetAsync($"/test/throw/{kind}");
        var problem = await ReadProblemAsync(response);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedKey, problem.GetProperty("title").GetString());
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(
            response.Headers.GetValues("X-Correlation-Id").Single(),
            problem.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Invalid_request_carries_field_error_keys()
    {
        var response = await _client.GetAsync("/test/throw/invalid");
        var problem = await ReadProblemAsync(response);

        var errors = problem.GetProperty("errors").GetProperty("name");
        Assert.Equal("error.test.name_required", errors[0].GetString());
    }

    [Fact]
    public async Task Unexpected_error_does_not_leak_internal_detail()
    {
        var response = await _client.GetAsync("/test/throw/unexpected");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(ThrowingController.SecretMessage, body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_with_key_title()
    {
        var response = await _client.GetAsync("/does-not-exist");
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("error.http.404", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Malformed_body_returns_validation_problem_with_keys_only()
    {
        var response = await _client.PostAsync(
            "/test/body",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("error.validation.failed", problem.GetProperty("title").GetString());
        foreach (var field in problem.GetProperty("errors").EnumerateObject())
        {
            Assert.All(field.Value.EnumerateArray(), m => Assert.Equal("error.validation.invalid", m.GetString()));
        }
    }

    [Fact]
    public async Task Valid_incoming_correlation_id_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "abc-123_XYZ");

        var response = await _client.SendAsync(request);

        Assert.Equal("abc-123_XYZ", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Unsafe_incoming_correlation_id_is_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad id with spaces");

        var response = await _client.SendAsync(request);

        var returned = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("bad id with spaces", returned);
        Assert.Matches("^[A-Za-z0-9_-]{1,64}$", returned);
    }

    [Fact]
    public async Task Missing_correlation_id_is_generated()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.False(string.IsNullOrEmpty(response.Headers.GetValues("X-Correlation-Id").Single()));
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
