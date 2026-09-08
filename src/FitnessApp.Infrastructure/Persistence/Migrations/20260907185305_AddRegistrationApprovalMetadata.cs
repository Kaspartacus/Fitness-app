using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationApprovalMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedByUserId",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ApprovalStatus_RegisteredAt",
                table: "AspNetUsers",
                columns: new[] { "ApprovalStatus", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ApprovalStatus_RegisteredAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "AspNetUsers");
        }
    }
}
