using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Dispatch.Services;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class BrokerProfileTests
{
  [Fact]
  public async Task PunctuationVariantsReuseImportedCustomerIdentity()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var customer = CustomerMatcher.Create("C.H. Robinson");
    fixture.Db.Customers.Add(customer);
    await fixture.Db.SaveChangesAsync();
    var search = new SearchBrokersHandler(
      fixture.Db,
      new Caller(),
      new Roles()
    );
    var result = await search.Handle(new("CH Robinson"), default);
    Assert.Equal(customer.Id, Assert.Single(result.Response!).Id);
    var save = new SaveBrokerProfileHandler(
      fixture.Db,
      new Caller(),
      new Roles()
    );
    var duplicate = await save.Handle(
      new(new() { Id = Guid.NewGuid(), Name = "CH Robinson" }),
      default
    );
    Assert.Equal(409, duplicate.StatusCode);
    Assert.Equal(1, await fixture.Db.Customers.CountAsync());
  }

  [Fact]
  public async Task ProfileRevisionProtectsDefaultsAndLoadSnapshots()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var save = new SaveBrokerProfileHandler(
      fixture.Db,
      new Caller(),
      new Roles()
    );
    var draft = new BrokerProfile
    {
      Id = Guid.NewGuid(),
      Name = "Broker",
      Terms = new()
      {
        BillingEmail = "billing@example.invalid",
        QuickPayEmail = "quick@example.invalid",
        PaymentDays = 30,
        QuickPayDays = 2,
        QuickPayPercent = 2,
      },
    };
    var created = await save.Handle(new(draft), default);
    Assert.True(created.Success);
    var snapshot = DispatchWorkspaceData.Read<BrokerPaymentTerms>(
      DispatchWorkspaceData.Write(created.Response!.Terms)
    );
    var revision = created.Response.Revision;
    draft.Revision = revision;
    draft.Terms.PaymentDays = 45;
    Assert.True((await save.Handle(new(draft), default)).Success);
    draft.Terms.PaymentDays = 90;
    var stale = await save.Handle(new(draft), default);
    Assert.Equal(409, stale.StatusCode);
    Assert.Equal(30, snapshot.PaymentDays);
    Assert.Equal(
      45,
      BrokerProfiles
        .Read(await fixture.Db.Customers.SingleAsync())
        .Terms.PaymentDays
    );
  }

  [Fact]
  public async Task ReadOnlyCallerCannotWriteBrokerDefaults()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var handler = new SaveBrokerProfileHandler(
      fixture.Db,
      new Caller(),
      new Roles("Driver")
    );
    var result = await handler.Handle(
      new(new() { Id = Guid.NewGuid(), Name = "Broker" }),
      default
    );
    Assert.Equal(403, result.StatusCode);
    Assert.Empty(await fixture.Db.Customers.ToListAsync());
  }

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "operator";
  }

  private sealed class Roles(string role = "Dispatch") : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string value,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
