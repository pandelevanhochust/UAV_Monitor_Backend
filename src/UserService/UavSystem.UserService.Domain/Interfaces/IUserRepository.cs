using UavSystem.UserService.Domain.Entities;

namespace UavSystem.UserService.Domain.Interfaces;

/// <summary>
/// Repository contract for User aggregate. Defined in Domain, implemented in Infrastructure.
/// All methods propagate CancellationToken and return Task.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IEnumerable<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
