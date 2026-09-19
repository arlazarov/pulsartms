namespace Client.Models.DTO.Mileage;

public sealed record MileagePolicyState(
  long Revision,
  string YardReturn,
  string Home,
  string Maintenance,
  string Reposition,
  DateTime? UpdatedAt
);

public sealed record MileagePolicyUpdate(
  long Revision,
  string YardReturn,
  string Home,
  string Maintenance,
  string Reposition
);
