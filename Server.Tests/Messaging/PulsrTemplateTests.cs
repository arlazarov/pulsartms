using System.Text.Json;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Messaging;

// The templates PulsR itself uses. One is available only while it is
// recorded for the company's current number exactly as PulsR defines it;
// PulsR never calls it approved on its own. The contact request goes by
// the ordinary template path with a stable retry key; the fuel card, whose
// image header PulsR cannot send yet, is never sent through it.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class PulsrTemplateTests
{
  private static ApprovedTemplate Recorded(
    PulsrTemplate x,
    string? text = null
  ) =>
    new()
    {
      Name = x.Name,
      Language = x.Language,
      Parameters = x.Examples.Count,
      Text = text ?? x.Body,
    };

  [Fact]
  public async Task TheContactRequestIsAvailableOnlyAsDefined()
  {
    await using var f = await ReplyFixture.CreateAsync();

    Assert.DoesNotContain(
      (await ListAsync(f, [])),
      x => x.Purpose == PulsrTemplates.ContactRequest
    );
    // A straight apostrophe is another text: not the contact request.
    Assert.Null(
      Assert
        .Single(
          await ListAsync(
            f,
            [
              Recorded(
                PulsrTemplates.Contact,
                PulsrTemplates.Contact.Body.Replace('’', '\'')
              ),
            ]
          )
        )
        .Purpose
    );
    await ResetAsync(f);
    Assert.Equal(
      PulsrTemplates.ContactRequest,
      Assert
        .Single(await ListAsync(f, [Recorded(PulsrTemplates.Contact)]))
        .Purpose
    );

    // Recorded for another number, it is not this number's.
    await ResetAsync(f);
    var other = Recorded(PulsrTemplates.Contact);
    other.BusinessNumberId = "999999";
    Assert.Empty(await ListAsync(f, [other]));
  }

  [Fact]
  public async Task AContactRequestIsSentOnceForItsKey()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync(hoursAgo: 30);
    var key = Guid.NewGuid();
    SendConversationTemplateCommand Request() =>
      new(conversation, key, "contact_request", "en_US", []);

    var first = await f.Files([Recorded(PulsrTemplates.Contact)])
      .Handle(Request(), default);
    var again = await f.Files().Handle(Request(), default);

    Assert.Equal(first.Response!.Id, again.Response!.Id);
    Assert.Equal(PulsrTemplates.Contact.Body, first.Response.Body);
    Assert.Single(
      await f
        .Db.ConversationMessages.AsNoTracking()
        .Where(x => x.Direction == MessageDirections.Outbound)
        .ToListAsync()
    );
    // Sending it opens no reply window: only the driver's reply does.
    Assert.True(
      (await f.Db.Conversations.AsNoTracking().SingleAsync()).LastInboundAt
        < f.Clock.GetUtcNow().UtcDateTime.AddHours(-24)
    );
    await f.Worker.RunOnceAsync(default);
    var sent = Assert.Single(f.Messaging.Templates);
    Assert.Equal(("contact_request", 0), (sent.Name, sent.Parameters.Count));
  }

  [Fact]
  public async Task TheFuelCardIsNeverSentWithoutItsImage()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync(hoursAgo: 30);

    var refused = await f.Files([Recorded(PulsrTemplates.FuelCard)])
      .Handle(
        new SendConversationTemplateCommand(
          conversation,
          Guid.NewGuid(),
          "fuel_plan_card",
          "en_US",
          ["Pilot 4521", "I-80 Exit 142", "95 gal"]
        ),
        default
      );

    Assert.Equal(409, refused.StatusCode);
    Assert.Empty(await ListAsync(f, []));
    Assert.Empty(
      await f
        .Db.ConversationMessages.AsNoTracking()
        .Where(x => x.Direction == MessageDirections.Outbound)
        .ToListAsync()
    );
  }

  [Fact]
  public void TheSubmissionIsExactlyWhatMetaIsGiven()
  {
    using var contact = JsonDocument.Parse(PulsrTemplates.Contact.Submission());
    var root = contact.RootElement;
    Assert.Equal(
      ("contact_request", "en_US", "UTILITY"),
      (
        root.GetProperty("name").GetString(),
        root.GetProperty("language").GetString(),
        root.GetProperty("category").GetString()
      )
    );
    var components = root.GetProperty("components");
    Assert.Equal(
      "Dispatch would like to speak with you. Please reply when it’s safe.",
      components[0].GetProperty("text").GetString()
    );
    var button = Assert.Single(
      components[1].GetProperty("buttons").EnumerateArray()
    );
    Assert.Equal(
      ("QUICK_REPLY", "I'm available"),
      (
        button.GetProperty("type").GetString(),
        button.GetProperty("text").GetString()
      )
    );

    using var fuel = JsonDocument.Parse(PulsrTemplates.FuelCard.Submission());
    var header = fuel.RootElement.GetProperty("components")[0];
    Assert.Equal("IMAGE", header.GetProperty("format").GetString());
    Assert.Null(
      ApprovedTemplates.Refusal(
        PulsrTemplates.FuelCard.Name,
        PulsrTemplates.FuelCard.Language,
        PulsrTemplates.FuelCard.Examples.Count,
        PulsrTemplates.FuelCard.Body
      )
    );
  }

  private static async Task<IReadOnlyList<MessageTemplateView>> ListAsync(
    ReplyFixture f,
    ApprovedTemplate[] templates
  ) =>
    (
      await f.Files(templates).Handle(new GetMessageTemplatesQuery(), default)
    ).Response!;

  private static async Task ResetAsync(ReplyFixture f)
  {
    await f.Db.ApprovedTemplates.ExecuteDeleteAsync();
    f.Db.ChangeTracker.Clear();
  }
}
