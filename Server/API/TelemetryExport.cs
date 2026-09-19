using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace API;

public static class TelemetryExport
{
  public const string EndpointSetting = "OTEL_EXPORTER_OTLP_ENDPOINT";
  public const string ServiceName = "amftms-api";

  // Metrics only: HTTP client traces would carry provider URLs, whose query strings can hold keys.
  public static IServiceCollection AddTelemetryExport(this IServiceCollection services, IConfiguration configuration)
  {
    if (string.IsNullOrWhiteSpace(configuration[EndpointSetting])) return services;
    services.AddOpenTelemetry()
      .ConfigureResource(resource => resource.AddService(ServiceName))
      .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(Application.Diagnostics.ApplicationMeter.Name)
        .AddOtlpExporter());
    return services;
  }
}
