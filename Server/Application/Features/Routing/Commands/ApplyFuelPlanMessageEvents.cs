using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Routing.Commands;

// The fuel plans' part of a provider notification that Messaging has
// already verified for this company: delivery statuses of sent fuel plans
// and each driver's latest message time, which opens the provider's reply
// window for the next plan. Applied in its own transaction after
// Messaging's; both are forward-only, so a provider retry of the whole
// notification settles both.
public sealed record ApplyFuelPlanMessageEventsCommand(
  Guid Company,
  DriverMessagingNotification Notification
) : IRequest<RequestResponse<int>>;

public sealed class ApplyFuelPlanMessageEventsHandler(
  IAppDbContext db,
  IFuelPlanTransport transport,
  ICurrentCompany companies,
  PlanningSummaryCache summaries
) : IRequestHandler<ApplyFuelPlanMessageEventsCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    ApplyFuelPlanMessageEventsCommand request,
    CancellationToken ct
  )
  {
    using var serving = companies.As(request.Company);
    var trucks = await ApplyStatusesAsync(request.Notification.Statuses, ct);
    await ApplyInboundAsync(request.Notification.Inbound, ct);
    await db.SaveChangesAsync(ct);
    // After the commit: only the trucks whose hand-over changed.
    foreach (var truck in trucks)
      summaries.Committed(request.Company, truck);
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
        x.Channel == transport.Channel && phones.Contains(x.Phone)
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
          Channel = transport.Channel,
          Phone = phone,
          LastInboundAt = at,
        }
      );
    }
  }
}
