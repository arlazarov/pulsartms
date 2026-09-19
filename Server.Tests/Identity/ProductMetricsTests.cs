using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Application.Behaviors;
using Application.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class ProductMetricsTests
{
  private sealed record ProductMetricRequest;

  [Fact]
  public async Task RequestHistogramUsesTheCanonicalProductIdentity()
  {
    var measurements =
      new ConcurrentQueue<(string Meter, string Name, string? Unit)>();
    using var listener = new MeterListener();
    listener.InstrumentPublished = (instrument, source) =>
    {
      if (instrument.Meter.Name == "PulsarTms.Application")
        source.EnableMeasurementEvents(instrument);
    };
    listener.SetMeasurementEventCallback<double>(
      (instrument, _, tags, _) =>
      {
        foreach (var tag in tags)
          if (
            tag.Key == "request"
            && Equals(tag.Value, nameof(ProductMetricRequest))
          )
            measurements.Enqueue(
              (instrument.Meter.Name, instrument.Name, instrument.Unit)
            );
      }
    );
    listener.Start();

    await new RequestDiagnosticsBehavior<
      ProductMetricRequest,
      RequestResponse<int>
    >(
      NullLogger<
        RequestDiagnosticsBehavior<ProductMetricRequest, RequestResponse<int>>
      >.Instance
    ).Handle(new(), _ => Task.FromResult(RequestResponse<int>.Ok(1)), default);

    Assert.Equal(
      ("PulsarTms.Application", "pulsartms.request.duration", "ms"),
      Assert.Single(measurements)
    );
  }
}
