using System.Text.RegularExpressions;
using Application.Models;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Queries;

// Messages matching words, days and a load, across the whole history the
// company holds - one conversation, or every conversation in the
// dispatcher's chosen driver group - newest first, fifty at a time.
//
// Days are the dispatcher's own, in the time zone the browser names, so
// their edges follow daylight saving. A load is matched two ways, and
// says which: a file from the message filed to that load (the only link
// a message has to a load), or the load number written in the text. A
// driver's current assignment says nothing about an old message and is
// not used.
public sealed record SearchMessagesQuery(
  Guid? ConversationId,
  string? Text,
  DateOnly? From,
  DateOnly? To,
  string? TimeZone,
  int? LoadNumber,
  MessageCursor? After = null,
  bool InChosenGroup = false
) : IRequest<RequestResponse<MessageSearchView>>;

public sealed record MessageSearchHit(
  Guid MessageId,
  Guid ConversationId,
  string? DriverName,
  string Participant,
  string Direction,
  DateTime SentAt,
  string Snippet,
  bool FiledToLoad,
  bool MentionsLoad
)
{
  // The chat's driver's status (not the message's): false for a driver no
  // longer active, whose history search still finds; null for no driver.
  public bool? DriverActive { get; init; }
}

// Next continues below the last message read, which may be below the last
// hit shown: a message whose number only resembles the load is read and
// left out.
public sealed record MessageSearchView(
  IReadOnlyList<MessageSearchHit> Hits,
  bool More
)
{
  public MessageCursor? Next { get; init; }
}

