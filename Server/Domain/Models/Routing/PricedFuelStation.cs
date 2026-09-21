namespace Domain.Models.Routing;

public sealed record PricedFuelStation(
  FuelPlanStop Station,
  double CashUsd,
  double EconomicUsd
);
