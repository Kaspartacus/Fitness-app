using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessApp.IntegrationTests;

public sealed class RegistrationMigrationTests
{
    private const string InitialMigration = "20260907121032_InitialIdentity";

    [Fact]
    public async Task LatestMigrationAppliesToEmptyDatabase()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await using var dbContext = CreateContext(databasePath);

            await dbContext.Database.MigrateAsync();

            Assert.Equal(
                [InitialMigration, "20260907185305_AddRegistrationApprovalMetadata"],
                await dbContext.Database.GetAppliedMigrationsAsync());
            var columns = await ReadUserColumnsAsync(databasePath);
            Assert.Contains("RegisteredAt", columns);
            Assert.Contains("DecidedAt", columns);
            Assert.Contains("DecidedByUserId", columns);
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
                Assert.Equal(1, await upgradedContext.UserRoles.CountAsync());
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
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('AspNetUsers');";
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
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
