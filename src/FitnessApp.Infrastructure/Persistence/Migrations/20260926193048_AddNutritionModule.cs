using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNutritionModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FridaCatalogueReleases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Checksum = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FoodCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FridaCatalogueReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FridaFoods",
                columns: table => new
                {
                    FoodId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DanishName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FoodGroup = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SearchName = table.Column<string>(type: "TEXT", maxLength: 700, nullable: false),
                    EnergyKcalPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProteinPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    CarbohydratePer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    FatPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    SugarPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    PublishedNutrientsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FridaFoods", x => x.FoodId);
                });

            migrationBuilder.CreateTable(
                name: "NutritionMeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionMeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutritionMeals_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutritionRecipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Portions = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutritionRecipes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutritionTargets",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    EnergyKcal = table.Column<decimal>(type: "TEXT", nullable: true),
                    Protein = table.Column<decimal>(type: "TEXT", nullable: true),
                    Carbohydrate = table.Column<decimal>(type: "TEXT", nullable: true),
                    Fat = table.Column<decimal>(type: "TEXT", nullable: true),
                    Sugar = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionTargets", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_NutritionTargets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutritionFoodEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FoodGroup = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CatalogueVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Grams = table.Column<decimal>(type: "TEXT", nullable: false),
                    EnergyKcalPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProteinPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    CarbohydratePer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    FatPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    SugarPer100g = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionFoodEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutritionFoodEntries_NutritionMeals_MealId",
                        column: x => x.MealId,
                        principalTable: "NutritionMeals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutritionRecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FoodGroup = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CatalogueVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Grams = table.Column<decimal>(type: "TEXT", nullable: false),
                    EnergyKcalPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProteinPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    CarbohydratePer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    FatPer100g = table.Column<decimal>(type: "TEXT", nullable: true),
                    SugarPer100g = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionRecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutritionRecipeIngredients_NutritionRecipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "NutritionRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FridaFoods_SearchName",
                table: "FridaFoods",
                column: "SearchName");

            migrationBuilder.CreateIndex(
                name: "IX_NutritionFoodEntries_MealId",
                table: "NutritionFoodEntries",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_NutritionMeals_UserId_Date_Slot",
                table: "NutritionMeals",
                columns: new[] { "UserId", "Date", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NutritionRecipeIngredients_RecipeId",
                table: "NutritionRecipeIngredients",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_NutritionRecipes_UserId_Name",
                table: "NutritionRecipes",
                columns: new[] { "UserId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FridaCatalogueReleases");

            migrationBuilder.DropTable(
                name: "FridaFoods");

            migrationBuilder.DropTable(
                name: "NutritionFoodEntries");

            migrationBuilder.DropTable(
                name: "NutritionRecipeIngredients");

            migrationBuilder.DropTable(
                name: "NutritionTargets");

            migrationBuilder.DropTable(
                name: "NutritionMeals");

            migrationBuilder.DropTable(
                name: "NutritionRecipes");
        }
    }
}
