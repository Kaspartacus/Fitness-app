using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateNutritionGoalsAndReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "__NutritionUserSettings" (
                    "UserId" TEXT NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY,
                    "HeightCm" TEXT NULL,
                    "WeightKg" TEXT NULL,
                    "DailyCaloriesTarget" TEXT NULL,
                    "ProteinTargetGrams" TEXT NULL,
                    "CarbohydrateTargetGrams" TEXT NULL,
                    "FatTargetGrams" TEXT NULL,
                    "SugarTargetGrams" TEXT NULL,
                    "TrainingRemindersEnabled" INTEGER NOT NULL DEFAULT 1,
                    "AdminRequestNotificationsEnabled" INTEGER NOT NULL DEFAULT 1,
                    "IsGarminDemoConnected" INTEGER NOT NULL,
                    "GarminDemoConnectedAtUtc" TEXT NULL,
                    CONSTRAINT "FK_UserSettings_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );

                INSERT INTO "__NutritionUserSettings" (
                    UserId, HeightCm, WeightKg, DailyCaloriesTarget, ProteinTargetGrams,
                    CarbohydrateTargetGrams, FatTargetGrams, SugarTargetGrams,
                    TrainingRemindersEnabled, AdminRequestNotificationsEnabled,
                    IsGarminDemoConnected, GarminDemoConnectedAtUtc)
                SELECT UserId, HeightCm, WeightKg, DailyCaloriesTarget, ProteinTargetGrams,
                    CarbohydrateTargetGrams, FatTargetGrams, SugarTargetGrams,
                    TrainingRemindersEnabled, AdminRequestNotificationsEnabled,
                    IsGarminDemoConnected, GarminDemoConnectedAtUtc
                FROM UserSettings;

                INSERT INTO "__NutritionUserSettings" (
                    UserId, DailyCaloriesTarget, ProteinTargetGrams, CarbohydrateTargetGrams,
                    FatTargetGrams, SugarTargetGrams, TrainingRemindersEnabled,
                    AdminRequestNotificationsEnabled, IsGarminDemoConnected)
                SELECT legacy.UserId, legacy.EnergyKcal, legacy.Protein, legacy.Carbohydrate,
                    legacy.Fat, legacy.Sugar, 1, 1, 0
                FROM NutritionTargets AS legacy
                WHERE NOT EXISTS (SELECT 1 FROM "__NutritionUserSettings" AS settings WHERE settings.UserId = legacy.UserId)
                    AND (legacy.EnergyKcal IS NOT NULL OR legacy.Protein IS NOT NULL
                        OR legacy.Carbohydrate IS NOT NULL OR legacy.Fat IS NOT NULL OR legacy.Sugar IS NOT NULL);

                UPDATE "__NutritionUserSettings"
                SET DailyCaloriesTarget = COALESCE(DailyCaloriesTarget,
                        (SELECT EnergyKcal FROM NutritionTargets WHERE UserId = "__NutritionUserSettings".UserId)),
                    ProteinTargetGrams = COALESCE(ProteinTargetGrams,
                        (SELECT Protein FROM NutritionTargets WHERE UserId = "__NutritionUserSettings".UserId)),
                    CarbohydrateTargetGrams = COALESCE(CarbohydrateTargetGrams,
                        (SELECT Carbohydrate FROM NutritionTargets WHERE UserId = "__NutritionUserSettings".UserId)),
                    FatTargetGrams = COALESCE(FatTargetGrams,
                        (SELECT Fat FROM NutritionTargets WHERE UserId = "__NutritionUserSettings".UserId)),
                    SugarTargetGrams = COALESCE(SugarTargetGrams,
                        (SELECT Sugar FROM NutritionTargets WHERE UserId = "__NutritionUserSettings".UserId))
                WHERE UserId IN (SELECT UserId FROM NutritionTargets);

                DROP TABLE UserSettings;
                ALTER TABLE "__NutritionUserSettings" RENAME TO UserSettings;
                DROP TABLE NutritionTargets;
                """);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CreatedFromDate",
                table: "NutritionRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "CreatedFromSlot",
                table: "NutritionRecipes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NutritionRecipeAdditions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    Portions = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionRecipeAdditions", x => new { x.UserId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_NutritionRecipeAdditions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NutritionRecipeAdditions_NutritionRecipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "NutritionRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NutritionRecipeAdditions_RecipeId",
                table: "NutritionRecipeAdditions",
                column: "RecipeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NutritionRecipeAdditions");

            migrationBuilder.DropColumn(
                name: "CreatedFromDate",
                table: "NutritionRecipes");

            migrationBuilder.DropColumn(
                name: "CreatedFromSlot",
                table: "NutritionRecipes");

            migrationBuilder.CreateTable(
                name: "NutritionTargets",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Carbohydrate = table.Column<decimal>(type: "TEXT", nullable: true),
                    EnergyKcal = table.Column<decimal>(type: "TEXT", nullable: true),
                    Fat = table.Column<decimal>(type: "TEXT", nullable: true),
                    Protein = table.Column<decimal>(type: "TEXT", nullable: true),
                    Sugar = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionTargets", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_NutritionTargets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO NutritionTargets (UserId, EnergyKcal, Protein, Carbohydrate, Fat, Sugar)
                SELECT UserId, DailyCaloriesTarget, ProteinTargetGrams, CarbohydrateTargetGrams,
                    FatTargetGrams, SugarTargetGrams
                FROM UserSettings
                WHERE DailyCaloriesTarget IS NOT NULL OR ProteinTargetGrams IS NOT NULL
                    OR CarbohydrateTargetGrams IS NOT NULL OR FatTargetGrams IS NOT NULL
                    OR SugarTargetGrams IS NOT NULL;

                CREATE TABLE "__NutritionUserSettings" (
                    "UserId" TEXT NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY,
                    "HeightCm" TEXT NULL,
                    "WeightKg" TEXT NULL,
                    "DailyCaloriesTarget" INTEGER NULL,
                    "ProteinTargetGrams" INTEGER NULL,
                    "CarbohydrateTargetGrams" INTEGER NULL,
                    "FatTargetGrams" INTEGER NULL,
                    "SugarTargetGrams" INTEGER NULL,
                    "TrainingRemindersEnabled" INTEGER NOT NULL DEFAULT 1,
                    "AdminRequestNotificationsEnabled" INTEGER NOT NULL DEFAULT 1,
                    "IsGarminDemoConnected" INTEGER NOT NULL,
                    "GarminDemoConnectedAtUtc" TEXT NULL,
                    CONSTRAINT "FK_UserSettings_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );

                INSERT INTO "__NutritionUserSettings" (
                    UserId, HeightCm, WeightKg, DailyCaloriesTarget, ProteinTargetGrams,
                    CarbohydrateTargetGrams, FatTargetGrams, SugarTargetGrams,
                    TrainingRemindersEnabled, AdminRequestNotificationsEnabled,
                    IsGarminDemoConnected, GarminDemoConnectedAtUtc)
                SELECT UserId, HeightCm, WeightKg,
                    CASE WHEN DailyCaloriesTarget IS NULL THEN NULL ELSE CAST(ROUND(DailyCaloriesTarget) AS INTEGER) END,
                    CASE WHEN ProteinTargetGrams IS NULL THEN NULL ELSE CAST(ROUND(ProteinTargetGrams) AS INTEGER) END,
                    CASE WHEN CarbohydrateTargetGrams IS NULL THEN NULL ELSE CAST(ROUND(CarbohydrateTargetGrams) AS INTEGER) END,
                    CASE WHEN FatTargetGrams IS NULL THEN NULL ELSE CAST(ROUND(FatTargetGrams) AS INTEGER) END,
                    CASE WHEN SugarTargetGrams IS NULL THEN NULL ELSE CAST(ROUND(SugarTargetGrams) AS INTEGER) END,
                    TrainingRemindersEnabled, AdminRequestNotificationsEnabled,
                    IsGarminDemoConnected, GarminDemoConnectedAtUtc
                FROM UserSettings;

                DROP TABLE UserSettings;
                ALTER TABLE "__NutritionUserSettings" RENAME TO UserSettings;
                """);
        }
    }
}