public sealed class MessageSearchHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IDriverScope scope
) : IRequestHandler<SearchMessagesQuery, RequestResponse<MessageSearchView>>
{
  public const int PageSize = 50;
  public const int MaximumText = 100;
  public const int MaximumDays = 366;

  // Days a search may name: a message is never older or newer than these,
  // and every edge between them converts.
  private static readonly DateOnly Earliest = new(2000, 1, 1);
  private static readonly DateOnly Latest = new(2100, 12, 31);

  public async Task<RequestResponse<MessageSearchView>> Handle(
    SearchMessagesQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return Fail("Access denied.", 403);
    var term = request.Text?.Trim() ?? "";
    if (term.Length > MaximumText)
      return Fail($"Search for at most {MaximumText} characters.", 400);
    if (request.LoadNumber is <= 0)
      return Fail("Enter a load number.", 400);
    if (
      term.Length == 0
      && request.From is null
      && request.To is null
      && request.LoadNumber is null
    )
      return Fail("Search for words, a day or a load.", 400);
    TimeZoneInfo? zone = null;
    if (request.From is not null || request.To is not null)
    {
      if (
        string.IsNullOrEmpty(request.TimeZone)
        || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out zone)
      )
        return Fail("The browser's time zone is not known.", 400);
      if (
        request.From is { } early && (early < Earliest || early > Latest)
        || request.To is { } late && (late < Earliest || late > Latest)
      )
        return Fail("Choose days between 2000 and 2100.", 400);
      if (
        request.From is { } first
        && request.To is { } final
        && (final < first || final.DayNumber - first.DayNumber >= MaximumDays)
      )
        return Fail("Choose at most a year, from the earlier day.", 400);
    }
    var messages = db.ConversationMessages.AsNoTracking();
    if (request.ConversationId is { } conversation)
      messages = messages.Where(x => x.ConversationId == conversation);
    else if (
      request.InChosenGroup
      && await scope.CurrentAsync(ct) is { IsAll: false } group
    )
    {
      var drivers = group.Drivers;
      messages = messages.Where(m =>
        db.Conversations.Any(c =>
          c.Id == m.ConversationId
          && c.DriverId != null
          && drivers.Contains(c.DriverId.Value)
        )
      );
    }
    if (term.Length > 0)
    {
      var lower = term.ToLowerInvariant();
      messages = messages.Where(m =>
        m.Body.ToLower().Contains(lower)
        || db.MessageAttachments.Any(a =>
          a.MessageId == m.Id && a.OriginalName.ToLower().Contains(lower)
        )
      );
    }
    if (request.From is { } from)
    {
      var start = Utc(from, zone!);
      messages = messages.Where(m => m.SentAt >= start);
    }
    if (request.To is { } to)
    {
      var end = Utc(to.AddDays(1), zone!);
      messages = messages.Where(m => m.SentAt < end);
    }
    if (request.LoadNumber is { } load)
    {
      var digits = load.ToString();
      messages = messages.Where(m =>
        m.Body.Contains(digits)
        || db.MessageAttachments.Any(a =>
          a.MessageId == m.Id
          && db.DispatchDocuments.Any(d =>
            d.SourceAttachmentId == a.Id
            && db.Dispatches.Any(l =>
              l.Id == d.DispatchId && l.LoadNumber == load
            )
          )
        )
      );
    }
    if (request.After is { } after)
      messages = messages.Where(x =>
        x.SentAt < after.SentAt
        || x.SentAt == after.SentAt
          && (
            x.CreatedAt < after.CreatedAt
            || x.CreatedAt == after.CreatedAt && x.Id.CompareTo(after.Id) < 0
          )
      );
    var read = await messages
      .OrderByDescending(x => x.SentAt)
      .ThenByDescending(x => x.CreatedAt)
      .ThenByDescending(x => x.Id)
      .Take(PageSize + 1)
      .Select(x => new
      {
        x.Id,
        x.ConversationId,
        x.Direction,
        x.Body,
        x.SentAt,
        x.CreatedAt,
      })
      .ToListAsync(ct);
    var page = read.Take(PageSize).ToList();
    var ids = page.Select(x => x.Id).ToArray();
    var filed =
      request.LoadNumber is { } number && ids.Length > 0
        ? (
          await db
            .MessageAttachments.AsNoTracking()
            .Where(a =>
              ids.Contains(a.MessageId)
              && db.DispatchDocuments.Any(d =>
                d.SourceAttachmentId == a.Id
                && db.Dispatches.Any(l =>
                  l.Id == d.DispatchId && l.LoadNumber == number
                )
              )
            )
            .Select(a => a.MessageId)
            .ToListAsync(ct)
        ).ToHashSet()
        : [];
    var names = (
      await db
        .MessageAttachments.AsNoTracking()
        .Where(a => ids.Contains(a.MessageId))
        .Select(a => new { a.MessageId, a.OriginalName })
        .ToListAsync(ct)
    ).ToLookup(x => x.MessageId, x => x.OriginalName);
    var conversations = page.Select(x => x.ConversationId).Distinct().ToArray();
    var who = await db
      .Conversations.AsNoTracking()
      .Where(c => conversations.Contains(c.Id))
      .Select(c => new
      {
        c.Id,
        c.Participant,
        Driver = db
          .Drivers.Where(d => d.Id == c.DriverId)
          .Select(d => d.Name)
          .FirstOrDefault(),
        Active = db
          .Drivers.Where(d => d.Id == c.DriverId)
          .Select(d => (bool?)d.IsActive)
          .FirstOrDefault(),
      })
      .ToDictionaryAsync(x => x.Id, ct);
    var hits = new List<MessageSearchHit>();
    foreach (var message in page)
    {
      var mentions =
        request.LoadNumber is { } mentioned
        && Mentions(message.Body, mentioned);
      var isFiled = filed.Contains(message.Id);
      // A number that only contains the load's digits (14070 for 1407)
      // is no mention: read, and left out.
      if (request.LoadNumber is not null && !mentions && !isFiled)
        continue;
      var chat = who.GetValueOrDefault(message.ConversationId);
      hits.Add(
        new(
          message.Id,
          message.ConversationId,
          chat?.Driver,
          chat?.Participant ?? "",
          message.Direction,
          message.SentAt,
          Snippet(message.Body, names[message.Id], term),
          isFiled,
          mentions
        )
        {
          DriverActive = chat?.Active,
        }
      );
    }
    var last = page.LastOrDefault();
    return RequestResponse<MessageSearchView>.Ok(
      new(hits, read.Count > PageSize)
      {
        Next =
          read.Count > PageSize && last is not null
            ? new(last.SentAt, last.CreatedAt, last.Id)
            : null,
      }
    );
  }

  // The instant a day starts where the dispatcher is. Where the clocks
  // jump forward at midnight that midnight never happens, and the day
  // starts at its first moment that does.
  internal static DateTime Utc(DateOnly day, TimeZoneInfo zone)
  {
    var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
    for (var minutes = 0; zone.IsInvalidTime(start) && minutes < 180; minutes++)
      start = start.AddMinutes(1);
    return TimeZoneInfo.ConvertTimeToUtc(start, zone);
  }

  internal static bool Mentions(string body, int load) =>
    Regex.IsMatch(body, $@"(?<!\d){load}(?!\d)");

  // Around the first match, at most 160 characters; a file's name when the
  // text is empty.
  private static string Snippet(
    string body,
    IEnumerable<string> files,
    string term
  )
  {
    var text = body.Length > 0 ? body : string.Join(", ", files);
    if (text.Length <= 160)
      return text;
    var at =
      term.Length > 0
        ? text.IndexOf(term, StringComparison.OrdinalIgnoreCase)
        : 0;
    var start = Math.Clamp(at - 60, 0, text.Length - 160);
    return (start > 0 ? "…" : "")
      + text.Substring(start, 160).Trim()
      + (start + 160 < text.Length ? "…" : "");
  }

  private static RequestResponse<MessageSearchView> Fail(
    string message,
    int status
  ) => RequestResponse<MessageSearchView>.Fail(message, status);
}
