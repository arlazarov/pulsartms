using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchStopCompletionEventConfiguration
  : IEntityTypeConfiguration<DispatchStopCompletionEvent>
{
  public void Configure(EntityTypeBuilder<DispatchStopCompletionEvent> builder)
  {
    builder.HasKey(x => x.Id);
    // Audit identities survive provider removal of a stop or deletion of an
    // account.
    builder.HasIndex(x => new { x.StopId, x.Revision }).IsUnique();
  }
}
