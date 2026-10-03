using System.Net;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

/// <summary>A temporary web root standing in for the built Angular bundle.</summary>
public sealed class SpaFactory : WebApplicationFactory<Program>
{
    public const string IndexMarker = "spa-index-marker";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"cb-spa-{Guid.NewGuid():N}");

    public SpaFactory()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "i18n"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), $"<!doctype html><html><body><cb-root></cb-root><!-- {IndexMarker} --></body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "main-5A5EEYQ4.js"), "console.log('bundle');");
        File.WriteAllText(Path.Combine(_webRoot, "favicon.ico"), "icon");
        File.WriteAllText(Path.Combine(_webRoot, "i18n", "ar.json"), "{\"app\":{\"title\":\"x\"}}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(_webRoot);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "",
                ["Jwt:SigningKey"] = TestJwt.SigningKey
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}

public class SpaHostingTests : IClassFixture<SpaFactory>
{
    private readonly HttpClient _client;

    public SpaHostingTests(SpaFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/specialties")]
    [InlineData("/specialties/12/edit")]
    [InlineData("/apiary")]
    [InlineData("/apiary/x")]
    [InlineData("/healthy")]
    [InlineData("/openapi-docs")]
    [InlineData("/Specialties")]
    public async Task Client_side_routes_get_index_html_anonymously(string path)
    {
        var response = await _client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(SpaFactory.IndexMarker, body);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/specialties/anything/else")]
    [InlineData("/API/does-not-exist")]
    [InlineData("/health")]
    [InlineData("/health/nothing")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/openapi")]
    public async Task Unknown_api_health_and_openapi_paths_never_return_index_html(string path)
    {
        var response = await _client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SpaFactory.IndexMarker, body);
        // Unchanged behaviour: the fallback authorization policy answers an anonymous caller with 401.
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Theory]
    [InlineData("/missing.js")]
    [InlineData("/assets/missing.css")]
    [InlineData("/specialties/1.2")]
    public async Task A_path_with_a_file_extension_is_never_index_html(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.DoesNotContain(SpaFactory.IndexMarker, await response.Content.ReadAsStringAsync());
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_still_work_and_are_json()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(SpaFactory.IndexMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Static_assets_are_served_anonymously_with_the_right_cache_headers()
    {
        var bundle = await _client.GetAsync("/main-5A5EEYQ4.js");
        var translations = await _client.GetAsync("/i18n/ar.json");
        var index = await _client.GetAsync("/index.html");
        var icon = await _client.GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", bundle.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, translations.StatusCode);
        Assert.Equal("no-cache", translations.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("no-cache", index.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
        Assert.Null(icon.Headers.CacheControl);
    }

    [Fact]
    public async Task A_post_to_an_unknown_path_does_not_get_index_html()
    {
        var response = await _client.PostAsync("/specialties", new StringContent("{}"));

        Assert.DoesNotContain(SpaFactory.IndexMarker, await response.Content.ReadAsStringAsync());
    }
}
