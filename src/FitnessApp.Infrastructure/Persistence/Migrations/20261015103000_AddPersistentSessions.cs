using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations;

public partial class AddPersistentSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>("LastActivityAt", "UserSessions", type: "TEXT", nullable: false,
            defaultValue: DateTimeOffset.MinValue);
        migrationBuilder.AddColumn<string>("RefreshTokenHash", "UserSessions", type: "TEXT", maxLength: 64, nullable: true);
        // Existing access-only sessions retain their old expiry and cannot be restored by cookie.
        migrationBuilder.Sql("UPDATE UserSessions SET LastActivityAt = CreatedAt;");
        migrationBuilder.CreateIndex("IX_UserSessions_RefreshTokenHash", "UserSessions", "RefreshTokenHash", unique: true);
        migrationBuilder.CreateTable("ConsumedRefreshTokens", columns: table => new
        {
            Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            SessionId = table.Column<string>(type: "TEXT", nullable: false),
            ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ConsumedRefreshTokens", x => x.Hash);
            table.ForeignKey("FK_ConsumedRefreshTokens_UserSessions_SessionId", x => x.SessionId,
                "UserSessions", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_ConsumedRefreshTokens_SessionId", "ConsumedRefreshTokens", "SessionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ConsumedRefreshTokens");
        migrationBuilder.DropIndex("IX_UserSessions_RefreshTokenHash", "UserSessions");
        migrationBuilder.DropColumn("LastActivityAt", "UserSessions");
        migrationBuilder.DropColumn("RefreshTokenHash", "UserSessions");
    }
}
