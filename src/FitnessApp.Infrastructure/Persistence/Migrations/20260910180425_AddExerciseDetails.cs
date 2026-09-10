using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExerciseDetails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Note",
            table: "ProgramExercises",
            type: "TEXT",
            maxLength: 250,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "Weight",
            table: "ProgramExercises",
            type: "TEXT",
            precision: 7,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.Sql("""
            UPDATE ProgramExercises
            SET Note = 'Opvarmning'
            WHERE IsWarmUp = 1;
            """);

        migrationBuilder.DropColumn(
            name: "IsWarmUp",
            table: "ProgramExercises");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsWarmUp",
            table: "ProgramExercises",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        // A note matching the forward migration's marker is restored as a warm-up on rollback.
        // A user-created identical note is intentionally treated the same because the legacy schema has no note field.
        migrationBuilder.Sql("""
            UPDATE ProgramExercises
            SET IsWarmUp = 1
            WHERE Note = 'Opvarmning';
            """);

        migrationBuilder.DropColumn(
            name: "Note",
            table: "ProgramExercises");

        migrationBuilder.DropColumn(
            name: "Weight",
            table: "ProgramExercises");
    }
}
