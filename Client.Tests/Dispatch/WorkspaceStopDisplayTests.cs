using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class WorkspaceStopDisplayTests
{
  [Fact]
  public void GenericTransferNameUsesItsAddressWithoutChangingSavedData()
  {
    var stop = new DispatchWorkspaceStop
    {
      Name = "Transfer yard",
      Address = "145 Major Grahams Road",
      City = "Max Meadows",
      Province = "VA",
    };
    Assert.Equal(
      "145 Major Grahams Road, Max Meadows, VA",
      DispatchWorkspaceStopDisplay.Name(stop)
    );
    Assert.Equal("Transfer yard", stop.Name);
    stop.Name = "Love's Travel Stop";
    Assert.Equal(stop.Name, DispatchWorkspaceStopDisplay.Name(stop));
  }
}
