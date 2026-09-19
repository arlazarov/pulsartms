namespace Application.Models;

public sealed record RequestTiming(long Count, long Failed, long Cancelled, double TotalMs, double MaxMs);
