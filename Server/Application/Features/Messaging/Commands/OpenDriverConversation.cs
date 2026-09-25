using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Commands;

// The drivers a dispatcher can start a chat with: those with a WhatsApp
// number of their own, never one taken from an ordinary phone. Search and
// After work as on the inbox; InChosenGroup narrows to the dispatcher's
// chosen driver group. WithoutConversation leaves out the drivers who
// already have a conversation on the number the company sends from, which
// the inbox lists above them.
public sealed record GetMessagingDriversQuery(
  string? Search = null,
  MessagingDriverCursor? After = null,
  bool InChosenGroup = false,
  bool WithoutConversation = false
) : IRequest<RequestResponse<MessagingDriversView>>;

public sealed record MessagingDriverCursor(string Name, Guid Id);

// ConversationId: the driver's conversation on the number the company
// sends from now, when there is one.
public sealed record MessagingDriver(
  Guid Id,
  string Name,
  string WhatsAppPhone,
  Guid? ConversationId
);

// Configured is false when the company has no WhatsApp number to send
// from; the drivers are still listed, and none can be opened.
public sealed record MessagingDriversView(
  IReadOnlyList<MessagingDriver> Drivers,
  bool Configured,
  bool More
)
{
  public MessagingDriverCursor? Next { get; init; }
}

// Opens the driver's conversation on the number the company sends from
// now, creating it when there is none. Choosing a driver sends nothing: a
// new conversation has no driver message, so no reply window and nothing
// unread, and its first message is an approved template. Two dispatchers
// choosing the same driver at once get the same conversation: the
// conversation's unique key refuses the second insert, which then reads
// the first.
public sealed record OpenDriverConversationCommand(Guid DriverId)
  : IRequest<RequestResponse<Guid>>;

public sealed class DriverConversations(
  IAppDbContext db,
  ICurrentUser caller,
  ICurrentCompany company,
  IDriverScope scope,
  IDriverMessaging messaging,
  MessagingEvents events,
  TimeProvider clock
)
  : IRequestHandler<
    GetMessagingDriversQuery,
    RequestResponse<MessagingDriversView>
  >,
    IRequestHandler<OpenDriverConversationCommand, RequestResponse<Guid>>
{
  public const int PageSize = 50;

  public async Task<RequestResponse<MessagingDriversView>> Handle(
    GetMessagingDriversQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return RequestResponse<MessagingDriversView>.Fail("Access denied.", 403);
    var term = request.Search?.Trim() ?? "";
    if (term.Length > InboxHandlers.MaximumSearch)
      return RequestResponse<MessagingDriversView>.Fail(
        $"Search for at most {InboxHandlers.MaximumSearch} characters.",
        400
      );
    var number = await messaging.BusinessNumberAsync(ct);
    var channel = messaging.Channel;
    var query = db
      .Drivers.AsNoTracking()
      .Where(x => x.IsActive && x.WhatsAppPhone != null);
    if (request.WithoutConversation && number is not null)
      query = query.Where(x =>
        !db.Conversations.Any(c =>
          c.Channel == channel
          && c.BusinessNumberId == number
          && c.Participant == x.WhatsAppPhone
        )
      );
    if (
      request.InChosenGroup
      && await scope.CurrentAsync(ct) is { IsAll: false } group
    )
    {
      var drivers = group.Drivers;
      query = query.Where(x => drivers.Contains(x.Id));
    }
    if (term.Length > 0)
    {
      var name = term.ToLowerInvariant();
      var digits = new string([.. term.Where(char.IsAsciiDigit)]);
      query = query.Where(x =>
        x.Name.ToLower().Contains(name)
        || digits.Length >= 3 && x.WhatsAppPhone!.Contains(digits)
      );
    }
    if (request.After is { } after)
      query = query.Where(x =>
        x.Name.CompareTo(after.Name) > 0
        || x.Name == after.Name && x.Id.CompareTo(after.Id) > 0
      );
    var page = await query
      .OrderBy(x => x.Name)
      .ThenBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.Name,
        Phone = x.WhatsAppPhone!,
      })
      .Take(PageSize + 1)
      .ToListAsync(ct);
    var shown = page.Take(PageSize).ToList();
    var phones = shown.Select(x => x.Phone).Distinct().ToArray();
    var open =
      number is null || phones.Length == 0
        ? []
        : await db
          .Conversations.AsNoTracking()
          .Where(x =>
            x.Channel == messaging.Channel
            && x.BusinessNumberId == number
            && phones.Contains(x.Participant)
          )
          .ToDictionaryAsync(x => x.Participant, x => x.Id, ct);
    return RequestResponse<MessagingDriversView>.Ok(
      new(
        [
          .. shown.Select(x => new MessagingDriver(
            x.Id,
            x.Name,
            x.Phone,
            open.TryGetValue(x.Phone, out var id) ? id : null
          )),
        ],
        number is not null,
        page.Count > PageSize
      )
      {
        Next = page.Count > PageSize ? new(shown[^1].Name, shown[^1].Id) : null,
      }
    );
  }

  public async Task<RequestResponse<Guid>> Handle(
    OpenDriverConversationCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return RequestResponse<Guid>.Fail("Access denied.", 403);
    var phone = await db
      .Drivers.AsNoTracking()
      .Where(x => x.Id == request.DriverId && x.IsActive)
      .Select(x => new { x.WhatsAppPhone })
      .SingleOrDefaultAsync(ct);
    if (phone is null)
      return RequestResponse<Guid>.Fail("Driver not found.", 404);
    if (phone.WhatsAppPhone is not { } participant)
      return RequestResponse<Guid>.Fail(
        "This driver has no WhatsApp number. Add it to the driver's "
          + "contacts first.",
        409
      );
    if (await messaging.BusinessNumberAsync(ct) is not { } number)
      return RequestResponse<Guid>.Fail(
        "WhatsApp is not set up for the company yet.",
        409
      );
    if (await ExistingAsync(number, participant, ct) is { } existing)
      return RequestResponse<Guid>.Ok(existing);
    var conversation = new Conversation
    {
      Id = Guid.NewGuid(),
      Channel = messaging.Channel,
      BusinessNumberId = number,
      Participant = participant,
      DriverId = request.DriverId,
      LastMessageAt = clock.GetUtcNow().UtcDateTime,
      Revision = 1,
    };
    db.Conversations.Add(conversation);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      db.Entry(conversation).State = EntityState.Detached;
      return await ExistingAsync(number, participant, ct) is { } first
        ? RequestResponse<Guid>.Ok(first)
        : throw new InvalidOperationException(
          "A conversation insert failed and no conversation holds its key."
        );
    }
    if (company.Id is { } serving)
      events.Publish(serving, new(conversation.Id, conversation.Revision));
    return RequestResponse<Guid>.Ok(conversation.Id);
  }

  private Task<Guid?> ExistingAsync(
    string number,
    string participant,
    CancellationToken ct
  ) =>
    db
      .Conversations.AsNoTracking()
      .Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == number
        && x.Participant == participant
      )
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
}
