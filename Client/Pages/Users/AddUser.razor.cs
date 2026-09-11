using Client.Pages.Base;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Users;

public partial class AddUser : FormPage
{
  [Inject]
  protected ApiService Api { get; set; } = default!;

  [Inject]
  protected NavigationManager Navigation { get; set; } = default!;

  protected AddUserModel Model { get; set; } = new();

  protected bool IsSaving { get; set; }

  protected async Task CreateUserAsync()
  {
    IsSaving = true;
    Errors.Clear();

    var result = await Api.PostAsync<AddUserModel, Guid>("api/users", Model);

    if (!result.Success)
    {
      SetErrors(result.Errors);
      IsSaving = false;
      return;
    }

    Navigation.NavigateTo("/users");
  }

  protected void Cancel()
  {
    Navigation.NavigateTo("/users");
  }

  protected class AddUserModel
  {
    public string Role { get; set; } = "Dispatch";
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
  }
}
