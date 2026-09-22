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
      scope.Add("command", fingerprint, tables, command.Duration);
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
      TimeSpan duration
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
          Math.Max(old?.MaxMs ?? 0, duration.TotalMilliseconds)
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
    double MaxMs
  );
}
