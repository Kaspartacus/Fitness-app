using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarOccurrenceMoves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarOccurrenceMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarOccurrenceMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarOccurrenceMoves_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOccurrenceMoves_UserId_Kind_OriginalDate",
                table: "CalendarOccurrenceMoves",
                columns: new[] { "UserId", "Kind", "OriginalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOccurrenceMoves_UserId_Kind_ScopeId_SourceId_OriginalDate",
                table: "CalendarOccurrenceMoves",
                columns: new[] { "UserId", "Kind", "ScopeId", "SourceId", "OriginalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOccurrenceMoves_UserId_Kind_TargetDate",
                table: "CalendarOccurrenceMoves",
                columns: new[] { "UserId", "Kind", "TargetDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarOccurrenceMoves");
        }
    }
}
