using Application.Features.Messaging.Options;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

// Send holds for tests: Open where no release is required, Required over
// the test's own database.
internal static class TestSendHold
{
  public static SendHold Open() =>
    new(
      new ServiceCollection()
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>(),
      new Revision("test"),
      Options.Create(new SendHoldOptions()),
      TimeProvider.System,
      NullLogger<SendHold>.Instance
    );

  public static SendHold Required(
    IServiceScopeFactory scopes,
    string? revision,
    TimeProvider clock,
    ILogger<SendHold>? logger = null
  ) =>
    new(
      scopes,
      new Revision(revision),
      Options.Create(new SendHoldOptions { RequireRelease = true }),
      clock,
      logger ?? NullLogger<SendHold>.Instance
    );

  public sealed record Revision(string? Name) : IDeploymentRevision;
}
