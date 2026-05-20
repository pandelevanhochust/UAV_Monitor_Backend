namespace UavSystem.UserService.Application.DTOs;

public sealed record UserDto(Guid Id, string Name, string Email, string Role, DateTime CreatedAt);
