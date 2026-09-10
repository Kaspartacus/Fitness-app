using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FitnessApp.Domain.Strength;

namespace FitnessApp.Infrastructure.Persistence;

public sealed class FitnessDbContext(DbContextOptions<FitnessDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<StrengthProgram> StrengthPrograms => Set<StrengthProgram>();
    public DbSet<ProgramWorkout> ProgramWorkouts => Set<ProgramWorkout>();
    public DbSet<ProgramExercise> ProgramExercises => Set<ProgramExercise>();
    public DbSet<ProgramScheduleEntry> ProgramScheduleEntries => Set<ProgramScheduleEntry>();
    public DbSet<CompletedWorkout> CompletedWorkouts => Set<CompletedWorkout>();
    public DbSet<CompletedWorkoutExercise> CompletedWorkoutExercises => Set<CompletedWorkoutExercise>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<StrengthProgram>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Version).IsConcurrencyToken();
            entity.HasIndex(p => new { p.UserId, p.CreatedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.Workouts).WithOne().HasForeignKey(w => w.ProgramId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.Schedule).WithOne().HasForeignKey(entry => entry.ProgramId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ProgramWorkout>(entity =>
        {
            entity.HasKey(workout => workout.Id);
            entity.Property(workout => workout.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(workout => new { workout.ProgramId, workout.Position }).IsUnique();
            entity.HasMany(workout => workout.Exercises).WithOne().HasForeignKey(exercise => exercise.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ProgramExercise>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Weight).HasPrecision(7, 2);
            entity.Property(e => e.Note).HasMaxLength(250);
            entity.HasIndex(e => new { e.WorkoutId, e.Position }).IsUnique();
        });
        builder.Entity<ProgramScheduleEntry>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => new { entry.ProgramId, entry.DayOfWeek }).IsUnique();
            entity.HasOne<ProgramWorkout>().WithMany().HasForeignKey(entry => entry.WorkoutId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CompletedWorkout>(entity =>
        {
            entity.HasKey(workout => workout.Id);
            entity.Property(workout => workout.WorkoutName).HasMaxLength(100).IsRequired();
            entity.HasIndex(workout => new { workout.UserId, workout.CompletedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(workout => workout.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(workout => workout.Exercises).WithOne().HasForeignKey(exercise => exercise.CompletedWorkoutId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CompletedWorkoutExercise>(entity =>
        {
            entity.HasKey(exercise => exercise.Id);
            entity.Property(exercise => exercise.Name).HasMaxLength(100).IsRequired();
            entity.Property(exercise => exercise.Weight).HasPrecision(7, 2);
            entity.HasIndex(exercise => new { exercise.CompletedWorkoutId, exercise.Position }).IsUnique();
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
