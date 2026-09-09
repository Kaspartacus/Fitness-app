using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Persistence;

public sealed class FitnessDbContext(DbContextOptions<FitnessDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<FitnessApp.Domain.Strength.StrengthProgram> StrengthPrograms => Set<FitnessApp.Domain.Strength.StrengthProgram>();
    public DbSet<FitnessApp.Domain.Strength.ProgramExercise> ProgramExercises => Set<FitnessApp.Domain.Strength.ProgramExercise>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FitnessApp.Domain.Strength.StrengthProgram>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Version).IsConcurrencyToken();
            entity.HasIndex(p => new { p.UserId, p.CreatedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.Exercises).WithOne().HasForeignKey(e => e.ProgramId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<FitnessApp.Domain.Strength.ProgramExercise>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => new { e.ProgramId, e.Position });
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.ApprovalStatus).HasConversion<int>().IsRequired();
            entity.Property(user => user.DecidedByUserId).HasMaxLength(450);
            entity.HasIndex(user => new { user.ApprovalStatus, user.RegisteredAt });
        });

        builder.Entity<UserSession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasMaxLength(32);
            entity.Property(session => session.SecurityStamp).HasMaxLength(450);
            entity.HasIndex(session => new { session.UserId, session.RevokedAt });
            entity.HasOne(session => session.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
