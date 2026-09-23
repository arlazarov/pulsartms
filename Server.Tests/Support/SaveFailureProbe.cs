using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

// Makes the next save fail as a lost race would, before anything is
// written.
internal sealed class SaveFailureProbe : SaveChangesInterceptor
{
  public bool FailNextSave { get; set; }

  public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
    DbContextEventData eventData,
    InterceptionResult<int> result,
    CancellationToken cancellationToken = default
  )
  {
    if (FailNextSave)
    {
      FailNextSave = false;
      throw new DbUpdateException("Save failed.");
    }
    return ValueTask.FromResult(result);
  }
}
