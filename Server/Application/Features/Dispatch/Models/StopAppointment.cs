using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Models;

public readonly record struct StopAppointment(
  DateOnly? Date,
  TimeOnly? Time,
  DateOnly? EndDate,
  TimeOnly? EndTime,
  bool IsWindow,
  string TimeZoneId
)
{
  public static StopAppointment From(DispatchStop stop) =>
    new(
      stop.ScheduledDate,
      stop.ScheduledTime,
      stop.ScheduledDate2,
      stop.ScheduledTime2,
      stop.IsWindow,
      stop.AppointmentTimeZoneId
    );

  public void ApplyTo(DispatchStop stop)
  {
    stop.ScheduledDate = Date;
    stop.ScheduledTime = Time;
    stop.ScheduledDate2 = EndDate;
    stop.ScheduledTime2 = EndTime;
    stop.IsWindow = IsWindow;
    stop.AppointmentTimeZoneId = TimeZoneId;
  }
}
