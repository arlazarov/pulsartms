using Client.Models.DTO.Planning;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private CancellationTokenSource? _previewRequest;

  private async Task<AutomaticPlanningResult?> LoadSavedPreviewAsync(
    Guid truckId
  )
  {
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _previewRequest = request;
    try
    {
      return await PlanningCache
        .ReadPreviewAsync(truckId, request.Token)
        .WaitAsync(TimeSpan.FromSeconds(2), Clock, request.Token);
    }
    catch (TimeoutException)
    {
      await request.CancelAsync();
      return null;
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    {
      return null;
    }
    finally
    {
      if (ReferenceEquals(_previewRequest, request))
        _previewRequest = null;
    }
  }
}
