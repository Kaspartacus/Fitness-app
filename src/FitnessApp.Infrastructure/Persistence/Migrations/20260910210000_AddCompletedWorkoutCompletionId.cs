using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20260910210000_AddCompletedWorkoutCompletionId")]
public partial class AddCompletedWorkoutCompletionId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CompletionId",
            table: "CompletedWorkouts",
            type: "TEXT",
            nullable: false,
            defaultValue: Guid.Empty);

        // Existing history predates client completion IDs. Its immutable primary key supplies a unique stable value.
        migrationBuilder.Sql("UPDATE \"CompletedWorkouts\" SET \"CompletionId\" = \"Id\";");
        migrationBuilder.CreateIndex(
            name: "IX_CompletedWorkouts_UserId_CompletionId",
            table: "CompletedWorkouts",
            columns: new[] { "UserId", "CompletionId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_CompletedWorkouts_UserId_CompletionId", table: "CompletedWorkouts");
        migrationBuilder.DropColumn(name: "CompletionId", table: "CompletedWorkouts");
    }
}
