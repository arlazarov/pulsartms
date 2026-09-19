namespace Application.Features.Fuel.Models;

public sealed record FuelExchangeRate(
  decimal UsdPerCad,
  DateOnly ObservedOn,
  DateTime RetrievedAt
);
