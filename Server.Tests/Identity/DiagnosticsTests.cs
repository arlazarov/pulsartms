using Application.Behaviors;
using Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class DiagnosticsTests
{
  private sealed record FailingDiagnosticRequest;

  [Fact]
  public async Task ReturnedFailuresCountAsFailuresWithoutExceptions()
  {
    var behavior = new RequestDiagnosticsBehavior<FailingDiagnosticRequest, RequestResponse<int>>(
      NullLogger<RequestDiagnosticsBehavior<FailingDiagnosticRequest, RequestResponse<int>>>.Instance);
    await behavior.Handle(new(), _ => Task.FromResult(RequestResponse<int>.Fail("invalid")), default);
    var value = RequestMetrics.Snapshot()[nameof(FailingDiagnosticRequest)];
    Assert.True(value.Failed >= 1);
    Assert.True(value.TotalMs >= 0);
  }

  [Fact]
  public async Task AdminAuditRecordsActorTargetAndRoleWithoutPassword()
  {
    var id = Guid.NewGuid();
    var logger = new CaptureLogger();
    var command = new Application.Features.Users.Commands.UpdateUserCommand(id, null, null, "NEVER-LOG-THIS", true, "Dispatch");
    await new AdminAuditBehavior<Application.Features.Users.Commands.UpdateUserCommand, RequestResponse<Guid>>(new Actor(), logger)
      .Handle(command, _ => Task.FromResult(RequestResponse<Guid>.Ok(id)), default);
    Assert.Contains("actor", logger.Text);
    Assert.Contains(id.ToString(), logger.Text);
    Assert.Contains("Dispatch", logger.Text);
    Assert.DoesNotContain("NEVER-LOG-THIS", logger.Text);
    Assert.DoesNotContain("Password", logger.Text);
  }

  private sealed class Actor : Application.Interfaces.ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "actor";
  }

  private sealed class CaptureLogger : ILogger<AdminAuditBehavior<Application.Features.Users.Commands.UpdateUserCommand, RequestResponse<Guid>>>
  {
    public string Text = "";
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
      => Text = formatter(state, exception);
  }
}
