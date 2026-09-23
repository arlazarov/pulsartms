using Domain.Entities.Consistency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Consistency;

public sealed class ConsistencyFindingConfiguration
  : IEntityTypeConfiguration<ConsistencyFinding>
{
  public void Configure(EntityTypeBuilder<ConsistencyFinding> b)
  {
    b.ToTable("ConsistencyFindings");
    b.HasKey(x => x.Id);
    b.Property(x => x.Rule).HasMaxLength(80).IsRequired();
    b.Property(x => x.EntityKey).HasMaxLength(120).IsRequired();
    b.Property(x => x.State).HasMaxLength(20).IsRequired();
    b.Property(x => x.Condition).HasMaxLength(20).IsRequired();
    b.Property(x => x.Severity).HasMaxLength(20).IsRequired();
    b.Property(x => x.Versions).HasMaxLength(400).IsRequired();
    b.Property(x => x.EvidenceJson).HasMaxLength(4000).IsRequired();
    b.Property(x => x.LastRepairOutcome).HasMaxLength(40);
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Rule,
        x.EntityKey,
        x.Occurrence,
      })
      .IsUnique();
    // Two instances overlapping during a rollout cannot both open the same
    // finding; the loser's page is read again on its next pass.
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Rule,
        x.EntityKey,
      })
      .IsUnique()
      .HasFilter("\"State\" = 'open'")
      .HasDatabaseName("IX_ConsistencyFindings_Open");
    b.HasIndex(x => new
    {
      x.CompanyId,
      x.State,
      x.Rule,
    });
  }
}

public sealed class ConsistencyEventConfiguration
  : IEntityTypeConfiguration<ConsistencyEvent>
{
  public void Configure(EntityTypeBuilder<ConsistencyEvent> b)
  {
    b.ToTable("ConsistencyEvents");
    b.HasKey(x => x.Id);
    b.Property(x => x.Id).ValueGeneratedOnAdd();
    b.Property(x => x.Kind).HasMaxLength(40).IsRequired();
    b.Property(x => x.Rule).HasMaxLength(80).IsRequired();
    b.Property(x => x.EntityKey).HasMaxLength(120).IsRequired();
    b.Property(x => x.DetailJson).HasMaxLength(2000).IsRequired();
    b.HasIndex(x => new { x.CompanyId, x.Sequence }).IsUnique();
    b.HasIndex(x => x.FindingId);
  }
}

public sealed class ConsistencyIncidentConfiguration
  : IEntityTypeConfiguration<ConsistencyIncident>
{
  public void Configure(EntityTypeBuilder<ConsistencyIncident> b)
  {
    b.ToTable("ConsistencyIncidents");
    b.HasKey(x => x.Id);
    b.Property(x => x.Rule).HasMaxLength(80).IsRequired();
    b.Property(x => x.EntityKey).HasMaxLength(120).IsRequired();
    b.Property(x => x.Reason).HasMaxLength(40).IsRequired();
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Rule,
        x.EntityKey,
      })
      .IsUnique();
    b.HasIndex(x => new { x.CompanyId, x.LastAt });
  }
}

public sealed class ConsistencyJournalHeadConfiguration
  : IEntityTypeConfiguration<ConsistencyJournalHead>
{
  public void Configure(EntityTypeBuilder<ConsistencyJournalHead> b)
  {
    b.ToTable("ConsistencyJournalHeads");
    b.HasKey(x => x.CompanyId);
  }
}
