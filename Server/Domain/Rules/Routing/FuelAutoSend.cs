using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Whether a prepared fuel plan may go to the driver without a dispatcher
// pressing Send plan.
//
// Sending is manual unless a company turns automatic sending on. Even then
// only this shift's stops of a driver on duty go, over a configured
// channel, and never the same content twice: a new price list or another
// poll that leaves the plan as it was sends nothing. Asked again right
// before anything goes out, so a setting turned off, a driver gone off duty
// or a plan changed while the send waited stops it there. A dispatcher can
// always send by hand, whatever this says.
public static class FuelAutoSend
{
  public static string? Refusal(bool enabled, bool channelReady, FuelPlan? plan)
  {
    if (!enabled)
      return "Automatic fuel plan sending is off.";
    if (!channelReady)
      return "No sending channel is configured.";
    if (plan is null)
      return "There is no fuel plan to send.";
    if (plan.IssueState == FuelIssueStates.HosUnknown)
      return "The driver's hours are unknown.";
    if (plan.IssueState != FuelIssueStates.Ready)
      return "The driver is not on duty.";
    // A stop the tank cannot reach is a dispatcher's call, not a message.
    if (plan.IssueCritical)
      return "A planned stop is out of reach.";
    var current = plan
      .Stops.Where(x => x.IssueHorizon == FuelIssueHorizons.Current)
      .ToList();
    if (current.Count == 0)
      return "No fuel stop is planned for this shift.";
    if (current.All(x => x.Sent is { Changed: false }))
      return "This plan was already sent.";
    return null;
  }
}
