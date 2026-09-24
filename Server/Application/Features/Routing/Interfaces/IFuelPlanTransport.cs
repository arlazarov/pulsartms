using Domain.Models.Messaging;

namespace Application.Features.Routing.Interfaces;

// What sending a fuel plan needs from the carrier's messaging provider, for
// the serving company: its channel name, whether it is configured, and one
// text to one driver. The provider adapter behind driver conversations
// implements this as well; fuel planning knows nothing else of messaging.
// A send is never retried here: an answer that did not come is unknown.
public interface IFuelPlanTransport
{
  string Channel { get; }

  Task<bool> IsConfiguredAsync(CancellationToken ct);

  Task<DriverMessageSendResult> SendTextAsync(
    string recipient,
    string text,
    CancellationToken ct
  );
}
