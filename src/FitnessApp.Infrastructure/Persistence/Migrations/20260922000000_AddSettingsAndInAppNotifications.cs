using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FitnessDbContext))]
    [Migration("20260922000000_AddSettingsAndInAppNotifications")]
    public partial class AddSettingsAndInAppNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InAppNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InAppNotifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    HeightCm = table.Column<decimal>(type: "TEXT", precision: 5, scale: 1, nullable: true),
                    WeightKg = table.Column<decimal>(type: "TEXT", precision: 5, scale: 1, nullable: true),
                    DailyCaloriesTarget = table.Column<int>(type: "INTEGER", nullable: true),
                    ProteinTargetGrams = table.Column<int>(type: "INTEGER", nullable: true),
                    CarbohydrateTargetGrams = table.Column<int>(type: "INTEGER", nullable: true),
                    FatTargetGrams = table.Column<int>(type: "INTEGER", nullable: true),
                    SugarTargetGrams = table.Column<int>(type: "INTEGER", nullable: true),
                    TrainingRemindersEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    AdminRequestNotificationsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    IsGarminDemoConnected = table.Column<bool>(type: "INTEGER", nullable: false),
                    GarminDemoConnectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_UserId_ReadAtUtc_CreatedAtUtc",
                table: "InAppNotifications",
                columns: new[] { "UserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_UserId_SourceKey",
                table: "InAppNotifications",
                columns: new[] { "UserId", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InAppNotifications");

            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
