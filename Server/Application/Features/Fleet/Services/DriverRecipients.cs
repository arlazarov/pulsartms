using Domain.Entities.Fleet;
using Domain.Rules.Fleet;

namespace Application.Features.Fleet.Services;

// Drivers with the stored number the WhatsApp rule starts from, for a
// database query to filter, search and compare against conversations in
// one statement: the WhatsApp number when one is stored, otherwise the
// phone. DriverWhatsApp.Resolve still decides, per row, whether it is a
// usable recipient.
public static class DriverRecipients
{
  public static IQueryable<DriverRecipientRow> Candidates(
    IQueryable<Driver> drivers
  ) =>
    drivers.Select(x => new DriverRecipientRow
    {
      Id = x.Id,
      Name = x.Name,
      IsActive = x.IsActive,
      WhatsAppPhone = x.WhatsAppPhone,
      Phone = x.Phone,
      Candidate =
        x.WhatsAppPhone != null && x.WhatsAppPhone != ""
          ? x.WhatsAppPhone
          : x.Phone,
    });
}

public sealed class DriverRecipientRow
{
  public Guid Id { get; init; }
  public string Name { get; init; } = "";
  public bool IsActive { get; init; }
  public string? WhatsAppPhone { get; init; }
  public string? Phone { get; init; }
  public string? Candidate { get; init; }

  public DriverWhatsAppRecipient Recipient =>
    DriverWhatsApp.Resolve(WhatsAppPhone, Phone);
}
