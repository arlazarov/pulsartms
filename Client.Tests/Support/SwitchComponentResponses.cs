using Client.Models.DTO.Execution;

namespace Client.Tests.Support;

internal static class SwitchComponentResponses
{
  public static SwitchWorkspace Workspace(Guid load)
  {
    var truck = Guid.NewGuid();
    var trailer = Guid.NewGuid();
    var assignment = new ExecutionAssignment(truck, null, trailer);
    return new(
      [
        new(
          load,
          1377,
          new string('a', 64),
          assignment,
          null,
          null,
          Enumerable
            .Range(1, 4)
            .Select(i => new SwitchVisitOption(
              Guid.NewGuid(),
              i,
              "Pick Up",
              $"Visit {i}",
              $"Address {i}",
              35,
              -80,
              false
            ))
            .ToArray()
        ),
      ],
      [new(truck, "54777"), new(Guid.NewGuid(), "11005")],
      [],
      [new(trailer, "44120")],
      []
    );
  }

  public static SwitchDetails Operation(Guid load)
  {
    var before = new SwitchAssignmentOption(
      new(Guid.NewGuid(), null, Guid.NewGuid()),
      "54777",
      "Driver A",
      "44120"
    );
    var after = new SwitchAssignmentOption(
      before.Resources with
      {
        TruckId = Guid.NewGuid(),
      },
      "11005",
      "Driver B",
      "44120"
    );
    return new(
      Guid.NewGuid(),
      "planned",
      7,
      "Transfer yard",
      35,
      -80,
      true,
      [
        new(
          Guid.NewGuid(),
          load,
          1377,
          "drop_hook",
          3,
          Guid.NewGuid(),
          5,
          Guid.NewGuid(),
          2,
          before,
          after,
          Guid.NewGuid(),
          Guid.NewGuid(),
          null,
          null,
          null,
          null,
          true,
          false
        ),
      ]
    );
  }
}
