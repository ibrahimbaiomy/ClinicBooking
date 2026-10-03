using System.Text.Json;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ClinicBooking.Tests;

/// <summary>
/// The committed <c>openapi.json</c> (D24) must match what the API generates. To refresh it after
/// changing a controller or DTO, run:
/// <c>UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests</c> (Git Bash) or set the
/// environment variable first (PowerShell: <c>$env:UPDATE_OPENAPI=1</c>), then commit the file.
/// </summary>
public class OpenApiDocumentTests : IClassFixture<ApiFactory>
{
    private const string RelativePath = "src/clinic-booking-web/src/api/openapi.json";

    private readonly ApiFactory _factory;

    public OpenApiDocumentTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_committed_openapi_json_matches_the_generated_document()
    {
        var generated = Normalize(await GenerateAsync());
        var path = Path.Combine(FindRepositoryRoot(), RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated, new System.Text.UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(path), $"{RelativePath} is missing. Generate it with: UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests");

        // Line endings are ignored, so a file generated on Windows passes on Linux.
        var committed = Normalize(await File.ReadAllTextAsync(path));
        Assert.True(
            committed == generated,
            $"{RelativePath} is out of date. Regenerate it with: UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests, then commit it.");
    }

    [Fact]
    public async Task The_document_is_machine_independent_and_exposes_dtos_not_entities()
    {
        using var document = JsonDocument.Parse(await GenerateAsync());
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("servers", out _), "servers would carry the caller's host");
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/specialties", out _));
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/specialties/{id}", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("SpecialtyResponse", schemas);
        Assert.Contains("CreateSpecialtyRequest", schemas);
        Assert.DoesNotContain("Specialty", schemas);
        Assert.DoesNotContain("RefreshToken", schemas);
        Assert.DoesNotContain("ApplicationUser", schemas);

        Assert.DoesNotContain("text/plain", root.GetRawText());
    }

    [Fact]
    public async Task The_document_is_not_served_unless_OpenApi_Enabled_is_true()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> GenerateAsync()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["OpenApi:Enabled"] = "true" })));
        using var client = factory.CreateClient();

        return await client.GetStringAsync("/openapi/v1.json");
    }

    // LF endings and a final newline, whatever the platform wrote.
    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicBooking.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("ClinicBooking.sln not found above the test output directory.");
    }
}
