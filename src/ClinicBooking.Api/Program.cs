using ClinicBooking.Api.Middleware;
using ClinicBooking.Api.Services;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
builder.Services.AddControllers();
builder.Services.AddErrorHandling();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
    context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug
    : exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
    : LogEventLevel.Information);
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ClinicBooking.Infrastructure.DependencyInjection.ReadyTag),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

public partial class Program;
