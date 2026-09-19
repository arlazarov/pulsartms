using Client.Models.DTO.Planning;

namespace Client.Models.DTO.Fleet;

public sealed record TruckHosSnapshot(string DriverName, DriverHosClocks? Hos);
