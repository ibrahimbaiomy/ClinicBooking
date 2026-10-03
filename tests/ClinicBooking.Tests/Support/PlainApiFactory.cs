using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ClinicBooking.Tests.Support;

/// <summary>
/// Hosts the real API only: unlike <see cref="ApiFactory"/> it does not load the test controllers,
/// so what it serves is exactly what production serves (used to generate the OpenAPI document).
/// </summary>
public sealed class PlainApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "",
                ["Jwt:SigningKey"] = TestJwt.SigningKey
            }));
    }
}
