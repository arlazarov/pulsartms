using Client.Models.DTO.Planning;

namespace Client.Shared.Fuel;

// Which of the two things a load's stop is, by the word the source used;
// the fuel plan and its editor name it the same way.
public static class StopJobs
{
  public static bool IsPickup(PlanStop stop) =>
    stop.Job.Contains("pick", StringComparison.OrdinalIgnoreCase);

  public static string Label(PlanStop stop) =>
    IsPickup(stop) ? "Pickup" : "Delivery";
}
