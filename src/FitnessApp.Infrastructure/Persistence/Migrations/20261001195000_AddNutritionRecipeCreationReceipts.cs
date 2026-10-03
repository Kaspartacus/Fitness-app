using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261001195000_AddNutritionRecipeCreationReceipts")]
public partial class AddNutritionRecipeCreationReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NutritionRecipeCreations",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Slot = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Portions = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NutritionRecipeCreations", x => new { x.UserId, x.RequestId });
                table.ForeignKey(
                    name: "FK_NutritionRecipeCreations_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            INSERT INTO "NutritionRecipeCreations" ("UserId", "RequestId", "Date", "Slot", "Name", "Portions")
            SELECT "UserId", "Id", "CreatedFromDate", "CreatedFromSlot", "Name", "Portions"
            FROM "NutritionRecipes";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NutritionRecipeCreations");
    }
}
