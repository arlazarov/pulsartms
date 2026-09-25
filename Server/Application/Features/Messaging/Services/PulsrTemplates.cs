using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Services;

// The templates PulsR itself uses, exactly as they are submitted to Meta.
// PulsR never calls one approved: a purpose is available only while an
// administrator has recorded, for the number the company sends from now,
// a template with this name, language, text and number of parameters -
// which they do after Meta approves it. Anything that differs is just
// another recorded template.
public static class PulsrTemplates
{
  public const string ContactRequest = "contactRequest";
  public const string FuelPlanCard = "fuelPlanCard";

  // Named parameters: Meta shows them to the reviewer and the driver reads
  // the company's own name, which the sending company supplies.
  public static readonly PulsrTemplate Contact = new(
    ContactRequest,
    "contact_request",
    "en_US",
    "UTILITY",
    null,
    "Dispatch at {{company_name}} would like to speak with you. Please "
      + "reply when it’s safe.",
    ["AMF Carrier"],
    ["I'm available"],
    null
  )
  {
    CompanyNamed = true,
  };

  // The body cannot start or end with a parameter, as Meta requires.
  public static readonly PulsrTemplate FuelCard = new(
    FuelPlanCard,
    "fuel_plan_card",
    "en_US",
    "UTILITY",
    "IMAGE",
    "Fuel stop: {{1}}, {{2}}. Planned fill: {{3}}.",
    ["Pilot 4521", "I-80 Exit 142, Walcott, IA", "95 gal"],
    [],
    "PulsR does not send a template with an image header yet: fuel plans "
      + "still go as text while the driver's reply window is open."
  );

  public static readonly IReadOnlyList<PulsrTemplate> All = [Contact, FuelCard];

  public static PulsrTemplate? Matching(ApprovedTemplate recorded) =>
    All.FirstOrDefault(x =>
      x.Name == recorded.Name
      && x.Language == recorded.Language
      && x.Body == recorded.Text
      && x.Examples.Count == recorded.Parameters
    );
}

// Unsupported says why PulsR cannot send it yet, when it cannot.
public sealed record PulsrTemplate(
  string Purpose,
  string Name,
  string Language,
  string Category,
  string? HeaderFormat,
  string Body,
  IReadOnlyList<string> Examples,
  IReadOnlyList<string> QuickReplies,
  string? Unsupported
)
{
  // Its one parameter is the sending company's name, filled by the server.
  public bool CompanyNamed { get; init; }

  // What to submit to Meta (WhatsApp Manager, or the Graph API's
  // message_templates), with the examples Meta asks for. An image header
  // needs a sample uploaded to Meta, whose handle goes in place of the
  // placeholder.
  public string Submission()
  {
    var components = new JsonArray();
    if (HeaderFormat is { } format)
      components.Add(
        new JsonObject
        {
          ["type"] = "HEADER",
          ["format"] = format,
          ["example"] = new JsonObject
          {
            ["header_handle"] = new JsonArray("<uploaded sample handle>"),
          },
        }
      );
    var body = new JsonObject { ["type"] = "BODY", ["text"] = Body };
    var names = ApprovedTemplates.Names(Body);
    if (names is not null)
      body["example"] = new JsonObject
      {
        ["body_text_named_params"] = new JsonArray(
          [
            .. names.Select(
              (name, index) =>
                (JsonNode)
                  new JsonObject
                  {
                    ["param_name"] = name,
                    ["example"] = Examples[index],
                  }
            ),
          ]
        ),
      };
    else if (Examples.Count > 0)
      body["example"] = new JsonObject
      {
        ["body_text"] = new JsonArray(
          new JsonArray([.. Examples.Select(x => (JsonNode)x)])
        ),
      };
    components.Add(body);
    if (QuickReplies.Count > 0)
      components.Add(
        new JsonObject
        {
          ["type"] = "BUTTONS",
          ["buttons"] = new JsonArray(
            [
              .. QuickReplies.Select(x =>
                (JsonNode)
                  new JsonObject { ["type"] = "QUICK_REPLY", ["text"] = x }
              ),
            ]
          ),
        }
      );
    var submission = new JsonObject
    {
      ["name"] = Name,
      ["language"] = Language,
      ["category"] = Category,
    };
    if (names is not null)
      submission["parameter_format"] = "NAMED";
    submission["components"] = components;
    return submission.ToJsonString(
      new JsonSerializerOptions
      {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      }
    );
  }
}
