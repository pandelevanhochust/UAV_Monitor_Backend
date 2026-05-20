using System.Security.Claims;

namespace UavSystem.UserService.Application.Interfaces;

/// <summary>
/// JWT service contract — defined in Application, implemented in Infrastructure.
/// </summary>
public interface IJwtService
{
    string GenerateToken(Guid userId, string email, string role);
    ClaimsPrincipal? ValidateToken(string token);
}
