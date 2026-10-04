using FitnessApp.Domain.Users;
using FitnessApp.Domain.Nutrition;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessApp.IntegrationTests;

public sealed class RegistrationMigrationTests
{
    private const string InitialMigration = "20260907121032_InitialIdentity";
    private const string RegistrationMigration = "20260907185305_AddRegistrationApprovalMetadata";
    private const string PasswordResetMigration = "20260909044755_AddPasswordResetCooldown";
    private const string StrengthProgramsMigration = "20260909170745_AddStrengthPrograms";
    private const string ExerciseDetailsMigration = "20260910180425_AddExerciseDetails";
    private const string StrengthTrainingFlowMigration = "20260910191738_AddStrengthTrainingFlow";
    private const string CompletedWorkoutCompletionIdMigration = "20260910210000_AddCompletedWorkoutCompletionId";
    private const string RunningModuleMigration = "20260910221108_AddRunningModule";
    private const string CalendarOccurrenceMovesMigration = "20260914193743_AddCalendarOccurrenceMoves";
    private const string SettingsAndInAppNotificationsMigration = "20260922000000_AddSettingsAndInAppNotifications";
    private const string CompletedWorkoutOccurrenceLinkMigration = "20260922145225_AddCompletedWorkoutOccurrenceLink";
    private const string NutritionModuleMigration = "20260926193048_AddNutritionModule";
    private const string ConsolidateNutritionGoalsAndReceiptsMigration = "20260927083716_ConsolidateNutritionGoalsAndReceipts";
    private const string UserNutritionFoodsMigration = "20260927104634_AddUserNutritionFoods";
    private const string NutritionFoodAdditionReceiptsMigration = "20260927152000_AddNutritionFoodAdditionReceipts";
    private const string NutritionRecipeAdditionReceiptsMigration = "20260927165000_DetachNutritionRecipeAdditionReceipts";
    private const string NutritionRecipeCreationReceiptsMigration = "20261001195000_AddNutritionRecipeCreationReceipts";

    [Fact]
    public async Task LatestMigrationAppliesToEmptyDatabase()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await using var dbContext = CreateContext(databasePath);

            await dbContext.Database.MigrateAsync();

