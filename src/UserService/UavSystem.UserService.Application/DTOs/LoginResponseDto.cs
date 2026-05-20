namespace UavSystem.UserService.Application.DTOs;

public sealed record LoginResponseDto(string Token, string UserId, string Role, DateTime ExpiresAt);
