namespace Application.Features.Messaging.Options;

// Templates Meta has approved for the carrier's number, as configured for
// this deployment (Messaging:Templates). Only these can be sent outside the
// driver's 24-hour window; none are configured until Meta approves some.
public sealed class MessagingOptions
{
  public List<MessageTemplate> Templates { get; set; } = [];
}

// Text shows the template with {{1}}, {{2}} for its parameters, as the
// driver will read it.
public sealed class MessageTemplate
{
  public string Name { get; set; } = "";
  public string Language { get; set; } = "en_US";
  public int Parameters { get; set; }
  public string Text { get; set; } = "";
}
