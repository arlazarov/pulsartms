using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Rules.Fleet;
using Domain.Rules.Messaging;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// Whether this shift's fuel can go to the driver over WhatsApp, and what
// became of the attempts for a plan version. Read only when Send plan is
// opened or used - never per card.
public sealed class FuelIssueChannel(
  IAppDbContext db,
  TruckPlanningInputsReader inputs,
  IDriverTextDelivery delivery,
  TimeProvider time
)
{
  public string Name => delivery.Channel;

  // The driver whose hours decide the shift is the one the plan goes to:
  // the same reader, so the two cannot disagree. A co-driver gets nothing.
  public async Task<FuelIssueRecipient> RecipientAsync(
    Guid truckId,
    CancellationToken ct
  )
  {
    var work = await inputs.ReadAsync(truckId, ct, includeHos: false);
    var driver = work?.DriverId is { } id
      ? await db
        .Drivers.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new
        {
          x.Id,
          x.Name,
          x.WhatsAppPhone,
        })
        .SingleOrDefaultAsync(ct)
      : null;
    var phone =
      driver?.WhatsAppPhone is { } number
      && ContactAddresses.Phone(number) == number
        ? number
        : null;
    // The reply window is Messaging's: the same one a conversation reply
    // is held to, under the company's current business number.
    var readiness = await delivery.ReadinessAsync(phone, ct);
    var configured = readiness.Configured;
    if (driver is null)
      return new(
        null,
        null,
        null,
        configured
          ? FuelIssueChannelStates.NoDriver
          : FuelIssueChannelStates.NotConfigured,
        null
      );
    var windowEnds = readiness.WindowEnds;
    var state =
      !configured ? FuelIssueChannelStates.NotConfigured
      : phone is null ? FuelIssueChannelStates.NoNumber
      : windowEnds is null ? FuelIssueChannelStates.OutsideWindow
      : FuelIssueChannelStates.Ready;
    return new(driver.Id, driver.Name, phone, state, windowEnds);
  }

  public async Task<FuelIssueMessageState?> LatestAsync(
    TruckFuelPlanSnapshot saved,
    CancellationToken ct
  )
  {
    var latest = await db
      .DriverMessages.AsNoTracking()
      .Where(x =>
        x.TruckId == saved.TruckId
        && x.PlanCalculatedAt == saved.CalculatedAt
        && x.AssignmentRevision == saved.AssignmentRevision
      )
      .OrderByDescending(x => x.CreatedAt)
      .ThenByDescending(x => x.Attempt)
      .FirstOrDefaultAsync(ct);
    return latest is null ? null : State(latest);
  }

  public FuelIssueMessageState State(DriverMessage message) =>
    new(
      DriverMessageProgress.Uncertain(message.Status, message.StatusAt, Now)
        ? DriverMessageStatuses.Unknown
        : message.Status,
      message.StatusAt,
      message.ErrorCode,
      DriverMessageProgress.Uncertain(message.Status, message.StatusAt, Now)
    );

  // The instruction, not its wording: the accepted assignment, each visit
  // with what the driver is told to do there, and the recipient. Words
  // that move with the truck ("about 40 miles") and a recalculation that
  // says the same thing do not make a second message.
  public static string Key(
    TruckFuelPlanSnapshot saved,
    IEnumerable<FuelPlanStop> visits,
    string recipient
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          string.Join(
            '\n',
            saved.TruckId.ToString("N"),
            saved.RootExecutionLegId?.ToString("N") ?? "",
            saved.AssignmentRevision.ToString(CultureInfo.InvariantCulture),
            string.Join(
              ',',
              visits
                .Select(x =>
                  FuelVisitIdentity.Key(x) + "=" + FuelVisitIdentity.Content(x)
                )
                .Order(StringComparer.Ordinal)
            ),
            recipient
          )
        )
      )
    );

  private DateTime Now => time.GetUtcNow().UtcDateTime;
}
