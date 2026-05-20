using System.Security.Claims;
using MediatR;
using UavSystem.UserService.Application.Interfaces;

namespace UavSystem.UserService.Application.Features.Auth.Queries;

/// <summary>
/// Validates a JWT token and returns the extracted claims.
/// Used by the gRPC UserGrpcService to serve Kong ForwardAuth requests.
/// </summary>
public sealed record ValidateTokenQuery(string Token) : IRequest<ValidateTokenResult>;

public sealed record ValidateTokenResult(string UserId, string Role, bool IsValid);

public sealed class ValidateTokenQueryHandler : IRequestHandler<ValidateTokenQuery, ValidateTokenResult>
{
    private readonly IJwtService _jwtService;

    public ValidateTokenQueryHandler(IJwtService jwtService)
    {
        _jwtService = jwtService;
    }

    public Task<ValidateTokenResult> Handle(ValidateTokenQuery request, CancellationToken cancellationToken)
    {
        var principal = _jwtService.ValidateToken(request.Token);

        if (principal is null)
            return Task.FromResult(new ValidateTokenResult(string.Empty, string.Empty, false));

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        return Task.FromResult(new ValidateTokenResult(userId, role, true));
    }
}
