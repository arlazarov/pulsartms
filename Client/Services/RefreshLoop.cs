namespace Client.Services;

public static class RefreshLoop
{
  public static async Task RunAsync(
    Func<CancellationToken, Task> refresh,
    Func<Exception, Task> onError,
    TimeSpan interval,
    CancellationToken cancellationToken,
    TimeProvider? clock = null,
    IPageVisibility? visibility = null)
  {
    using var timer = new PeriodicTimer(interval, clock ?? TimeProvider.System);
    using var pace = visibility?.Pace(timer, interval);
    try
    {
      do
      {
        try
        {
          await refresh(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          break;
        }
        catch (Exception ex) when (ex is HttpRequestException
          or OperationCanceledException or System.Text.Json.JsonException
          or Microsoft.JSInterop.JSException)
        {
          await onError(ex);
        }
      } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
  }
}
