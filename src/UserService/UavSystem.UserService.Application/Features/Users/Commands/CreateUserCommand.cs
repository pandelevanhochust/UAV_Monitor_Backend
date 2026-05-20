using MediatR;
using UavSystem.UserService.Application.DTOs;
using UavSystem.UserService.Domain.Interfaces;

namespace UavSystem.UserService.Application.Features.Users.Commands;

/// <summary>
/// Creates a new user (ADMIN-only operation).
/// </summary>
public sealed record CreateUserCommand(
    string Username,
    string Email,
    string Password,
    string Role
) : IRequest<UserDto>;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IUserRepository _userRepository;

    public CreateUserCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var existing = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException($"User with email '{request.Email}' already exists.");

        var role = Enum.Parse<Domain.Enums.UserRole>(request.Role, ignoreCase: true);
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new Domain.Entities.User(request.Username, request.Email, passwordHash, role);
        await _userRepository.AddAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return new UserDto(user.Id, user.Username, user.Email, user.Role.ToString(), user.UpdatedAt);
    }
}
