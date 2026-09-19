using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence.Configurations.Execution;

public sealed class ExecutionLegStopConfiguration
  : IEntityTypeConfiguration<ExecutionLegStop>
{
  public void Configure(EntityTypeBuilder<ExecutionLegStop> builder)
  {
    builder.ToTable("ExecutionLegStops");
    builder.HasKey(x => new { x.ExecutionLegId, x.Id });
    builder.Property(x => x.Id).ValueGeneratedNever();
    builder.HasIndex(x => new { x.ExecutionLegId, x.Position });
    builder.HasIndex(x => x.DispatchId);
    var utc = new ValueConverter<DateTime, DateTime>(
      value => value,
      value => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    );
    foreach (var property in builder.Metadata.GetProperties())
      if (property.ClrType == typeof(DateTime?))
        property.SetValueConverter(utc);
  }
}
