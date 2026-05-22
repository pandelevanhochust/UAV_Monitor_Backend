using Microsoft.EntityFrameworkCore;
using UavSystem.UserService.Domain.Entities;
using UavSystem.UserService.Domain.Enums;

namespace UavSystem.UserService.Infrastructure.Persistence;

public sealed class UserDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<UserRole>("public", "user_role");

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                  .HasColumnName("id")
                  .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.Username)
                  .HasColumnName("username")
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(e => e.Email)
                  .HasColumnName("email")
                  .HasMaxLength(150)
                  .IsRequired();

            entity.HasIndex(e => e.Email)
                  .IsUnique()
                  .HasDatabaseName("idx_users_email");

            entity.Property(e => e.PasswordHash)
                  .HasColumnName("password_hash")
                  .HasMaxLength(255)
                  .IsRequired();

            entity.Property(e => e.Role)
                  .HasColumnName("role")
                  .HasDefaultValue(UserRole.Monitor)
                  .HasConversion<string>();

            entity.Property(e => e.UpdatedAt)
                  .HasColumnName("updated_at")
                  .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
