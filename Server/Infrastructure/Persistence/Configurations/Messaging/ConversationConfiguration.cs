using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Messaging;

public sealed class ConversationConfiguration
  : IEntityTypeConfiguration<Conversation>
{
  public void Configure(EntityTypeBuilder<Conversation> b)
  {
    b.ToTable("Conversations");
    b.HasKey(x => x.Id);
    b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
    b.Property(x => x.BusinessNumberId).HasMaxLength(64).IsRequired();
    b.Property(x => x.Participant).HasMaxLength(16).IsRequired();
    b.Property(x => x.LastPreview).HasMaxLength(200).IsRequired();
    b.Property(x => x.Revision).IsConcurrencyToken();
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Channel,
        x.BusinessNumberId,
        x.Participant,
      })
      .IsUnique();
    b.HasIndex(x => new { x.CompanyId, x.LastMessageAt });
  }
}

public sealed class ConversationMessageConfiguration
  : IEntityTypeConfiguration<ConversationMessage>
{
  public void Configure(EntityTypeBuilder<ConversationMessage> b)
  {
    b.ToTable("ConversationMessages");
    b.HasKey(x => x.Id);
    b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
    b.Property(x => x.BusinessNumberId).HasMaxLength(64).IsRequired();
    b.Property(x => x.Direction).HasMaxLength(8).IsRequired();
    b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
    b.Property(x => x.Body).HasMaxLength(4096).IsRequired();
    b.Property(x => x.Template).HasMaxLength(2000);
    b.Property(x => x.ProviderMessageId).HasMaxLength(200);
    b.Property(x => x.Status).HasMaxLength(32).IsRequired();
    // A notification delivered twice is one message; the same provider id
    // under another business number is another message.
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Channel,
        x.BusinessNumberId,
        x.ProviderMessageId,
      })
      .IsUnique()
      .HasFilter("\"ProviderMessageId\" IS NOT NULL");
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Channel,
        x.BusinessNumberId,
        x.IdempotencyKey,
        x.Attempt,
      })
      .IsUnique()
      .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    b.HasIndex(x => new { x.ConversationId, x.SentAt });
    b.HasOne<Conversation>()
      .WithMany()
      .HasForeignKey(x => x.ConversationId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}

public sealed class MessageAttachmentConfiguration
  : IEntityTypeConfiguration<MessageAttachment>
{
  public void Configure(EntityTypeBuilder<MessageAttachment> b)
  {
    b.ToTable("MessageAttachments");
    b.HasKey(x => x.Id);
    b.Property(x => x.ProviderMediaId).HasMaxLength(200);
    b.Property(x => x.DeclaredType).HasMaxLength(100).IsRequired();
    b.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
    b.Property(x => x.Caption).HasMaxLength(1024).IsRequired();
    b.Property(x => x.State).HasMaxLength(20).IsRequired();
    b.Property(x => x.FailureReason).HasMaxLength(200);
    b.HasIndex(x => x.MessageId);
    b.HasIndex(x => new { x.State, x.NextAttemptAt });
    b.HasOne<ConversationMessage>()
      .WithMany()
      .HasForeignKey(x => x.MessageId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}

public sealed class ConversationReadConfiguration
  : IEntityTypeConfiguration<ConversationRead>
{
  public void Configure(EntityTypeBuilder<ConversationRead> b)
  {
    b.ToTable("ConversationReads");
    b.HasKey(x => x.Id);
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.ConversationId,
        x.UserId,
      })
      .IsUnique();
    b.HasOne<Conversation>()
      .WithMany()
      .HasForeignKey(x => x.ConversationId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
