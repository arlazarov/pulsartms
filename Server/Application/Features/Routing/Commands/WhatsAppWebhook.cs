using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Routing.Commands;

// Meta's subscription check: the challenge is echoed only for this
// carrier's verify token.
public sealed record VerifyWhatsAppWebhookQuery(
  string CompanyKey,
  string? Mode,
  string? VerifyToken,
  string? Challenge
) : IRequest<RequestResponse<string>>;

// A signed notification for one carrier. It is read only after its
// signature matches that carrier's app secret, and only changes for its
// own business number count.
public sealed record ReceiveWhatsAppWebhookCommand(
  string CompanyKey,
  string? Signature,
  Stream Body
) : IRequest<RequestResponse<int>>;

public sealed class WhatsAppWebhookHandlers(
  IAppDbContext db,
  IDriverMessaging messaging,
  ICurrentCompany companies,
  PlanningSummaryCache summaries
)
  : IRequestHandler<VerifyWhatsAppWebhookQuery, RequestResponse<string>>,
    IRequestHandler<ReceiveWhatsAppWebhookCommand, RequestResponse<int>>
{
  public const int MaximumBody = 262_144;

  public async Task<RequestResponse<string>> Handle(
    VerifyWhatsAppWebhookQuery request,
    CancellationToken ct
  )
  {
    if (
      request.Mode != "subscribe"
      || request.Challenge is not { Length: > 0 and <= 128 } challenge
      || !challenge.All(char.IsAsciiLetterOrDigit)
      || await CompanyAsync(request.CompanyKey, ct) is not { } company
    )
      return RequestResponse<string>.Fail("Not accepted.", 403);
    using var serving = companies.As(company);
    return await messaging.AcceptsSubscriptionAsync(request.VerifyToken, ct)
      ? RequestResponse<string>.Ok(challenge)
      : RequestResponse<string>.Fail("Not accepted.", 403);
  }

  public async Task<RequestResponse<int>> Handle(
    ReceiveWhatsAppWebhookCommand request,
    CancellationToken ct
  )
  {
    if (await CompanyAsync(request.CompanyKey, ct) is not { } company)
      return RequestResponse<int>.Fail("Not accepted.", 404);
    var body = await ReadAsync(request.Body, ct);
    if (body is null)
      return RequestResponse<int>.Fail("Not accepted.", 413);
    using var serving = companies.As(company);
    var notification = await messaging.ReadNotificationAsync(
      body,
      request.Signature,
      ct
    );
    if (notification is null)
      return RequestResponse<int>.Fail("Not accepted.", 401);
    var trucks = await ApplyStatusesAsync(notification.Statuses, ct);
    await ApplyInboundAsync(notification.Inbound, ct);
    await db.SaveChangesAsync(ct);
    // After the commit: only the trucks whose hand-over changed.
    foreach (var truck in trucks)
      summaries.Committed(company, truck);
    return RequestResponse<int>.Ok(trucks.Count);
  }

  // Each status belongs to the message with that provider id and to no
  // other: a notification about an old attempt never touches a newer one.
  // Repeats and late arrivals do not move a status back.
  private async Task<HashSet<Guid>> ApplyStatusesAsync(
    IReadOnlyList<DriverMessageStatusEvent> statuses,
    CancellationToken ct
  )
  {
    var trucks = new HashSet<Guid>();
    if (statuses.Count == 0)
      return trucks;
    var ids = statuses.Select(x => x.ProviderMessageId).Distinct().ToArray();
    var messages = await db
      .DriverMessages.Where(x =>
        x.ProviderMessageId != null && ids.Contains(x.ProviderMessageId)
      )
      .ToDictionaryAsync(x => x.ProviderMessageId!, ct);
    foreach (var status in statuses.OrderBy(x => x.At))
      if (
        messages.GetValueOrDefault(status.ProviderMessageId) is { } message
        && DriverMessageProgress.Advances(message.Status, status.Status)
      )
      {
        message.Status = status.Status;
        message.StatusAt = status.At;
        message.ErrorCode = status.ErrorCode;
        trucks.Add(message.TruckId);
      }
    return trucks;
  }

  private async Task ApplyInboundAsync(
    IReadOnlyList<DriverMessageInboundEvent> inbound,
    CancellationToken ct
  )
  {
    if (inbound.Count == 0)
      return;
    var latest = inbound
      .GroupBy(x => x.Phone)
      .ToDictionary(x => x.Key, x => x.Max(y => y.At));
    var phones = latest.Keys.ToArray();
    var windows = await db
      .DriverMessagingWindows.Where(x =>
        x.Channel == messaging.Channel && phones.Contains(x.Phone)
      )
      .ToDictionaryAsync(x => x.Phone, ct);
    foreach (var (phone, at) in latest)
    {
      if (windows.GetValueOrDefault(phone) is { } window)
      {
        if (at > window.LastInboundAt)
          window.LastInboundAt = at;
        continue;
      }
      db.DriverMessagingWindows.Add(
        new DriverMessagingWindow
        {
          Id = Guid.NewGuid(),
          Channel = messaging.Channel,
          Phone = phone,
          LastInboundAt = at,
        }
      );
    }
  }

  private async Task<Guid?> CompanyAsync(string key, CancellationToken ct) =>
    string.IsNullOrWhiteSpace(key) || key.Length > 100
      ? null
      : await db
        .Companies.AsNoTracking()
        .Where(x => x.Key == key && x.IsActive)
        .Select(x => (Guid?)x.Id)
        .SingleOrDefaultAsync(ct);

  private static async Task<byte[]?> ReadAsync(
    Stream body,
    CancellationToken ct
  )
  {
    using var copy = new MemoryStream();
    var buffer = new byte[16_384];
    int read;
    while ((read = await body.ReadAsync(buffer, ct)) > 0)
    {
      if (copy.Length + read > MaximumBody)
        return null;
      copy.Write(buffer, 0, read);
    }
    return copy.ToArray();
  }
}
