using Application.Interfaces;
using Domain.Entities;

namespace Server.Tests.Support;

internal sealed class TestCompany : ICurrentCompany
{
  private readonly AsyncLocal<Guid?> chosen = new();

  public Guid? Id => chosen.Value ?? Company.Amf;

  public IDisposable As(Guid company)
  {
    var previous = chosen.Value;
    chosen.Value = company;
    return new Restore(() => chosen.Value = previous);
  }

  private sealed class Restore(Action restore) : IDisposable
  {
    public void Dispose() => restore();
  }
}
