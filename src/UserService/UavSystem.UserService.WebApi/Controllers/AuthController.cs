using MediatR;
using Microsoft.AspNetCore.Mvc;
using UavSystem.UserService.Application.DTOs;
using UavSystem.UserService.Application.Features.Auth.Commands;

namespace UavSystem.UserService.WebApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// POST /api/v1/auth/login — Public endpoint (no JWT required).
    /// Authenticates user credentials and returns a signed JWT.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken ct)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var result = await _sender.Send(command, ct);
        return Ok(result);
    }
}
