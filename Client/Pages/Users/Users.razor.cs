using Client.Models.DTO;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Users;

public partial class Users
{
  [Inject]
  protected ApiService Api { get; set; } = default!;

  protected PaginatedListDTO<UserDTO>? UsersData { get; set; }

  protected bool IsLoading { get; set; }

  protected bool IsDeleting { get; set; }

  protected string? Error { get; set; }

  protected int PageSize { get; set; } = 20;

  protected Guid? DeleteUserId { get; set; }

  protected override async Task OnInitializedAsync()
  {
    await LoadUsersAsync(1);
  }

  protected async Task PageChangedAsync(int page)
  {
    await LoadUsersAsync(page);
  }

  protected void AskDeleteUser(Guid id)
  {
    DeleteUserId = id;
  }

  protected void CancelDelete()
  {
    if (IsDeleting)
      return;

    DeleteUserId = null;
  }

  protected async Task DeleteUserAsync()
  {
    if (DeleteUserId is null)
      return;

    IsDeleting = true;
    Error = null;

    var result = await Api.DeleteAsync<Guid>($"api/users/{DeleteUserId}");

    if (!result.Success)
    {
      Error = result.ErrorMessage;
      IsDeleting = false;
      return;
    }

    DeleteUserId = null;
    IsDeleting = false;

    var currentPage = UsersData?.Page ?? 1;

    await LoadUsersAsync(currentPage);
  }

  private async Task LoadUsersAsync(int page)
  {
    IsLoading = true;
    Error = null;

    var result = await Api.GetAsync<PaginatedListDTO<UserDTO>>(
      $"api/users?page={page}&pageSize={PageSize}"
    );

    if (result.Success)
    {
      UsersData = result.Response;
    }
    else
    {
      Error = result.ErrorMessage;
    }

    IsLoading = false;
  }
}
