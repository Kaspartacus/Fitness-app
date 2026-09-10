using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddStrengthTrainingFlow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite cannot drop the old ProgramExercises foreign key in place. Rebuilding the table keeps each
        // existing program and exercise, placing its former flat exercise list in a first workout.
        migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

        migrationBuilder.CreateTable(
            name: "CompletedWorkouts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkoutId = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkoutName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompletedWorkouts", x => x.Id);
                table.ForeignKey("FK_CompletedWorkouts_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProgramWorkouts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Position = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProgramWorkouts", x => x.Id);
                table.ForeignKey("FK_ProgramWorkouts_StrengthPrograms_ProgramId", x => x.ProgramId, "StrengthPrograms", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            INSERT INTO ProgramWorkouts (Id, ProgramId, Name, Position)
            SELECT Id, Id, 'Træning 1', 0
            FROM StrengthPrograms;
            """);

        migrationBuilder.CreateTable(
            name: "CompletedWorkoutExercises",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CompletedWorkoutId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProgramExerciseId = table.Column<Guid>(type: "TEXT", nullable: true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Weight = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                Sets = table.Column<int>(type: "INTEGER", nullable: false),
                Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                Position = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompletedWorkoutExercises", x => x.Id);
                table.ForeignKey("FK_CompletedWorkoutExercises_CompletedWorkouts_CompletedWorkoutId", x => x.CompletedWorkoutId, "CompletedWorkouts", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProgramScheduleEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                WorkoutId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProgramScheduleEntries", x => x.Id);
                table.ForeignKey("FK_ProgramScheduleEntries_ProgramWorkouts_WorkoutId", x => x.WorkoutId, "ProgramWorkouts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProgramScheduleEntries_StrengthPrograms_ProgramId", x => x.ProgramId, "StrengthPrograms", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            CREATE TABLE "__ProgramExercises" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProgramExercises" PRIMARY KEY,
                "WorkoutId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Weight" TEXT NOT NULL,
                "Sets" INTEGER NOT NULL,
                "Repetitions" INTEGER NOT NULL,
                "Note" TEXT NULL,
                "Position" INTEGER NOT NULL,
                CONSTRAINT "FK_ProgramExercises_ProgramWorkouts_WorkoutId"
                    FOREIGN KEY ("WorkoutId") REFERENCES "ProgramWorkouts" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "__ProgramExercises" ("Id", "WorkoutId", "Name", "Weight", "Sets", "Repetitions", "Note", "Position")
            SELECT "Id", "ProgramId", "Name", "Weight", "Sets", "Repetitions", "Note",
                   ROW_NUMBER() OVER (PARTITION BY "ProgramId" ORDER BY "Position", "Id") - 1
            FROM "ProgramExercises";

            DROP TABLE "ProgramExercises";
            ALTER TABLE "__ProgramExercises" RENAME TO "ProgramExercises";
            """);

        migrationBuilder.CreateIndex("IX_ProgramExercises_WorkoutId_Position", "ProgramExercises", new[] { "WorkoutId", "Position" }, unique: true);
        migrationBuilder.CreateIndex("IX_CompletedWorkoutExercises_CompletedWorkoutId_Position", "CompletedWorkoutExercises", new[] { "CompletedWorkoutId", "Position" }, unique: true);
        migrationBuilder.CreateIndex("IX_CompletedWorkouts_UserId_CompletedAt", "CompletedWorkouts", new[] { "UserId", "CompletedAt" });
        migrationBuilder.CreateIndex("IX_ProgramScheduleEntries_ProgramId_DayOfWeek", "ProgramScheduleEntries", new[] { "ProgramId", "DayOfWeek" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProgramScheduleEntries_WorkoutId", "ProgramScheduleEntries", "WorkoutId");
        migrationBuilder.CreateIndex("IX_ProgramWorkouts_ProgramId_Position", "ProgramWorkouts", new[] { "ProgramId", "Position" }, unique: true);
        migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
        migrationBuilder.Sql("""
            CREATE TABLE "__ProgramExercises" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProgramExercises" PRIMARY KEY,
                "ProgramId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Weight" TEXT NOT NULL,
                "Sets" INTEGER NOT NULL,
                "Repetitions" INTEGER NOT NULL,
                "Note" TEXT NULL,
                "Position" INTEGER NOT NULL,
                CONSTRAINT "FK_ProgramExercises_StrengthPrograms_ProgramId"
                    FOREIGN KEY ("ProgramId") REFERENCES "StrengthPrograms" ("Id") ON DELETE CASCADE
            );
            INSERT INTO "__ProgramExercises" ("Id", "ProgramId", "Name", "Weight", "Sets", "Repetitions", "Note", "Position")
            SELECT exercise."Id", workout."ProgramId", exercise."Name", exercise."Weight", exercise."Sets", exercise."Repetitions", exercise."Note", exercise."Position"
            FROM "ProgramExercises" exercise
            INNER JOIN "ProgramWorkouts" workout ON workout."Id" = exercise."WorkoutId";
            DROP TABLE "ProgramExercises";
            ALTER TABLE "__ProgramExercises" RENAME TO "ProgramExercises";
            """);
        migrationBuilder.DropTable("CompletedWorkoutExercises");
        migrationBuilder.DropTable("ProgramScheduleEntries");
        migrationBuilder.DropTable("CompletedWorkouts");
        migrationBuilder.DropTable("ProgramWorkouts");
        migrationBuilder.CreateIndex("IX_ProgramExercises_ProgramId_Position", "ProgramExercises", new[] { "ProgramId", "Position" });
        migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
    }
}
