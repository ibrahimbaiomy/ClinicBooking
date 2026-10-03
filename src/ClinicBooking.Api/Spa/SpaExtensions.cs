using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace ClinicBooking.Api.Spa;

/// <summary>
/// Serves the built Angular app from <c>wwwroot</c> with a SPA fallback (D15). Static files are served
/// by middleware placed before authorization, so the fallback authorization policy (D50) never sees
/// them; the fallback endpoint is explicitly anonymous.
/// </summary>
public static partial class SpaExtensions
{
    private const string IndexFile = "index.html";

    // A catch-all route that does NOT match: a path whose first segment is exactly api, health or
    // openapi (whole segment: /apiary still matches), or any path with a file extension (a missing
    // /x.js must not become HTML). Everything excluded keeps its normal behaviour: ProblemDetails
    // 401 or 404, never index.html.
    private const string FallbackPattern = "{*path:regex(^(?!(api|health|openapi)(/|$))[^.]*$)}";

    /// <summary>Static files, placed first in the pipeline.</summary>
    public static IApplicationBuilder UseSpaStaticFiles(this IApplicationBuilder app) =>
        app.UseStaticFiles(CreateOptions());

    /// <summary>Anonymous <c>index.html</c> for client-side routes such as <c>/specialties</c>.</summary>
    public static IEndpointRouteBuilder MapSpaFallback(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFallbackToFile(FallbackPattern, IndexFile, CreateOptions()).AllowAnonymous();

        // A catch-all with a constraint does not match the empty path, so the root is mapped itself.
        endpoints.MapFallbackToFile("/", IndexFile, CreateOptions()).AllowAnonymous();

        return endpoints;
    }

    private static StaticFileOptions CreateOptions() => new()
    {
        OnPrepareResponse = context =>
        {
            var headers = context.Context.Response.Headers;
            var path = context.Context.Request.Path;

            // The shell and the translations must be revalidated on every load; fingerprinted bundles
            // never change under the same name.
            if (context.File.Name == IndexFile || path.StartsWithSegments("/i18n"))
            {
                headers.CacheControl = "no-cache";
            }
            else if (FingerprintedAsset().IsMatch(context.File.Name))
            {
                headers.CacheControl = "public,max-age=31536000,immutable";
            }
        }
    };

    // main-5A5EEYQ4.js, styles-X6HCRQRZ.css, chunk-AB12CD34.js
    [GeneratedRegex(@"-[A-Za-z0-9]{8}\.(js|css)$")]
    private static partial Regex FingerprintedAsset();
}
