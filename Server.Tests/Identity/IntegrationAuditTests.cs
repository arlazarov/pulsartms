using Application.Behaviors;
using Application.Features.Integrations.Commands;
using Application.Features.Integrations.Models;
using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class IntegrationAuditTests
{
  [Theory]
  [InlineData("samsara", true)]
  [InlineData("google-email", false)]
  [InlineData("UNTRUSTED-PROVIDER-SECRET", false)]
  public async Task AuditContainsOnlySafeIdentityRevisionAndOutcome(
    string provider,
    bool success
  )
  {
    var logger = new CaptureLogger();
    var request = new UpdateIntegrationCredentialsCommand(
      provider,
      new()
      {
        Revision = 9,
        Fields = new()
        {
          ["apiKey"] = "DO-NOT-LOG-KEY",
          ["clientSecret"] = "DO-NOT-LOG-SECRET",
          ["refreshToken"] = "DO-NOT-LOG-TOKEN",
          ["DO-NOT-LOG-FIELD"] = "DO-NOT-LOG-VALUE",
        },
      }
    );
    var audit = new AdminAuditBehavior<
      UpdateIntegrationCredentialsCommand,
      RequestResponse<IntegrationConnectionState>
    >(new Actor(), logger);
    await audit.Handle(
      request,
      _ =>
        Task.FromResult(
          success
            ? RequestResponse<IntegrationConnectionState>.Ok(
              new("samsara", true, true, true, 10, null, [])
            )
            : RequestResponse<IntegrationConnectionState>.Fail(
              "Credential fields are invalid."
            )
        ),
      default
    );
    Assert.Single(logger.Messages);
    var text = logger.Messages[0];
    Assert.Contains("test-admin", text);
    Assert.Contains("UpdateIntegrationCredentialsCommand", text);
    Assert.Contains("Revision", text);
    Assert.Contains("9", text);
    Assert.Contains(success ? "completed" : "rejected", text);
    Assert.DoesNotContain("DO-NOT-LOG", text);
    Assert.DoesNotContain("UNTRUSTED-PROVIDER-SECRET", text);
    Assert.DoesNotContain("clientSecret", text);
    Assert.DoesNotContain("refreshToken", text);
  }

  private sealed class Actor : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "test-admin";
  }

  private sealed class CaptureLogger
    : ILogger<
      AdminAuditBehavior<
        UpdateIntegrationCredentialsCommand,
        RequestResponse<IntegrationConnectionState>
      >
    >
  {
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    ) => Messages.Add(formatter(state, exception));
  }
}
