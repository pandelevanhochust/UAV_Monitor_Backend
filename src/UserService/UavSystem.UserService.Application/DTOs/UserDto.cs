namespace UavSystem.UserService.Application.DTOs;

public sealed record UserDto(Guid Id, string Username, string Email, string Role, DateTime UpdatedAt);
