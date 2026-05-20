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
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; } = UserRole.Monitor;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Required by EF Core for materialization — private to enforce invariants
    private User() { }

    public User(string name, string email, string passwordHash, UserRole role)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));
        Role = role;
    }

    public void UpdateName(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
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
