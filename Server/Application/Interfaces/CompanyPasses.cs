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
    foreach (var company in await roster.ActiveAsync(ct))
    {
      ct.ThrowIfCancellationRequested();
      using var serving = current.As(company);
      await pass(ct);
    }
  }
}
