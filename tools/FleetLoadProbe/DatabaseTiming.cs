using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FleetLoadProbe;

internal sealed class DatabaseTiming
  : IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>,
    IDisposable
{
  private readonly AsyncLocal<Measurement?> current = new();
  private readonly ConcurrentBag<IDisposable> subscriptions = [];
  private readonly ConcurrentQueue<object> completed = new();
  private readonly IDisposable listener;
  private readonly ConcurrentDictionary<Guid, string> callers = new();

  // An async method's frame is its state machine, <Name>d__N.MoveNext:
  // name the method, not the machine.
  private static string Caller(System.Reflection.MethodBase method)
  {
    var type = method.DeclaringType!;
    var owner =
      type.DeclaringType is { } outer && type.Name.StartsWith('<')
        ? outer
        : type;
    var name = type.Name.StartsWith('<')
      ? type.Name[1..type.Name.IndexOf('>')]
      : method.Name;
    return owner.Name + "." + name;
  }

  public DatabaseTiming() =>
    listener = DiagnosticListener.AllListeners.Subscribe(this);

  public Measurement Begin(string label)
  {
    var measurement = new Measurement(label, this);
    current.Value = measurement;
    return measurement;
  }

  public object[] Snapshot() => completed.ToArray();

  public void OnNext(DiagnosticListener value)
  {
    if (value.Name == "Microsoft.EntityFrameworkCore")
      subscriptions.Add(value.Subscribe(this));
  }

  public void OnNext(KeyValuePair<string, object?> value)
  {
    if (current.Value is not { } scope)
      return;
    if (
      value.Value is CommandEventData starting
      && value.Key.EndsWith("CommandExecuting", StringComparison.Ordinal)
    )
    {
      // Before the command runs, the stack still holds the synchronous path
      // from the application method that asked into EF; afterwards it is an
      // I/O continuation with no application frame.
      callers[starting.CommandId] = string.Join(
        " < ",
        new StackTrace()
          .GetFrames()
          .Select(x => x.GetMethod())
          .Where(x =>
            x?.DeclaringType?.FullName is { } name
            && (
              name.StartsWith("Application.", StringComparison.Ordinal)
              || name.StartsWith("Infrastructure.", StringComparison.Ordinal)
            )
          )
          .Select(x => Caller(x!))
          .Distinct()
          .Take(4)
      );
      return;
    }
    if (value.Value is CommandExecutedEventData command)
    {
      var sql = command.Command.CommandText;
      var fingerprint = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(sql))
      )[..12];
      var tables = string.Join(
        '+',
        Regex
          .Matches(sql, "(?:FROM|JOIN|UPDATE|INTO)\\s+\"([A-Za-z]+)\"")
          .Select(x => x.Groups[1].Value)
          .Distinct()
          .Order()
      );
      callers.TryRemove(command.CommandId, out var caller);
      scope.Add("command", fingerprint, tables, command.Duration, sql, caller);
    }
    else if (
      value.Value is ConnectionEndEventData connection
      && value.Key.EndsWith("ConnectionOpened", StringComparison.Ordinal)
    )
      scope.Add("connection-open", "", "", connection.Duration);
    else if (value.Value is TransactionEndEventData transaction)
      scope.Add("transaction", "", "", transaction.Duration);
  }

  public void OnCompleted() { }

  public void OnError(Exception error) { }

  public void Dispose()
  {
    listener.Dispose();
    foreach (var subscription in subscriptions)
      subscription.Dispose();
  }

  public sealed class Measurement(string label, DatabaseTiming owner)
    : IDisposable
  {
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly object gate = new();
    private readonly Dictionary<string, Timing> timings = [];

    public void Add(
      string kind,
      string fingerprint,
      string tables,
      TimeSpan duration,
      string? sql = null,
      string? caller = null
    )
    {
      lock (gate)
      {
        var key = kind + ":" + fingerprint;
        timings.TryGetValue(key, out var old);
        timings[key] = new(
          kind,
          fingerprint,
          tables,
          (old?.Count ?? 0) + 1,
          (old?.TotalMs ?? 0) + duration.TotalMilliseconds,
          Math.Max(old?.MaxMs ?? 0, duration.TotalMilliseconds),
          old?.Sql ?? sql,
          old is null ? [caller ?? ""] : [.. old.Callers, caller ?? ""]
        );
      }
    }

    public void Dispose()
    {
      lock (gate)
        owner.completed.Enqueue(
          new
          {
            label,
            wallMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            timings = timings
              .Values.OrderByDescending(x => x.TotalMs)
              .ToArray(),
          }
        );
      while (owner.completed.Count > 32)
        owner.completed.TryDequeue(out _);
      owner.current.Value = null;
    }
  }

  public sealed record Timing(
    string Kind,
    string Fingerprint,
    string Tables,
    int Count,
    double TotalMs,
    double MaxMs,
    // The statement text, without parameter values: which read a repeated
    // fingerprint is. Only the probe's own measured requests carry it.
    string? Sql,
    // Each execution's calling application frames, innermost first.
    string[] Callers
  );
}
