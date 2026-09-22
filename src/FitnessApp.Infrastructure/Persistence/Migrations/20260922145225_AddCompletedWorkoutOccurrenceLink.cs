using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedWorkoutOccurrenceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ScheduledOccurrenceDate",
                table: "CompletedWorkouts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompletedWorkouts_UserId_ProgramId_WorkoutId_ScheduledOccurrenceDate",
                table: "CompletedWorkouts",
                columns: new[] { "UserId", "ProgramId", "WorkoutId", "ScheduledOccurrenceDate" },
                unique: true,
                filter: "\"ScheduledOccurrenceDate\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompletedWorkouts_UserId_ProgramId_WorkoutId_ScheduledOccurrenceDate",
                table: "CompletedWorkouts");

            migrationBuilder.DropColumn(
                name: "ScheduledOccurrenceDate",
                table: "CompletedWorkouts");
        }
    }
}
