using MediatR;
using UavSystem.UserService.Application.DTOs;
using UavSystem.UserService.Application.Interfaces;
using UavSystem.UserService.Domain.Interfaces;

namespace UavSystem.UserService.Application.Features.Auth.Commands;

/// <summary>
/// Authenticates a user and returns a signed JWT.
/// </summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResponseDto>;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponseDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;

    public LoginCommandHandler(IUserRepository userRepository, IJwtService jwtService)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
    }

    public async Task<LoginResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        var token = _jwtService.GenerateToken(user.Id, user.Email, user.Role.ToString());

        return new LoginResponseDto(
            Token: token,
            UserId: user.Id.ToString(),
            Role: user.Role.ToString(),
            ExpiresAt: DateTime.UtcNow.AddSeconds(86400)
        );
    }
}
