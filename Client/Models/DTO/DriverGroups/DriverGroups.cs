namespace Client.Models.DTO.DriverGroups;

// The signed-in dispatcher's own groups and the one chosen for every page
// (null for all drivers).
public sealed record DriverGroupsView(
  Guid? Selected,
  IReadOnlyList<DriverGroupView> Groups
);

public sealed record DriverGroupView(
  Guid Id,
  string Name,
  long Revision,
  IReadOnlyList<Guid> Drivers
);

public sealed record DriverGroupRequest(
  string Name,
  IReadOnlyList<Guid> Drivers,
  long Revision
);

public sealed record DriverGroupSelection(Guid? GroupId);
