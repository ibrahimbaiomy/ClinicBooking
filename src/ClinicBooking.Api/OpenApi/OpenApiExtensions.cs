namespace ClinicBooking.Api.OpenApi;

public static class OpenApiExtensions
{
    public const string EnabledSetting = "OpenApi:Enabled";

    /// <summary>
    /// Registers the generator and a transformer that makes the document identical on every
    /// machine: no <c>servers</c> entry (it would carry the caller's host) and only the JSON
    /// media types we actually use (D24, D50).
    /// </summary>
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Servers = null;

            foreach (var path in document.Paths.Values)
            {
                if (path.Operations is null)
                {
                    continue;
                }

                foreach (var operation in path.Operations.Values)
                {
                    Prune(operation.RequestBody?.Content);

                    if (operation.Responses is null)
                    {
                        continue;
                    }

                    foreach (var response in operation.Responses.Values)
                    {
                        Prune(response.Content);
                    }
                }
            }

            return Task.CompletedTask;
        }));

        return services;
    }

    /// <summary>
    /// Serves <c>/openapi/v1.json</c> only when <c>OpenApi:Enabled</c> is true (Development, tests),
    /// never in Production. Anonymous, because the fallback policy would otherwise block it.
    /// </summary>
    public static WebApplication MapApiOpenApi(this WebApplication app)
    {
        if (string.Equals(app.Configuration[EnabledSetting], "true", StringComparison.OrdinalIgnoreCase))
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }

    // The framework lists text/plain, text/json and application/*+json next to application/json.
    private static void Prune(IDictionary<string, Microsoft.OpenApi.OpenApiMediaType>? content)
    {
        if (content is null)
        {
            return;
        }

        foreach (var mediaType in content.Keys.ToList())
        {
            if (mediaType != "application/json" && mediaType != "application/problem+json")
            {
                content.Remove(mediaType);
            }
        }
    }
}
