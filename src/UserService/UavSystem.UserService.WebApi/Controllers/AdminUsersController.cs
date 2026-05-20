using MediatR;
using Microsoft.AspNetCore.Mvc;
using UavSystem.UserService.Application.DTOs;
using UavSystem.UserService.Application.Features.Users.Commands;
using UavSystem.UserService.Domain.Interfaces;

namespace UavSystem.UserService.WebApi.Controllers;

[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IUserRepository _userRepository;

    public AdminUsersController(ISender sender, IUserRepository userRepository)
    {
        _sender = sender;
        _userRepository = userRepository;
    }

    /// <summary>
    /// POST /api/v1/admin/users — Creates a new user (ADMIN only).
    /// X-User-Role header injected by Kong ForwardAuth.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserCommand command,
        [FromHeader(Name = "X-User-Role")] string? userRole,
        CancellationToken ct)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var result = await _sender.Send(command, ct);
        return CreatedAtAction(nameof(GetUsers), new { id = result.Id }, result);
    }

    /// <summary>
    /// GET /api/v1/admin/users — Lists all users (ADMIN only).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromHeader(Name = "X-User-Role")] string? userRole,
        CancellationToken ct)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var users = await _userRepository.GetAllAsync(ct);
        var dtos = users.Select(u =>
            new UserDto(u.Id, u.Name, u.Email, u.Role.ToString(), u.CreatedAt));

        return Ok(dtos);
    }
}
