namespace ClinicBooking.Tests;

public class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Api_host_starts()
    {
        using var client = _factory.CreateClient();

        Assert.NotNull(client);
    }
}