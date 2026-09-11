using Client.Models.DTO.Planning;
namespace Client.Pages.Dispatch;

public static class DispatchRigStatus
{
    public static string Resolve(decimal speed, string? engineState, DriverHosClocks? hos, DateTime now)
    {
        if (speed >= 1) return "moving";
        var engine = engineState?.Trim().ToLowerInvariant();
        if (engine is "idle" or "idling" or "on" or "running") return "idling";
        if (engine != "off") return "parked";
        return hos?.CurrentDutyStatus == "sleeperBerth" && hos.UpdatedAt > now.AddMinutes(-15)
            ? "sleeping" : "off";
    }
}
