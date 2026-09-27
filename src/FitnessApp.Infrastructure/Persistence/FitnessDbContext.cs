using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FitnessApp.Domain.Calendar;
using FitnessApp.Domain.Running;
using FitnessApp.Domain.Strength;
using FitnessApp.Infrastructure.Nutrition;

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
    public DbSet<FridaFood> FridaFoods => Set<FridaFood>();
    public DbSet<FridaCatalogueRelease> FridaCatalogueReleases => Set<FridaCatalogueRelease>();
    public DbSet<NutritionTarget> NutritionTargets => Set<NutritionTarget>();
    public DbSet<NutritionMeal> NutritionMeals => Set<NutritionMeal>();
    public DbSet<NutritionFoodEntry> NutritionFoodEntries => Set<NutritionFoodEntry>();
    public DbSet<NutritionRecipe> NutritionRecipes => Set<NutritionRecipe>();
    public DbSet<NutritionRecipeIngredient> NutritionRecipeIngredients => Set<NutritionRecipeIngredient>();

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
            entity.HasIndex(workout => new
                { workout.UserId, workout.ProgramId, workout.WorkoutId, workout.ScheduledOccurrenceDate })
                .IsUnique()
                .HasFilter("\"ScheduledOccurrenceDate\" IS NOT NULL");
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
        builder.Entity<FridaFood>(entity =>
        {
            entity.HasKey(food => food.FoodId);
            entity.Property(food => food.DanishName).HasMaxLength(500).IsRequired();
            entity.Property(food => food.FoodGroup).HasMaxLength(300).IsRequired();
            entity.Property(food => food.SearchName).HasMaxLength(700).IsRequired();
            entity.Property(food => food.PublishedNutrientsJson).IsRequired();
            entity.HasIndex(food => food.SearchName);
        });
        builder.Entity<FridaCatalogueRelease>(entity =>
        {
            entity.HasKey(release => release.Id);
            entity.Property(release => release.Version).HasMaxLength(32).IsRequired();
            entity.Property(release => release.SourceUrl).HasMaxLength(1024).IsRequired();
            entity.Property(release => release.Checksum).HasMaxLength(64).IsRequired();
        });
        builder.Entity<NutritionTarget>(entity =>
        {
            entity.HasKey(target => target.UserId);
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<NutritionTarget>(target => target.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<NutritionMeal>(entity =>
        {
            entity.HasKey(meal => meal.Id);
            entity.Property(meal => meal.UserId).HasMaxLength(450).IsRequired();
            entity.Property(meal => meal.Slot).HasConversion<int>();
            entity.HasIndex(meal => new { meal.UserId, meal.Date, meal.Slot }).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(meal => meal.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(meal => meal.Entries).WithOne(entry => entry.Meal).HasForeignKey(entry => entry.MealId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<NutritionFoodEntry>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Name).HasMaxLength(500).IsRequired();
            entity.Property(entry => entry.FoodGroup).HasMaxLength(300).IsRequired();
            entity.Property(entry => entry.CatalogueVersion).HasMaxLength(32).IsRequired();
        });
        builder.Entity<NutritionRecipe>(entity =>
        {
            entity.HasKey(recipe => recipe.Id);
            entity.Property(recipe => recipe.UserId).HasMaxLength(450).IsRequired();
            entity.Property(recipe => recipe.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(recipe => new { recipe.UserId, recipe.Name });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(recipe => recipe.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(recipe => recipe.Ingredients).WithOne(ingredient => ingredient.Recipe)
                .HasForeignKey(ingredient => ingredient.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<NutritionRecipeIngredient>(entity =>
        {
            entity.HasKey(ingredient => ingredient.Id);
            entity.Property(ingredient => ingredient.Name).HasMaxLength(500).IsRequired();
            entity.Property(ingredient => ingredient.FoodGroup).HasMaxLength(300).IsRequired();
            entity.Property(ingredient => ingredient.CatalogueVersion).HasMaxLength(32).IsRequired();
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
