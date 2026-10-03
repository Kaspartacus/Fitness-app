using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNutritionFoods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomFoodId",
                table: "NutritionRecipeIngredients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomFoodId",
                table: "NutritionFoodEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NutritionCustomFoods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SearchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EnergyKcalPer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProteinPer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    CarbohydratePer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    FatPer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    SugarPer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionCustomFoods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutritionCustomFoods_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NutritionCustomFoods_UserId_SearchName",
                table: "NutritionCustomFoods",
                columns: new[] { "UserId", "SearchName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NutritionCustomFoods");

            migrationBuilder.DropColumn(
                name: "CustomFoodId",
                table: "NutritionRecipeIngredients");

            migrationBuilder.DropColumn(
                name: "CustomFoodId",
                table: "NutritionFoodEntries");
        }
    }
}