            Assert.Equal(
                [InitialMigration, RegistrationMigration, PasswordResetMigration, StrengthProgramsMigration, ExerciseDetailsMigration,
                    StrengthTrainingFlowMigration, CompletedWorkoutCompletionIdMigration, RunningModuleMigration,
                    CalendarOccurrenceMovesMigration, SettingsAndInAppNotificationsMigration,
                    CompletedWorkoutOccurrenceLinkMigration, NutritionModuleMigration,
                    ConsolidateNutritionGoalsAndReceiptsMigration, UserNutritionFoodsMigration,
                    NutritionFoodAdditionReceiptsMigration, NutritionRecipeAdditionReceiptsMigration,
                    NutritionRecipeCreationReceiptsMigration],
                await dbContext.Database.GetAppliedMigrationsAsync());
            var columns = await ReadUserColumnsAsync(databasePath);
            Assert.Contains("RegisteredAt", columns);
            Assert.Contains("DecidedAt", columns);
            Assert.Contains("DecidedByUserId", columns);
            Assert.Contains("LastPasswordResetEmailQueuedAt", columns);
            Assert.Contains("SecurityStamp", await ReadColumnsAsync(databasePath, "UserSessions"));
            Assert.Contains("Name", await ReadColumnsAsync(databasePath, "StrengthPrograms"));
            Assert.Contains("WorkoutId", await ReadColumnsAsync(databasePath, "ProgramExercises"));
            Assert.DoesNotContain("ProgramId", await ReadColumnsAsync(databasePath, "ProgramExercises"));
            Assert.Contains("Weight", await ReadColumnsAsync(databasePath, "ProgramExercises"));
            Assert.Contains("Note", await ReadColumnsAsync(databasePath, "ProgramExercises"));
            Assert.Contains("Position", await ReadColumnsAsync(databasePath, "ProgramExercises"));
            Assert.DoesNotContain("IsWarmUp", await ReadColumnsAsync(databasePath, "ProgramExercises"));
            Assert.Contains("ProgramId", await ReadColumnsAsync(databasePath, "ProgramWorkouts"));
            Assert.Contains("WorkoutId", await ReadColumnsAsync(databasePath, "ProgramScheduleEntries"));
            Assert.Contains("IsCompleted", await ReadColumnsAsync(databasePath, "CompletedWorkoutExercises"));
            Assert.Contains("CompletionId", await ReadColumnsAsync(databasePath, "CompletedWorkouts"));
            Assert.Contains("ScheduledOccurrenceDate", await ReadColumnsAsync(databasePath, "CompletedWorkouts"));
            Assert.Contains("TargetDate", await ReadColumnsAsync(databasePath, "CalendarOccurrenceMoves"));
            Assert.Contains("TrainingRemindersEnabled", await ReadColumnsAsync(databasePath, "UserSettings"));
            Assert.Contains("SourceKey", await ReadColumnsAsync(databasePath, "InAppNotifications"));
            Assert.Contains("CreatedFromDate", await ReadColumnsAsync(databasePath, "NutritionRecipes"));
            Assert.Contains("RequestId", await ReadColumnsAsync(databasePath, "NutritionRecipeAdditions"));
            Assert.Contains("RequestId", await ReadColumnsAsync(databasePath, "NutritionRecipeCreations"));
            Assert.Contains("RequestId", await ReadColumnsAsync(databasePath, "NutritionFoodAdditions"));
            Assert.DoesNotContain("NutritionTargets", await ReadTableNamesAsync(databasePath));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task NutritionReceiptMigrationsBackfillLogsAndPreserveRecipeReceipts()
    {
        var databasePath = NewDatabasePath();
        const string userId = "nutrition-receipt-user";
        var mealId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        var recipeRequestId = Guid.NewGuid();
        try
        {
            await using (var dbContext = CreateContext(databasePath))
                await dbContext.GetService<IMigrator>().MigrateAsync(UserNutritionFoodsMigration);

            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO AspNetUsers (
                        Id, DisplayName, ApprovalStatus, UserName, NormalizedUserName,
                        Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                        SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
                        TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
                    VALUES (
                        $userId, 'Nutrition user', 1, 'nutrition@example.test', 'NUTRITION@EXAMPLE.TEST',
                        'nutrition@example.test', 'NUTRITION@EXAMPLE.TEST', 1, 'password-hash',
                        'security-stamp', 'user-stamp', NULL, 0, 0, NULL, 1, 0);
                    INSERT INTO NutritionRecipes (Id, UserId, Name, Portions, CreatedAtUtc, CreatedFromDate, CreatedFromSlot)
                    VALUES ($recipeId, $userId, 'Frokost', 1, '2026-09-26 12:00:00', '2026-09-26', 2);
                    INSERT INTO NutritionRecipeAdditions (UserId, RequestId, RecipeId, Date, Slot, Portions)
                    VALUES ($userId, $recipeRequestId, $recipeId, '2026-09-27', 4, 1.5);
                    INSERT INTO NutritionMeals (Id, UserId, Date, Slot)
                    VALUES ($mealId, $userId, '2026-09-26', 2);
                    INSERT INTO NutritionFoodEntries (
                        Id, MealId, FoodId, CustomFoodId, Name, FoodGroup, CatalogueVersion,
                        Grams, EnergyKcalPer100g, ProteinPer100g, CarbohydratePer100g, FatPer100g, SugarPer100g)
                    VALUES (
                        $entryId, $mealId, 1, NULL, 'Æble, rå', 'Frugt', 'frida-5.5',
                        125.5, 52, 0.3, 11.4, 0.2, 10.3);
                    """;
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$recipeId", recipeId.ToString());
                command.Parameters.AddWithValue("$recipeRequestId", recipeRequestId.ToString());
                command.Parameters.AddWithValue("$mealId", mealId.ToString());
                command.Parameters.AddWithValue("$entryId", entryId.ToString());
                await command.ExecuteNonQueryAsync();
            }

            await using (var dbContext = CreateContext(databasePath))
            {
                await dbContext.Database.MigrateAsync();
                var receipt = await dbContext.NutritionFoodAdditions.AsNoTracking().SingleAsync();
                Assert.Equal(userId, receipt.UserId);
                Assert.Equal(entryId, receipt.RequestId);
                Assert.Equal(new DateOnly(2026, 9, 26), receipt.Date);
                Assert.Equal(NutritionMealSlot.Lunch, receipt.Slot);
                Assert.Equal(125.5m, receipt.Grams);
                var recipeReceipt = await dbContext.NutritionRecipeAdditions.AsNoTracking().SingleAsync();
                Assert.Equal(userId, recipeReceipt.UserId);
                Assert.Equal(recipeRequestId, recipeReceipt.RequestId);
                Assert.Equal(recipeId, recipeReceipt.RecipeId);
                Assert.Equal(new DateOnly(2026, 9, 27), recipeReceipt.Date);
                Assert.Equal(NutritionMealSlot.Dinner, recipeReceipt.Slot);
                Assert.Equal(1.5m, recipeReceipt.Portions);
                var creationReceipt = await dbContext.NutritionRecipeCreations.AsNoTracking().SingleAsync();
                Assert.Equal(userId, creationReceipt.UserId);
                Assert.Equal(recipeId, creationReceipt.RequestId);
                Assert.Equal("Frokost", creationReceipt.Name);
                Assert.Equal(new DateOnly(2026, 9, 26), creationReceipt.Date);
                Assert.Equal(NutritionMealSlot.Lunch, creationReceipt.Slot);
                Assert.Equal(1m, creationReceipt.Portions);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task NutritionReceiptDowngradeDoesNotLinkReceiptsAcrossOwnersWhenRecipeIdsAreReused()
    {
        var databasePath = NewDatabasePath();
        const string firstUserId = "nutrition-receipt-owner-a";
        const string secondUserId = "nutrition-receipt-owner-b";
        var recipeId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        try
        {
            await using (var dbContext = CreateContext(databasePath))
                await dbContext.GetService<IMigrator>().MigrateAsync(UserNutritionFoodsMigration);

            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO AspNetUsers (
                        Id, DisplayName, ApprovalStatus, UserName, NormalizedUserName,
                        Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                        SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
                        TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
                    VALUES
                        ($firstUserId, 'First owner', 1, 'first@example.test', 'FIRST@EXAMPLE.TEST',
                            'first@example.test', 'FIRST@EXAMPLE.TEST', 1, 'password-hash',
                            'security-stamp-a', 'user-stamp-a', NULL, 0, 0, NULL, 1, 0),
                        ($secondUserId, 'Second owner', 1, 'second@example.test', 'SECOND@EXAMPLE.TEST',
                            'second@example.test', 'SECOND@EXAMPLE.TEST', 1, 'password-hash',
                            'security-stamp-b', 'user-stamp-b', NULL, 0, 0, NULL, 1, 0);
                    INSERT INTO NutritionRecipes (Id, UserId, Name, Portions, CreatedAtUtc, CreatedFromDate, CreatedFromSlot)
                    VALUES ($recipeId, $firstUserId, 'Måltid A', 1, '2026-09-26 12:00:00', '2026-09-26', 2);
                    INSERT INTO NutritionRecipeAdditions (UserId, RequestId, RecipeId, Date, Slot, Portions)
                    VALUES ($firstUserId, $requestId, $recipeId, '2026-09-27', 4, 1);
                    """;
                command.Parameters.AddWithValue("$firstUserId", firstUserId);
                command.Parameters.AddWithValue("$secondUserId", secondUserId);
                command.Parameters.AddWithValue("$recipeId", recipeId.ToString());
                command.Parameters.AddWithValue("$requestId", requestId.ToString());
                await command.ExecuteNonQueryAsync();
            }

            await using (var dbContext = CreateContext(databasePath))
                await dbContext.Database.MigrateAsync();

            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    DELETE FROM NutritionRecipes WHERE Id = $recipeId;
                    INSERT INTO NutritionRecipes (Id, UserId, Name, Portions, CreatedAtUtc, CreatedFromDate, CreatedFromSlot)
                    VALUES ($recipeId, $secondUserId, 'Måltid B', 1, '2026-09-27 12:00:00', '2026-09-27', 3);
                    """;
                command.Parameters.AddWithValue("$recipeId", recipeId.ToString());
                command.Parameters.AddWithValue("$secondUserId", secondUserId);
                await command.ExecuteNonQueryAsync();
            }

            await using (var dbContext = CreateContext(databasePath))
            {
                await dbContext.GetService<IMigrator>().MigrateAsync(NutritionFoodAdditionReceiptsMigration);
                Assert.Empty(await dbContext.NutritionRecipeAdditions.AsNoTracking().ToListAsync());
                await dbContext.Database.OpenConnectionAsync();
                await using var command = dbContext.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM NutritionRecipes WHERE Id = $recipeId AND UserId = $secondUserId";
                command.Parameters.Add(new SqliteParameter("$recipeId", recipeId.ToString()));
                command.Parameters.Add(new SqliteParameter("$secondUserId", secondUserId));
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task NutritionMigrationPreservesFractionalLegacyTargetsAndExistingSettings()
    {
        var databasePath = NewDatabasePath();
        const string userId = "nutrition-target-user";
        const string roleId = "nutrition-target-role";
        try
        {
            await using (var dbContext = CreateContext(databasePath))
            {
                var migrator = dbContext.GetService<IMigrator>();
                await migrator.MigrateAsync(NutritionModuleMigration);
            }

            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                    VALUES ($roleId, 'User', 'USER', 'role-stamp');
                    INSERT INTO AspNetUsers (
                        Id, DisplayName, ApprovalStatus, UserName, NormalizedUserName,
                        Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                        SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
                        TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
                    VALUES (
                        $userId, 'Nutrition user', 1, 'nutrition@example.test', 'NUTRITION@EXAMPLE.TEST',
                        'nutrition@example.test', 'NUTRITION@EXAMPLE.TEST', 1, 'password-hash',
                        'security-stamp', 'user-stamp', NULL, 0, 0, NULL, 1, 0);
                    INSERT INTO UserSettings (UserId, DailyCaloriesTarget, ProteinTargetGrams,
                        CarbohydrateTargetGrams, FatTargetGrams, SugarTargetGrams,
                        TrainingRemindersEnabled, AdminRequestNotificationsEnabled,
                        IsGarminDemoConnected)
                    VALUES ($userId, 2000, NULL, NULL, NULL, NULL, 1, 1, 0);
                    INSERT INTO NutritionTargets (UserId, EnergyKcal, Protein, Carbohydrate, Fat, Sugar)
                    VALUES ($userId, 2300.5, 120.5, 250.25, 70.75, 45.5);
                    """;
                command.Parameters.AddWithValue("$roleId", roleId);
                command.Parameters.AddWithValue("$userId", userId);
                await command.ExecuteNonQueryAsync();
            }

            await using (var dbContext = CreateContext(databasePath))
            {
                await dbContext.Database.MigrateAsync();
                var settings = await dbContext.UserSettings.AsNoTracking().SingleAsync(item => item.UserId == userId);
                Assert.Equal(2000m, settings.DailyCaloriesTarget);
                Assert.Equal(120.5m, settings.ProteinTargetGrams);
                Assert.Equal(250.25m, settings.CarbohydrateTargetGrams);
                Assert.Equal(70.75m, settings.FatTargetGrams);
                Assert.Equal(45.5m, settings.SugarTargetGrams);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task UpgradePreservesExistingIdentityDataAndAddsNullableMetadata()
    {
        var databasePath = NewDatabasePath();
        const string userId = "existing-user";
        const string roleId = "existing-role";
        const string passwordHash = "existing-password-hash";
        try
        {
            await using (var initialContext = CreateContext(databasePath))
            {
                var migrator = initialContext.GetService<IMigrator>();
                await migrator.MigrateAsync(InitialMigration);
            }

            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                    VALUES ($roleId, 'User', 'USER', 'role-stamp');
                    INSERT INTO AspNetUsers (
                        Id, DisplayName, ApprovalStatus, UserName, NormalizedUserName,
                        Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                        SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
                        TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
                    VALUES (
                        $userId, 'Eksisterende bruger', 1, 'existing@example.test', 'EXISTING@EXAMPLE.TEST',
                        'existing@example.test', 'EXISTING@EXAMPLE.TEST', 1, $passwordHash,
                        'security-stamp', 'user-stamp', NULL, 0, 0, NULL, 1, 0);
                    INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES ($userId, $roleId);
                    INSERT INTO UserSessions (Id, UserId, CreatedAt, ExpiresAt, RevokedAt)
                    VALUES ('existing-session', $userId, '2026-09-08T00:00:00+00:00', '2026-09-08T01:00:00+00:00', NULL);
                    """;
                command.Parameters.AddWithValue("$roleId", roleId);
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$passwordHash", passwordHash);
                await command.ExecuteNonQueryAsync();
            }

            await using (var upgradedContext = CreateContext(databasePath))
            {
                await upgradedContext.Database.MigrateAsync();
                var user = await upgradedContext.Users.AsNoTracking().SingleAsync();
                Assert.Equal(userId, user.Id);
                Assert.Equal("Eksisterende bruger", user.DisplayName);
                Assert.Equal(AccountApprovalStatus.Approved, user.ApprovalStatus);
                Assert.Equal(passwordHash, user.PasswordHash);
                Assert.Null(user.RegisteredAt);
                Assert.Null(user.DecidedAt);
                Assert.Null(user.DecidedByUserId);
                Assert.Null(user.LastPasswordResetEmailQueuedAt);
                Assert.Equal(1, await upgradedContext.UserRoles.CountAsync());
                var session = await upgradedContext.UserSessions.AsNoTracking().SingleAsync();
                Assert.Equal("existing-session", session.Id);
                Assert.Null(session.SecurityStamp);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static FitnessDbContext CreateContext(string databasePath) =>
        new(new DbContextOptionsBuilder<FitnessDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options);

    private static async Task<HashSet<string>> ReadUserColumnsAsync(string databasePath)
        => await ReadColumnsAsync(databasePath, "AspNetUsers");

    private static async Task<HashSet<string>> ReadColumnsAsync(string databasePath, string tableName)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"fitnessapp-migration-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
