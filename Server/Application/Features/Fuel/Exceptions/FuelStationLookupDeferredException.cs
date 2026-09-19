namespace Application.Features.Fuel.Exceptions;

public sealed class FuelStationLookupDeferredException()
  : Exception(
    "A fuel station lookup is in progress or waiting to retry. Retry the import later."
  );
