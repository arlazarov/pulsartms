namespace Application.Features.Dispatch.Services;

// The machine-readable line a blocked correction adds after its sentence,
// so the load page can link the load that blocks it. The client reads the
// same prefix (DispatchCorrectionBlock) and shows only the sentence.
public static class DispatchCorrectionErrors
{
  public const string BlockedByPrefix = "blocked-by:";

  public static string BlockedBy(Guid dispatchId, int loadNumber) =>
    $"{BlockedByPrefix}{dispatchId}:{loadNumber}";
}
