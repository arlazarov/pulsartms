using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchBrokerTests
{
  [Fact]
  public async Task SearchIsExplicitAndSelectionCopiesTermsWithoutWriting()
  {
    var calls = new List<HttpMethod>();
    var profile = new BrokerProfile
    {
      Id = Guid.NewGuid(),
      Revision = 4,
      Name = "C.H. Robinson",
      Terms = new() { PaymentDays = 30, BillingEmail = "ap@example.invalid" },
    };
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        calls.Add(request.Method);
        return Task.FromResult(
          MileageComponentResponses.Ok(new List<BrokerProfile> { profile })
        );
      }
    );
    var metadata = new DispatchWorkspaceMetadata();
    var changes = 0;
    var component = context.Render<DispatchBroker>(p =>
      p.Add(x => x.Metadata, metadata)
        .Add(x => x.CanEdit, true)
        .Add(x => x.Changed, () => changes++)
    );
    Assert.Empty(calls);
    Assert.Empty(component.FindAll("details"));
    await component
      .Find("input[placeholder='Broker name']")
      .ChangeAsync("CH Robinson");
    Assert.Empty(calls);
    await component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Search brokers")
      .ClickAsync(new());
    await component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Use C.H. Robinson")
      .ClickAsync(new());
    Assert.Equal([HttpMethod.Get], calls);
    Assert.Equal(profile.Id, metadata.BrokerId);
    Assert.Equal(30, metadata.PaymentTerms.PaymentDays);
    metadata.PaymentTerms.PaymentDays = 90;
    Assert.Equal(30, profile.Terms.PaymentDays);
    Assert.Equal(1, changes);
  }

  [Fact]
  public async Task SharedDefaultsSaveIsExplicitAndIncludesOpenedRevision()
  {
    BrokerProfile? saved = null;
    var profile = new BrokerProfile
    {
      Id = Guid.NewGuid(),
      Revision = 3,
      Name = "Broker",
    };
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(
            new List<BrokerProfile> { profile }
          );
        saved = await request.Content!.ReadFromJsonAsync<BrokerProfile>(ct);
        return MileageComponentResponses.Ok(saved!);
      }
    );
    var metadata = new DispatchWorkspaceMetadata();
    var component = context.Render<DispatchBroker>(p =>
      p.Add(x => x.Metadata, metadata).Add(x => x.CanEdit, true)
    );
    await component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Search brokers")
      .ClickAsync(new());
    await component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Use Broker")
      .ClickAsync(new());
    Assert.Null(saved);
    metadata.PaymentTerms.PaymentDays = 45;
    await component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Save as broker defaults")
      .ClickAsync(new());
    Assert.Equal(profile.Id, saved!.Id);
    Assert.Equal(3, saved.Revision);
    Assert.Equal(45, saved.Terms.PaymentDays);
  }
}
