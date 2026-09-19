using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Synchronization;

public sealed class ApplicationWorker<TOperation>(
  TOperation operation,
  IConfiguration configuration
) : BackgroundService
  where TOperation : class, IBackgroundOperation
{
  protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
    Runs(configuration, typeof(TOperation))
      ? operation.RunAsync(stoppingToken)
      : Task.CompletedTask;

  // Enabled keeps its meaning: false stops every operation in this instance.
  // Roles narrows what one instance runs, so request serving and background
  // work can be separated without a switch per operation. Absent or empty
  // means all of them, which is what every existing deployment states.
  internal static bool Runs(IConfiguration configuration, Type operation)
  {
    if (!configuration.GetValue("BackgroundOperations:Enabled", true))
      return false;
    var roles = configuration
      .GetSection("BackgroundOperations:Roles")
      .Get<string[]>();
    if (roles is null || roles.Length == 0)
      return true;
    var name = Role(operation);
    return roles.Any(role =>
      string.Equals(role.Trim(), name, StringComparison.OrdinalIgnoreCase)
    );
  }

  // IDriverHosRefreshOperation names the role DriverHosRefresh, so a role
  // reads as the work it runs rather than as an interface name.
  internal static string Role(Type operation)
  {
    var name = operation.Name;
    if (operation.IsInterface && name.Length > 1 && name[0] == 'I')
      name = name[1..];
    return name.EndsWith("Operation", StringComparison.Ordinal)
      ? name[..^"Operation".Length]
      : name;
  }
}
