using Microsoft.AspNetCore.Components;

namespace Client.Shared.Brand.BrandLogo;

public partial class BrandLogo
{
  [Parameter]
  public bool Reversed { get; set; }

  [Parameter]
  public bool Prominent { get; set; }

  private string LogoClass =>
    $"brand-logo{(Reversed ? " brand-logo--reversed" : "")}{(Prominent ? " brand-logo--prominent" : "")}";
}
