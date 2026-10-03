using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Tests.Support;

namespace ClinicBooking.Tests;

public class HealthEndpointTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_returns_200_without_checking_dependencies()
    {
        var response = await _client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    // No database is configured in this factory, so the readiness check fails
    // without needing a real server. The success path needs Testcontainers (later).
    [Fact]
    public async Task Ready_returns_503_problem_details_when_database_is_unavailable()
    {
        var response = await _client.GetAsync("/health/ready");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("error.health.not_ready", problem.GetProperty("title").GetString());
    }
}
