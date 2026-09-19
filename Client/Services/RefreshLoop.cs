using System.Text.Json;
using Microsoft.JSInterop;

namespace Client.Services;

public static class RefreshLoop
{
  public static async Task RunAsync(
    Func<CancellationToken, Task> refresh,
    Func<Exception, Task> onError,
    TimeSpan interval,
    CancellationToken cancellationToken,
    TimeProvider? clock = null,
    PageVisibility? visibility = null,
    bool runImmediately = true
  )
  {
    var first = true;
    var timer = new PeriodicTimer(interval, clock ?? TimeProvider.System);
    try
    {
      do
      {
        var changed = visibility?.Changed ?? CancellationToken.None;
        try
        {
          if ((!first || runImmediately) && visibility?.IsVisible != false)
            await refresh(cancellationToken);
          first = false;
        }
        catch (OperationCanceledException)
          when (cancellationToken.IsCancellationRequested)
        {
          break;
        }
        catch (Exception ex)
          when (ex
              is HttpRequestException
                or OperationCanceledException
                or JsonException
                or JSException
          )
        {
          await onError(ex);
        }
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken,
          changed
        );
        try
        {
          if (!await timer.WaitForNextTickAsync(waiting.Token))
            break;
        }
        catch (OperationCanceledException)
          when (!cancellationToken.IsCancellationRequested
            && changed.IsCancellationRequested
          )
        {
          timer.Dispose();
          timer = new PeriodicTimer(interval, clock ?? TimeProvider.System);
        }
        cancellationToken.ThrowIfCancellationRequested();
      } while (true);
    }
    catch (OperationCanceledException)
      when (cancellationToken.IsCancellationRequested) { }
    finally
    {
      timer.Dispose();
    }
  }
}
