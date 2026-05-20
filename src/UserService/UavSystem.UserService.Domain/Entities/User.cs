using UavSystem.UserService.Domain.Enums;

namespace UavSystem.UserService.Domain.Entities;

/// <summary>
/// Core domain entity representing a system user (supervisor or monitor).
/// Immutable where possible — uses private set / init accessors.
/// No public parameterless constructor per domain entity conventions.
/// </summary>
public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; } = UserRole.Monitor;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    // Required by EF Core for materialization — private to enforce invariants
    private User() { }

    public User(string username, string email, string passwordHash, UserRole role)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));
        Role = role;
    }

    public void UpdateUsername(string username)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
    }

    public void UpdatePasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));
    }

    public void UpdateRole(UserRole role)
    {
        Role = role;
    }
}
