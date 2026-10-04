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
public class OpenApiDocumentTests : IClassFixture<PlainApiFactory>
{
    private const string RelativePath = "src/clinic-booking-web/src/api/openapi.json";

    private readonly PlainApiFactory _factory;

    public OpenApiDocumentTests(PlainApiFactory factory)
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
    public async Task The_document_contains_no_test_only_endpoints_or_schemas()
    {
        using var document = JsonDocument.Parse(await GenerateAsync());
        var root = document.RootElement;

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.All(paths, path => Assert.StartsWith("/api/", path));

        var schemas = root.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain("BodyDto", schemas);
        Assert.DoesNotContain("ValidatedBody", schemas);
    }

    [Fact]
    public async Task Every_operation_documents_a_success_response_with_a_schema_except_204()
    {
        using var document = JsonDocument.Parse(await GenerateAsync());
        var missing = new List<string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var responses = operation.Value.GetProperty("responses").EnumerateObject()
                    .Where(r => r.Name.StartsWith('2'))
                    .ToList();

                var label = $"{operation.Name.ToUpperInvariant()} {path.Name}";
                if (responses.Count == 0)
                {
                    missing.Add($"{label}: no 2xx response");
                    continue;
                }

                foreach (var response in responses.Where(r => r.Name != "204"))
                {
                    var hasSchema = response.Value.TryGetProperty("content", out var content)
                        && content.TryGetProperty("application/json", out var json)
                        && json.TryGetProperty("schema", out _);
                    if (!hasSchema)
                    {
                        missing.Add($"{label}: {response.Name} has no JSON schema");
                    }
                }
            }
        }

        Assert.True(missing.Count == 0, "Undocumented success responses: " + string.Join("; ", missing));
    }

    [Fact]
    public async Task The_clinics_endpoints_and_dtos_are_documented_without_entity_internals()
    {
        using var document = JsonDocument.Parse(await GenerateAsync());
        var root = document.RootElement;

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/clinics", out var collection));
        Assert.True(paths.TryGetProperty("/api/clinics/{id}", out var item));
        Assert.True(collection.TryGetProperty("get", out _) && collection.TryGetProperty("post", out _));
        Assert.True(item.TryGetProperty("get", out _) && item.TryGetProperty("put", out _) && item.TryGetProperty("delete", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "ClinicResponse", "CreateClinicRequest", "UpdateClinicRequest", "PagedResponseOfClinicResponse" })
        {
            Assert.True(schemas.TryGetProperty(name, out _), $"{name} is missing");
        }

        var properties = schemas.GetProperty("ClinicResponse").GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("address", properties);
        Assert.Contains("phone", properties);
        Assert.Contains("rowVersion", properties);
        Assert.DoesNotContain(properties, p => p.Contains("Normalized", StringComparison.OrdinalIgnoreCase) || p.Contains("IsDeleted", StringComparison.OrdinalIgnoreCase));
        Assert.False(schemas.TryGetProperty("Clinic", out _));
    }

    [Fact]
    public async Task The_auth_success_schemas_are_documented()
    {
        using var document = JsonDocument.Parse(await GenerateAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("AuthResponse", schemas);
        Assert.Contains("CurrentUserResponse", schemas);
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
