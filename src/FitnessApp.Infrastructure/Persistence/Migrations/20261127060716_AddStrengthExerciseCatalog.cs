using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrengthExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogExerciseId",
                table: "ProgramExercises",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "ProgramExercises",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogExerciseId",
                table: "CompletedWorkoutExercises",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "CompletedWorkoutExercises",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StrengthCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SearchAliases = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    ImageAlt = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    IsTimed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrengthCatalog", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "StrengthCatalog",
                columns: new[] { "Id", "Category", "ImageAlt", "IsTimed", "Name", "SearchAliases" },
                values: new object[,]
                {
                    { "bench-press", "chest", "En person ligger på en bænk med fødderne i gulvet og holder en vægtstang over brystet", false, "Bench Press", "bænkpres|bænkpress|brystpres" },
                    { "biceps-curl", "arms", "En person bøjer albuerne og løfter håndvægtene mod skuldrene", false, "Biceps Curl", "bicepsbøjning|bicep curl|armcurl" },
                    { "calf-raise", "legs", "En stående person løfter hælene og støtter på forfødderne", false, "Calf Raise", "lægpres|tåhævning|taahaevning|lægøvelse" },
                    { "deadlift", "full-body", "En stående person holder en vægtstang med begge hænder foran lårene", false, "Deadlift", "dødløft|doedloeft|dødløft med stang|dødløft fra gulv" },
                    { "dumbbell-row", "back", "En person støtter på en bænk og trækker en håndvægt op ved siden af kroppen", false, "Dumbbell Row", "håndvægtsroning|enarmsroning|roning med håndvægt" },
                    { "lunge", "legs", "En person har én fod foran den anden og bøjer begge knæ", false, "Lunge", "udfald|udfaldsskridt|lunges" },
                    { "plank", "core", "En person holder kroppen lige med støtte på underarme og tæer", true, "Plank", "planke|underarmsplanke|front plank" },
                    { "pull-up", "back", "En person trækker sig op i en stang med begge hænder", false, "Pull-Up", "kropshævning|kropshævninger|pull up|pullups" },
                    { "push-up", "chest", "En person holder kroppen løftet med strakte arme og støtte på tæerne", false, "Push-Up", "armbøjning|armbøjninger|push up|pushups" },
                    { "side-plank", "core", "En person holder kroppen lige på siden med støtte på underarm og fødder", true, "Side Plank", "sideplanke|side planke|sidebro" },
                    { "squat", "legs", "En person sænker sig i en squat med bøjede knæ og ret ryg", false, "Squat", "knæbøjning|knæbøjninger|squats" },
                    { "triceps-extension", "arms", "En person strækker albuerne og løfter en håndvægt over hovedet", false, "Triceps Extension", "tricepsstræk|tricepspres|triceps extension med håndvægt" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrengthCatalog");

            migrationBuilder.DropColumn(
                name: "CatalogExerciseId",
                table: "ProgramExercises");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "ProgramExercises");

            migrationBuilder.DropColumn(
                name: "CatalogExerciseId",
                table: "CompletedWorkoutExercises");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "CompletedWorkoutExercises");
        }
    }
}
