using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace FitnessApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitnessDbContext))]
[Migration("20261021120000_AddPrivateDiary")]
public sealed partial class AddPrivateDiary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "DiaryRemindersEnabled", table: "UserSettings", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.CreateTable(name: "DiaryEntries", columns: table => new
        {
            UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
            Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
            Ciphertext = table.Column<string>(type: "TEXT", nullable: false),
            Version = table.Column<Guid>(type: "TEXT", nullable: false),
            ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DiaryEntries", x => new { x.UserId, x.Date });
            table.ForeignKey("FK_DiaryEntries_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex(name: "IX_DiaryEntries_ExpiresAtUtc", table: "DiaryEntries", column: "ExpiresAtUtc");
        migrationBuilder.CreateTable(name: "DiaryProtections", columns: table => new
        {
            UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
            CodeHash = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
            FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
            LockedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            Revision = table.Column<Guid>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DiaryProtections", x => x.UserId);
            table.ForeignKey("FK_DiaryProtections_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "DiaryGrants", columns: table => new
        {
            Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
            SessionId = table.Column<string>(type: "TEXT", nullable: false),
            Revision = table.Column<Guid>(type: "TEXT", nullable: false),
            LastActivityUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DiaryGrants", x => x.Hash);
            table.ForeignKey("FK_DiaryGrants_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_DiaryGrants_UserSessions_SessionId", x => x.SessionId, "UserSessions", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex(name: "IX_DiaryGrants_SessionId", table: "DiaryGrants", column: "SessionId");
        migrationBuilder.CreateIndex(name: "IX_DiaryGrants_UserId_SessionId", table: "DiaryGrants", columns: new[] { "UserId", "SessionId" });
        migrationBuilder.CreateTable(name: "DiaryMaintenance", columns: table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            CompletedOnUtc = table.Column<DateOnly>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_DiaryMaintenance", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DiaryGrants");
        migrationBuilder.DropTable("DiaryProtections");
        migrationBuilder.DropTable("DiaryEntries");
        migrationBuilder.DropTable("DiaryMaintenance");
        migrationBuilder.DropColumn("DiaryRemindersEnabled", "UserSettings");
    }
}
