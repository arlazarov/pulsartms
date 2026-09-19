using Application.Features.Users.Commands;
using Application.Features.Users.Queries;
using Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/[controller]")]
public class UsersController : BaseController
{
  [Authorize]
  [HttpGet]
  public Task<IActionResult> GetUserList([FromQuery] ListQuery query) =>
    HandleRequest(
      new GetUserListQuery
      {
        Page = query.Page,
        PageSize = query.PageSize,
        Search = query.Search,
      }
    );

  [HttpGet("{id:guid}")]
  public Task<IActionResult> GetUser(Guid id) =>
    HandleRequest(new GetUserByIdQuery(id));

  [HttpPost]
  public Task<IActionResult> Register(RegisterUserCommand command) =>
    HandleRequest(command);

  [HttpPatch("{id:guid}")]
  public Task<IActionResult> UpdateUser(
    Guid id,
    UpdateUserCommand command,
    CancellationToken cancellationToken
  ) => HandleRequest(command with { Id = id }, cancellationToken);

  [HttpDelete("{id:guid}")]
  public Task<IActionResult> DeleteUser(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new DeleteUserCommand(id), cancellationToken);
}
