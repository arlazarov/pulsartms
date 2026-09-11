using Client.Pages.Base;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Users;

public partial class EditUser : FormPage
{
  [Parameter]
  public Guid Id { get; set; }

  [Inject]
  protected ApiService Api { get; set; } = default!;

  [Inject]
  protected NavigationManager Navigation { get; set; } = default!;

  protected EditUserModel Model { get; set; } = new();

  protected bool IsLoading { get; set; }

  protected bool IsSaving { get; set; }

  protected string? LoadError { get; set; }

  protected override async Task OnInitializedAsync()
  {
    await LoadUserAsync();
  }

  private async Task LoadUserAsync()
  {
    IsLoading = true;
    LoadError = null;

    var result = await Api.GetAsync<Models.DTO.UserDTO>($"api/users/{Id}");

    if (!result.Success || result.Response is null)
    {
      LoadError = result.ErrorMessage;
      IsLoading = false;
      return;
    }

    Model.Name = result.Response.Name;
    Model.Email = result.Response.Email;
    Model.IsActive = result.Response.IsActive;
    Model.Role = result.Response.Role;

    IsLoading = false;
  }

  protected async Task UpdateUserAsync()
  {
    IsSaving = true;
    Errors.Clear();

    var request = new UpdateUserRequest
    {
      IsActive = Model.IsActive,
      Role = Model.Role,
      Name = Model.Name,
      Email = Model.Email,
      Password = string.IsNullOrWhiteSpace(Model.Password) ? null : Model.Password,
    };

    var result = await Api.PatchAsync<UpdateUserRequest, Guid>($"api/users/{Id}", request);

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

  protected class EditUserModel
  {
    public string Role { get; set; } = "Dispatch";
    public bool IsActive { get; set; }
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Password { get; set; }
  }

  protected class UpdateUserRequest
  {
    public string Role { get; set; } = "Dispatch";
    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public bool? IsActive { get; set; }
  }
}
