using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunningModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunningPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReplacedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    ThirtyMinuteDistanceKm = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    TargetDistanceKm = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WeeklyFrequency = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunningPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunningPlans_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunningPlanDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunningPlanDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunningPlanDays_RunningPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "RunningPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunningSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedDistanceKm = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    PaceMinSecondsPerKm = table.Column<int>(type: "INTEGER", nullable: false),
                    PaceMaxSecondsPerKm = table.Column<int>(type: "INTEGER", nullable: false),
                    Structure = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunningSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunningSessions_RunningPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "RunningPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunningResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CompletionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DistanceKm = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AverageHeartRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunningResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunningResults_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunningResults_RunningSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "RunningSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunningPlanDays_PlanId_DayOfWeek",
                table: "RunningPlanDays",
                columns: new[] { "PlanId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunningPlans_UserId",
                table: "RunningPlans",
                column: "UserId",
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RunningPlans_UserId_CreatedAtUtc",
                table: "RunningPlans",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunningResults_SessionId",
                table: "RunningResults",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunningResults_UserId_CompletionId",
                table: "RunningResults",
                columns: new[] { "UserId", "CompletionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunningResults_UserId_Date",
                table: "RunningResults",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RunningSessions_PlanId_Date",
                table: "RunningSessions",
                columns: new[] { "PlanId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunningSessions_PlanId_Position",
                table: "RunningSessions",
                columns: new[] { "PlanId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunningPlanDays");

            migrationBuilder.DropTable(
                name: "RunningResults");

            migrationBuilder.DropTable(
                name: "RunningSessions");

            migrationBuilder.DropTable(
                name: "RunningPlans");
        }
    }
}
