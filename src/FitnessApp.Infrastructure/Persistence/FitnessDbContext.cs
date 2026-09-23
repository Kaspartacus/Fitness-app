using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FitnessApp.Domain.Calendar;
using FitnessApp.Domain.Running;
using FitnessApp.Domain.Settings;
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

    public DbSet<RunningPlan> RunningPlans => Set<RunningPlan>();
    public DbSet<RunningPlanDay> RunningPlanDays => Set<RunningPlanDay>();
    public DbSet<RunningSession> RunningSessions => Set<RunningSession>();
    public DbSet<RunningResult> RunningResults => Set<RunningResult>();
    public DbSet<CalendarOccurrenceMove> CalendarOccurrenceMoves => Set<CalendarOccurrenceMove>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();

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
            entity.HasIndex(workout => new { workout.UserId, workout.CompletionId }).IsUnique();
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

        builder.Entity<RunningPlan>(entity =>
        {
            entity.HasKey(plan => plan.Id);
            entity.Property(plan => plan.Version).IsConcurrencyToken();
            entity.Property(plan => plan.ThirtyMinuteDistanceKm).HasPrecision(5, 2);
            entity.Property(plan => plan.TargetDistanceKm).HasPrecision(5, 2);
            entity.HasIndex(plan => new { plan.UserId, plan.CreatedAtUtc });
            entity.HasIndex(plan => plan.UserId).IsUnique().HasFilter("\"IsActive\" = 1");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(plan => plan.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(plan => plan.SelectedDays).WithOne().HasForeignKey(day => day.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(plan => plan.Sessions).WithOne().HasForeignKey(session => session.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<RunningPlanDay>(entity =>
        {
            entity.HasKey(day => day.Id);
            entity.HasIndex(day => new { day.PlanId, day.DayOfWeek }).IsUnique();
        });
        builder.Entity<RunningSession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.PlannedDistanceKm).HasPrecision(5, 2);
            entity.Property(session => session.Structure).HasMaxLength(RunningRules.MaxStructureLength).IsRequired();
            entity.HasIndex(session => new { session.PlanId, session.Date }).IsUnique();
            entity.HasIndex(session => new { session.PlanId, session.Position }).IsUnique();
        });
        builder.Entity<RunningResult>(entity =>
        {
            entity.HasKey(result => result.Id);
            entity.Property(result => result.DistanceKm).HasPrecision(5, 2);
            entity.Property(result => result.Note).HasMaxLength(RunningRules.MaxNoteLength);
            entity.Property(result => result.Version).IsConcurrencyToken();
            entity.HasIndex(result => new { result.UserId, result.Date });
            entity.HasIndex(result => new { result.UserId, result.CompletionId }).IsUnique();
            entity.HasIndex(result => result.SessionId).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(result => result.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RunningSession>().WithOne().HasForeignKey<RunningResult>(result => result.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<CalendarOccurrenceMove>(entity =>
        {
            entity.HasKey(move => move.Id);
            entity.Property(move => move.UserId).HasMaxLength(450).IsRequired();
            entity.Property(move => move.Kind).HasConversion<int>();
            entity.HasIndex(move => new
            {
                move.UserId,
                move.Kind,
                move.ScopeId,
                move.SourceId,
                move.OriginalDate
            }).IsUnique();
            entity.HasIndex(move => new { move.UserId, move.Kind, move.OriginalDate });
            entity.HasIndex(move => new { move.UserId, move.Kind, move.TargetDate });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(move => move.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(settings => settings.UserId);
            entity.Property(settings => settings.UserId).HasMaxLength(450);
            entity.Property(settings => settings.HeightCm).HasPrecision(5, 1);
            entity.Property(settings => settings.WeightKg).HasPrecision(5, 1);
            entity.Property(settings => settings.TrainingRemindersEnabled).HasDefaultValue(true);
            entity.Property(settings => settings.AdminRequestNotificationsEnabled).HasDefaultValue(true);
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserSettings>(settings => settings.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InAppNotification>(entity =>
        {
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.UserId).HasMaxLength(450).IsRequired();
            entity.Property(notification => notification.Kind).HasConversion<int>();
            entity.Property(notification => notification.SourceKey).HasMaxLength(200).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(500).IsRequired();
            entity.Property(notification => notification.TargetPath).HasMaxLength(300).IsRequired();
            entity.HasIndex(notification => new { notification.UserId, notification.SourceKey }).IsUnique();
            entity.HasIndex(notification => new { notification.UserId, notification.ReadAtUtc, notification.CreatedAtUtc });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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
