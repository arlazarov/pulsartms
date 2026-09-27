namespace Client.Shared.Trucks;

// Moving or standing, from the last reported speed: the map draws the same
// split as an arrow or a circle (Scripts/fleetMap/rendering/
// truckAppearance.ts, held to this threshold by its tests).
public static class TruckMotion
{
  public const decimal MovingSpeed = 1m;

  public static bool IsMoving(decimal speed) => speed >= MovingSpeed;
}
