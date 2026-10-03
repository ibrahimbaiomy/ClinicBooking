using System.Globalization;
using System.Threading.RateLimiting;
using ClinicBooking.Api.Authorization;
using ClinicBooking.Api.Filters;
using ClinicBooking.Application.Features.Auth;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClinicBooking.Api.Authentication;

public static class AuthExtensions
{
    public const string LoginRateLimitPolicy = "login";
    public const string RefreshRateLimitPolicy = "refresh";
    public const string RateLimitedKey = "error.auth.rate_limited";

    /// <summary>JWT bearer authentication, permission policies, the login rate limiter and the origin check.</summary>
    public static IServiceCollection AddApiAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection("Auth"));
        services.Configure<OriginCheckOptions>(configuration.GetSection("Auth"));
        services.Configure<RateLimitSettings>(configuration.GetSection("RateLimiting"));

        // Fails at host start when the key is missing or too short. Messages never contain the key.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(
                JwtOptions.IsValid,
                "Jwt settings are invalid: Issuer and Audience are required and SigningKey must be at least "
                + JwtOptions.MinimumKeyBytes + " bytes (set Jwt__SigningKey from JWT_SIGNING_KEY).")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, timeProvider) =>
            {
                var jwt = jwtOptions.Value;

                // Keep "sub" as "sub": CurrentUser reads it directly.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),

                    // IdentityModel has no TimeProvider hook, so lifetime is checked here against
                    // the injected clock (tests move it). The API hands over DateTime values.
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = timeProvider.GetUtcNow();
                        var skew = parameters.ClockSkew;
                        return notBefore is { } nbf && expires is { } exp
                            && new DateTimeOffset(DateTime.SpecifyKind(nbf, DateTimeKind.Utc)) - skew <= now
                            && new DateTimeOffset(DateTime.SpecifyKind(exp, DateTimeKind.Utc)) + skew > now;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            // Secure by default: an endpoint with no authorization metadata needs a signed-in
            // user. Anonymous endpoints must say [AllowAnonymous] (login, refresh, logout) or
            // be mapped with .AllowAnonymous() (health, OpenAPI).
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission)));
            }
        });
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddScoped<SameOriginFilter>();
        services.AddRateLimiter(ConfigureRateLimiter);

        return services;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(LoginRateLimitPolicy, context => PerClientWindow(context, s => s.LoginPerMinute));
        options.AddPolicy(RefreshRateLimitPolicy, context => PerClientWindow(context, s => s.RefreshPerMinute));

        options.OnRejected = async (context, cancellationToken) =>
        {
            var http = context.HttpContext;
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                http.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = RateLimitedKey
                    }
                });
        };
    }

    // Partitioned by client address. Behind a proxy this needs forwarded headers (deploy step).
    private static RateLimitPartition<string> PerClientWindow(
        HttpContext context,
        Func<RateLimitSettings, int> limit)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit(settings),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    }
}

/// <summary>Bound from the "RateLimiting" section.</summary>
public sealed class RateLimitSettings
{
    public int LoginPerMinute { get; set; } = 10;

    public int RefreshPerMinute { get; set; } = 30;
}

/// <summary>Bound from the "Auth" section: extra allowed origins for refresh and logout (e.g. the Angular dev server).</summary>
public sealed class OriginCheckOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}
