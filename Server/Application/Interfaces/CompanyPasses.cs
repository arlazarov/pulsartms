using System.Runtime.ExceptionServices;

namespace Application.Interfaces;

public static class CompanyPasses
{
  // Running one pass of clock-driven work, once per carrier.
  //
  // Queue-driven work takes its carrier from the row it claimed. This is
  // for the rest: refreshing forecasts, pulling hours, capturing
  // odometers. There is no row to take a carrier from, so the pass is run
  // once for each of them, and each run sees only that carrier's work.
  //
  // A host with no notion of carriers runs the pass once, as itself.
  public static async Task ForEachCompanyAsync(
    IServiceProvider services,
    Func<CancellationToken, Task> pass,
    CancellationToken ct
  )
  {
    var current =
      services.GetService(typeof(ICurrentCompany)) as ICurrentCompany;
    var roster = services.GetService(typeof(ICompanyRoster)) as ICompanyRoster;
    if (current is null || roster is null)
    {
      await pass(ct);
      return;
    }
    // One carrier's failure does not cost the carriers after it their turn.
    // The failure still reaches the caller, after every carrier has been
    // served, so it is logged once at the job's own boundary; one failure is
    // rethrown as it was, so a caller's filters (planning busy, for one)
    // still recognise it.
    List<ExceptionDispatchInfo>? failures = null;
    foreach (var company in await roster.ActiveAsync(ct))
    {
      ct.ThrowIfCancellationRequested();
      using var serving = current.As(company);
      try
      {
        await pass(ct);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception failure)
      {
        (failures ??= []).Add(ExceptionDispatchInfo.Capture(failure));
      }
    }
    if (failures is [var only])
      only.Throw();
    if (failures is { Count: > 1 })
      throw new AggregateException(failures.Select(x => x.SourceException));
  }
}
