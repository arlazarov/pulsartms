namespace Application.Interfaces;

public interface IReadCache
{
  Task<T> GetAsync<T>(
    string group,
    string key,
    Func<Task<T>> load,
    TimeSpan? lifetime = null,
    CancellationToken ct = default
  );
  void Invalidate(string group);
  long Generation(string group);
}
