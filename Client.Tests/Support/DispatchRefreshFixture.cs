using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Support;

internal sealed class DispatchRefreshFixture : IDisposable, IAsyncDisposable
{
  private readonly Channel<TaskCompletionSource<HttpResponseMessage>> requests =
    Channel.CreateUnbounded<TaskCompletionSource<HttpResponseMessage>>();
  private readonly Channel<
    TaskCompletionSource<HttpResponseMessage>
  > telemetryRequests = Channel.CreateUnbounded<
    TaskCompletionSource<HttpResponseMessage>
  >();
  private readonly TaskCompletionSource initialTelemetry = new(
    TaskCreationOptions.RunContinuationsAsynchronously
  );
  public Task InitialTelemetryRequested => initialTelemetry.Task;
  public FakeTimeProvider Clock { get; } =
    new(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
  public Guid Truck { get; } = Guid.NewGuid();
  public DispatchResponse Load { get; }
  public ClientComponentContext Context { get; }
  public bool DeferBoard { get; set; }
  public bool DeferTelemetry { get; set; }

  public DispatchRefreshFixture()
  {
    Load = new()
    {
      Id = Guid.NewGuid(),
      TruckId = Truck,
      LoadNumber = 1373,
      DriverName = "Fixture driver",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pickup",
          Address = "100 Main St",
          City = "Toronto",
          Province = "ON",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Delivery",
          Address = "200 Main St",
          City = "Ottawa",
          Province = "ON",
        },
      ],
    };
    Load.Eta = Forecast();
    var initial = Load.Eta;
    Context = new ClientComponentContext(
      (request, ct) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/dispatch/board/telemetry")
          initialTelemetry.TrySetResult();
        if (path == "/api/dispatch/board/telemetry" && DeferTelemetry)
        {
          var reply = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
          );
          telemetryRequests.Writer.TryWrite(reply);
          return reply.Task.WaitAsync(ct);
        }
        if (path == "/api/dispatch/board")
        {
          if (
            !DeferBoard
            || request.RequestUri.Query.Contains("includeEta=true")
            || request.RequestUri.Query.Contains("includeFinancials=true")
          )
            return Task.FromResult(Board());
          var reply = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
          );
          requests.Writer.TryWrite(reply);
          return reply.Task.WaitAsync(ct);
        }
        if (path == "/api/dispatch/board/planning")
          return Task.FromResult(
            Ok(
              new[]
              {
                new AutomaticPlanningResult(
                  Truck,
                  Load.Id,
                  Load.LoadNumber,
                  new(
                    new(),
                    new()
                    {
                      Id = Load.Id,
                      TruckId = Truck,
                      DispatchId = Load.Id,
                      Version = 1,
                    },
                    null,
                    null,
                    null,
                    true
                  )
                  {
                    Eta = initial,
                  },
                  null
                ),
              }
            )
          );
        return Task.FromResult(Ok(Array.Empty<object>()));
      }
    );
    Context.Services.AddSingleton<TimeProvider>(Clock);
    Context.JSInterop.Mode = JSRuntimeMode.Loose;
  }

  public DispatchEta Forecast(int delayMinutes = 0) =>
    new(
      Clock.GetUtcNow().UtcDateTime,
      Clock.GetUtcNow().AddMinutes(2).UtcDateTime,
      Load.Stops.Select(stop => new StopEta(
          stop.Id,
          Clock.GetUtcNow().AddHours(stop.Sequence).AddMinutes(delayMinutes),
          "UTC",
          null,
          delayMinutes,
          60,
          0
        )
        {
          DispatchId = Load.Id,
        })
        .ToArray(),
      null,
      []
    );

  public HttpResponseMessage Board() =>
    Ok(
      new
      {
        items = new[]
        {
          new
          {
            key = Truck.ToString(),
            truckId = Truck,
            truckNumber = "54777",
            dispatches = new[] { Load },
          },
        },
        page = 1,
        pageSize = 12,
        totalCount = 1,
        totalPages = 1,
      }
    );

  public IRenderedComponent<DispatchList> Render() =>
    Context.Render<DispatchList>();

  public async Task<TaskCompletionSource<HttpResponseMessage>> NextRequest() =>
    await requests
      .Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));

  public async Task<
    TaskCompletionSource<HttpResponseMessage>
  > NextTelemetryRequest() =>
    await telemetryRequests
      .Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));

  private static HttpResponseMessage Ok(object value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response = value }),
    };

  public void Dispose() => Context.Dispose();

  public ValueTask DisposeAsync() => Context.DisposeAsync();
}
