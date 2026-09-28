namespace Client.Pages.Dispatch;

// The load a stop correction was blocked by, as the server names it in a
// line after its sentence (DispatchCorrectionErrors on the server).
public sealed record DispatchCorrectionBlock(Guid DispatchId, int LoadNumber)
{
  private const string Prefix = "blocked-by:";

  public static (string Message, DispatchCorrectionBlock? Block) Read(
    IEnumerable<string> errors
  )
  {
    DispatchCorrectionBlock? block = null;
    var shown = new List<string>();
    foreach (var error in errors)
    {
      var parts = error.StartsWith(Prefix, StringComparison.Ordinal)
        ? error[Prefix.Length..].Split(':')
        : null;
      if (
        parts is [var id, var number]
        && Guid.TryParse(id, out var dispatch)
        && int.TryParse(number, out var load)
      )
        block = new(dispatch, load);
      else
        shown.Add(error);
    }
    return (string.Join(" ", shown), block);
  }
}
