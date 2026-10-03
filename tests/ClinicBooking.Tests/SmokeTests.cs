using ClinicBooking.Tests.Support;

namespace ClinicBooking.Tests;

public class SmokeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SmokeTests(ApiFactory factory)
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