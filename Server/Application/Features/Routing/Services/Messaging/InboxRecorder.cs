using Domain.Entities.Messaging;
using Domain.Models.Messaging;

namespace Application.Features.Routing.Services.Messaging;

// Records what drivers wrote, in the webhook's own transaction. A message is
// keyed by its provider id under the company, channel and business number,
// so a notification delivered twice records it once; two deliveries racing
// conflict on that key and the provider's retry finds it recorded. A file
// becomes a pending attachment for the media worker, which must copy it
// before the provider's media id expires. Each message raises the company's
// arrival sequence in the same transaction; a concurrent recording that
// read the same sequence fails and the provider's retry records it after.
public sealed class InboxRecorder(IAppDbContext db, TimeProvider clock)
{
  // WhatsApp keeps a received media id for seven days.
  public static readonly TimeSpan MediaLifetime = TimeSpan.FromDays(7);

  public const string Received = "received";

  // Conversations that gained a message, for notification after commit.
  public async Task<IReadOnlyList<Guid>> RecordAsync(
    string channel,
    string businessNumberId,
    IReadOnlyList<DriverMessageInboundEvent> inbound,
    CancellationToken ct
  )
  {
    var events = inbound
      .Where(x => x.ProviderMessageId is not null)
      .DistinctBy(x => x.ProviderMessageId)
      .OrderBy(x => x.At)
      .ToList();
    if (events.Count == 0 || businessNumberId.Length == 0)
      return [];
    var ids = events.Select(x => x.ProviderMessageId!).ToArray();
    var known = await db
      .ConversationMessages.AsNoTracking()
      .Where(x =>
        x.Channel == channel
        && x.BusinessNumberId == businessNumberId
        && ids.Contains(x.ProviderMessageId!)
      )
      .Select(x => x.ProviderMessageId!)
      .ToListAsync(ct);
    events.RemoveAll(x => known.Contains(x.ProviderMessageId!));
    if (events.Count == 0)
      return [];
    var phones = events.Select(x => x.Phone).Distinct().ToArray();
    var conversations = await db
      .Conversations.Where(x =>
        x.Channel == channel
        && x.BusinessNumberId == businessNumberId
        && phones.Contains(x.Participant)
      )
      .ToDictionaryAsync(x => x.Participant, ct);
    var drivers = (
      await db
        .Drivers.AsNoTracking()
        .Where(x => x.WhatsAppPhone != null && phones.Contains(x.WhatsAppPhone))
        .Select(x => new { x.Id, Phone = x.WhatsAppPhone! })
        .ToListAsync(ct)
    )
      .GroupBy(x => x.Phone)
      .Where(x => x.Count() == 1)
      .ToDictionary(x => x.Key, x => x.Single().Id);
    var now = clock.GetUtcNow().UtcDateTime;
    var changed = new HashSet<Guid>();
    ConversationArrivalHead? head = null;
    foreach (var item in events)
    {
      if (!conversations.TryGetValue(item.Phone, out var conversation))
      {
        conversation = new Conversation
        {
          Id = Guid.NewGuid(),
          Channel = channel,
          BusinessNumberId = businessNumberId,
          Participant = item.Phone,
          DriverId = Driver(drivers, item.Phone),
          LastMessageAt = item.At,
        };
        db.Conversations.Add(conversation);
        conversations[item.Phone] = conversation;
      }
      conversation.DriverId ??= Driver(drivers, item.Phone);
      var message = new ConversationMessage
      {
        Id = Guid.NewGuid(),
        ConversationId = conversation.Id,
        Channel = channel,
        BusinessNumberId = businessNumberId,
        Direction = MessageDirections.Inbound,
        Kind = item.Kind switch
        {
          "file" => ConversationMessageKinds.File,
          "unsupported" => ConversationMessageKinds.Unsupported,
          _ => ConversationMessageKinds.Text,
        },
        Body = item.Text,
        ProviderMessageId = item.ProviderMessageId,
        Status = Received,
        StatusAt = now,
        SentAt = item.At,
        CreatedAt = now,
      };
      db.ConversationMessages.Add(message);
      if (item.Media is { } media)
        db.MessageAttachments.Add(
          new MessageAttachment
          {
            Id = Guid.NewGuid(),
            MessageId = message.Id,
            ProviderMediaId = media.Id,
            MediaExpiresAt = now + MediaLifetime,
            DeclaredType = media.MimeType,
            OriginalName = media.FileName ?? "",
            Caption = item.Text,
            NextAttemptAt = now,
            CreatedAt = now,
          }
        );
      if (conversation.LastInboundAt is not { } last || item.At > last)
        conversation.LastInboundAt = item.At;
      if (item.At >= conversation.LastMessageAt)
      {
        conversation.LastMessageAt = item.At;
        conversation.LastMessageId = message.Id;
        conversation.LastPreview = Preview(message, item);
      }
      conversation.Revision++;
      message.ArrivedRevision = conversation.Revision;
      conversation.LastInboundRevision = conversation.Revision;
      head ??= await ArrivalHeadAsync(ct);
      head.Sequence++;
      conversation.LastInboundSequence = head.Sequence;
      changed.Add(conversation.Id);
    }
    return [.. changed];
  }

  // A Guid dictionary answers Guid.Empty for a missing key; no match must
  // stay null.
  private static Guid? Driver(Dictionary<string, Guid> drivers, string phone) =>
    drivers.TryGetValue(phone, out var id) ? id : null;

  private static string Preview(
    ConversationMessage message,
    DriverMessageInboundEvent item
  )
  {
    var text = message.Kind switch
    {
      ConversationMessageKinds.File => item.Media?.FileName
        is { Length: > 0 } name
        ? name
      : item.Text.Length > 0 ? item.Text
      : "File",
      ConversationMessageKinds.Unsupported => "A message PulsR cannot show",
      _ => item.Text,
    };
    return text.Length <= 200 ? text : text[..200];
  }

  private async Task<ConversationArrivalHead> ArrivalHeadAsync(
    CancellationToken ct
  )
  {
    var head = await db.ConversationArrivalHeads.SingleOrDefaultAsync(ct);
    if (head is null)
    {
      head = new ConversationArrivalHead { Id = Guid.NewGuid() };
      db.ConversationArrivalHeads.Add(head);
    }
    return head;
  }
}
