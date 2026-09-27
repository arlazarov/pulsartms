namespace Domain.Entities.Dispatch;

// The last read ticket a carrier's load import handed out for a provider
// (audit F21). Each pass takes the next before it reads the provider; a
// pass that began reading later holds a larger ticket, and a load keeps
// the ticket of the pass that last wrote it (DispatchSourceLink.ReadTicket)
// so an earlier reading never overwrites a later one. The database orders
// the tickets; no clock is compared.
public sealed class DispatchImportRead : ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public string Provider { get; set; } = "";
  public long LastTicket { get; set; }
}
