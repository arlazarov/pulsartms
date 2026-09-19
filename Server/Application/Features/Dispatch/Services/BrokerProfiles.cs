using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Services;

public static class BrokerProfiles
{
  public static BrokerProfile Read(Customer customer)
  {
    var profile = DispatchWorkspaceData.Read<BrokerProfile>(
      customer.ProfileJson
    );
    profile.Id = customer.Id;
    profile.Name = customer.Name;
    profile.Revision = customer.ProfileRevision;
    return profile;
  }

  public static string? Validate(BrokerProfile? profile)
  {
    if (
      profile is null
      || profile.Id == Guid.Empty
      || profile.Revision is < 0 or long.MaxValue
      || string.IsNullOrWhiteSpace(profile.Name)
      || profile.Name.Length > 200
      || profile.Contact is null
      || profile.Contact.Length > 200
      || profile.Phone is null
      || profile.Phone.Length > 100
      || !DispatchBillingRules.Email(profile.Email)
      || profile.BillTo is null
      || profile.BillTo.Length > 2000
    )
      return "Check broker identity and contact fields.";
    return DispatchBillingRules.TermsError(profile.Terms);
  }
}
