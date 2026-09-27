using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class WorkspaceFingerprintTests
{
  [Fact]
  public void PriceScaleIsNotAChangeButPriceValueIs()
  {
    var load = new DispatchEntity { Id = Guid.NewGuid(), Price = 1200m };
    var original = DispatchWorkspaceData.Fingerprint(load, []);
    load.Price = 1200.00m;
    Assert.Equal(original, DispatchWorkspaceData.Fingerprint(load, []));
    var json = DispatchWorkspaceData.Write(
      DispatchWorkspaceData.Commercial(load)
    );
    Assert.Equal(
      load.Price,
      DispatchWorkspaceData.Read<DispatchWorkspaceMetadata>(json).Price
    );
    load.Price = 1200.01m;
    Assert.NotEqual(original, DispatchWorkspaceData.Fingerprint(load, []));
  }
}
