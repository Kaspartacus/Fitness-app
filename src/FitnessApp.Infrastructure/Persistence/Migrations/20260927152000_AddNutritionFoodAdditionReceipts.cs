using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20260927152000_AddNutritionFoodAdditionReceipts")]
public partial class AddNutritionFoodAdditionReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NutritionFoodAdditions",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Slot = table.Column<int>(type: "INTEGER", nullable: false),
                FoodId = table.Column<int>(type: "INTEGER", nullable: true),
                CustomFoodId = table.Column<Guid>(type: "TEXT", nullable: true),
                Grams = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NutritionFoodAdditions", x => new { x.UserId, x.RequestId });
                table.ForeignKey(
                    name: "FK_NutritionFoodAdditions_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            INSERT INTO NutritionFoodAdditions (UserId, RequestId, Date, Slot, FoodId, CustomFoodId, Grams)
            SELECT meals.UserId, entries.Id, meals.Date, meals.Slot, entries.FoodId, entries.CustomFoodId, entries.Grams
            FROM NutritionFoodEntries AS entries
            INNER JOIN NutritionMeals AS meals ON meals.Id = entries.MealId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NutritionFoodAdditions");
    }
}
