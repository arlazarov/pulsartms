using Application.Features.Mileage.Interfaces;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Mileage.Background;

public sealed class OdometerCaptureOperation(
  IServiceScopeFactory scopes,
  TimeProvider clock,
  ILogger<OdometerCaptureOperation> logger
) : IOdometerCaptureOperation
{
  public async Task RunAsync(CancellationToken stoppingToken)
  {
    var owner = Guid.NewGuid().ToString("N");
    var failures = 0;
    var nextProviderRead = DateTime.MinValue;
    while (!stoppingToken.IsCancellationRequested)
    {
      var delay = TimeSpan.FromMinutes(2);
      try
      {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
          stoppingToken
        );
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (
          await services
            .GetRequiredService<IOdometerCaptureLease>()
            .AcquireAsync(owner, clock.GetUtcNow().UtcDateTime, timeout.Token)
        )
        {
          // A carrier's own cursor, a carrier's own intervals. The lease
          // above is the server's - only one instance captures at a time -
          // but what is captured belongs to whoever is being served, so
          // the capture is run once for each of them.
          await CompanyPasses.ForEachCompanyAsync(
            services,
            async token =>
            {
              var recorder =
                services.GetRequiredService<IAutomaticMileageRecorder>();
              var cursor = await recorder.ReadOdometerCursorAsync(token);
              var current = await recorder.CaptureOdometerAsync(
                cursor,
                new([], cursor ?? "", false),
                token
              );
              if (current && clock.GetUtcNow().UtcDateTime >= nextProviderRead)
              {
                var page = await services
                  .GetRequiredService<IOdometerFeedProvider>()
                  .ReadAsync(cursor, token);
                var accepted = await recorder.CaptureOdometerAsync(
                  cursor,
                  page,
                  token
                );
                if (accepted && page.HasMore)
                  delay = TimeSpan.FromSeconds(1);
                failures = 0;
              }
            },
            timeout.Token
          );
        }
      }
      catch (OperationCanceledException)
        when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (OperationCanceledException)
      {
        failures++;
        Backoff();
      }
      catch (Exception ex)
      {
        if (failures++ == 0)
          logger.LogWarning(
            ex,
            "Odometer capture {OperationId} will retry",
            owner
          );
        Backoff();
      }
      await Task.Delay(delay, stoppingToken);

      void Backoff() =>
        nextProviderRead = clock
          .GetUtcNow()
          .UtcDateTime.AddMinutes(Math.Min(15, failures * 2));
    }
  }
}
