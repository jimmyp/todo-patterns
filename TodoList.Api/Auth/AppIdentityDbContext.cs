// TodoList.Api/Auth/AppIdentityDbContext.cs
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TodoList.Api.Auth;

/// <summary>
/// Separate EF context for ASP.NET Core Identity tables + ApiKeys.
/// Shares the same SQL Server database as TodoDbContext but keeps identity
/// schema separate so migrations can be managed independently.
/// </summary>
public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApiKeyEntity>(b =>
        {
            b.HasKey(k => k.Id);
            b.HasIndex(k => k.KeyHash).IsUnique();
            b.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
            b.Property(k => k.UserId).HasMaxLength(450).IsRequired();
            b.Property(k => k.Role).HasMaxLength(20).IsRequired();
        });
    }
}
