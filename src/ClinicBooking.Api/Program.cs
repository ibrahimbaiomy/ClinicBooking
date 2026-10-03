using ClinicBooking.Api.Authentication;
using ClinicBooking.Api.Filters;
using ClinicBooking.Api.Middleware;
using ClinicBooking.Api.OpenApi;
using ClinicBooking.Api.Services;
using ClinicBooking.Api.Spa;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, logger) => logger
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddCurrentUser();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());
builder.Services.AddErrorHandling();
builder.Services.AddApiAuth(builder.Configuration);
builder.Services.AddApiOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
}

await app.Services.SeedInitialUserAsync();

// Static files first: they skip logging, correlation and authorization (the SPA shell is public).
app.UseSpaStaticFiles();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
    context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug
    : exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
    : LogEventLevel.Information);
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapApiOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ClinicBooking.Infrastructure.DependencyInjection.ReadyTag),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapSpaFallback();

app.Run();

public partial class Program;
