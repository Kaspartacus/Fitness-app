using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20260927165000_DetachNutritionRecipeAdditionReceipts")]
public partial class DetachNutritionRecipeAdditionReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "NutritionRecipeAdditions_Detached" (
                "UserId" TEXT NOT NULL,
                "RequestId" TEXT NOT NULL,
                "RecipeId" TEXT NOT NULL,
                "Date" TEXT NOT NULL,
                "Slot" INTEGER NOT NULL,
                "Portions" TEXT NOT NULL,
                CONSTRAINT "PK_NutritionRecipeAdditions" PRIMARY KEY ("UserId", "RequestId"),
                CONSTRAINT "FK_NutritionRecipeAdditions_AspNetUsers_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "NutritionRecipeAdditions_Detached" ("UserId", "RequestId", "RecipeId", "Date", "Slot", "Portions")
            SELECT "UserId", "RequestId", "RecipeId", "Date", "Slot", "Portions"
            FROM "NutritionRecipeAdditions";

            DROP TABLE "NutritionRecipeAdditions";
            ALTER TABLE "NutritionRecipeAdditions_Detached" RENAME TO "NutritionRecipeAdditions";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "NutritionRecipeAdditions_Restored" (
                "UserId" TEXT NOT NULL,
                "RequestId" TEXT NOT NULL,
                "RecipeId" TEXT NOT NULL,
                "Date" TEXT NOT NULL,
                "Slot" INTEGER NOT NULL,
                "Portions" TEXT NOT NULL,
                CONSTRAINT "PK_NutritionRecipeAdditions" PRIMARY KEY ("UserId", "RequestId"),
                CONSTRAINT "FK_NutritionRecipeAdditions_AspNetUsers_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_NutritionRecipeAdditions_NutritionRecipes_RecipeId"
                    FOREIGN KEY ("RecipeId") REFERENCES "NutritionRecipes" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "NutritionRecipeAdditions_Restored" ("UserId", "RequestId", "RecipeId", "Date", "Slot", "Portions")
            SELECT additions."UserId", additions."RequestId", additions."RecipeId", additions."Date", additions."Slot", additions."Portions"
            FROM "NutritionRecipeAdditions" AS additions
            INNER JOIN "NutritionRecipes" AS recipes
                ON recipes."Id" = additions."RecipeId"
                AND recipes."UserId" = additions."UserId";

            DROP TABLE "NutritionRecipeAdditions";
            ALTER TABLE "NutritionRecipeAdditions_Restored" RENAME TO "NutritionRecipeAdditions";
            CREATE INDEX "IX_NutritionRecipeAdditions_RecipeId" ON "NutritionRecipeAdditions" ("RecipeId");
            """);
    }
}
