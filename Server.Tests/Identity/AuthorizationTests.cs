using System.Security.Claims;
using Infrastructure.Identity;
using Infrastructure.Integrations.Google.Gmail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class AuthorizationTests
{
  [Theory]
  [InlineData("admin-id", true)]
  [InlineData("other-id", false)]
  [InlineData("", false)]
  public async Task OnlyAdminRoleCanAdminister(string id, bool allowed)
  {
    var requirement = new AdminRequirement();
    var user = new ClaimsPrincipal(new ClaimsIdentity(
      [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Email, "anton@amfcarrier.com")], "Bearer"));
    var context = new AuthorizationHandlerContext([requirement], user, null);
    await new AdminAuthorizationHandler(new TestRoles()).HandleAsync(context);
    Assert.Equal(allowed, context.HasSucceeded);
  }

  [Fact]
  public async Task UnconfiguredPushFailsClosed()
  {
    var validator = new GmailPushValidator(new ConfigurationBuilder().Build());
    Assert.False(validator.IsConfigured);
    Assert.False(await validator.ValidateAsync("Bearer anything"));
    Assert.False(validator.IsExpectedMailbox("someone@example.com"));
  }

  private sealed class TestRoles : Application.Interfaces.IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<string?>(id == "admin-id" ? "Admin" : "Dispatch");
    public Task<Dictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetAsync(string id, string role, CancellationToken ct = default) => throw new NotImplementedException();
  }

  [Fact]
  public async Task PushRequiresTokenAndExpectedMailbox()
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Gmail:PushAudience"] = "https://example.com/push", ["Gmail:PushServiceAccountEmail"] = "push@example.com",
      ["Gmail:MailboxEmail"] = "fuel@example.com" }).Build();
    var validator = new GmailPushValidator(config);
    Assert.True(validator.IsConfigured);
    Assert.False(await validator.ValidateAsync(""));
    Assert.False(validator.IsExpectedMailbox("someone@example.com"));
    Assert.True(validator.IsExpectedMailbox("fuel@example.com"));
  }
}
