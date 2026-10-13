using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledNotificationsAndWebPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TrainingRemindersEnabled",
                table: "UserSettings",
                newName: "StrengthNotificationsEnabled");

            migrationBuilder.RenameColumn(
                name: "AdminRequestNotificationsEnabled",
                table: "UserSettings",
                newName: "RunningNotificationsEnabled");

            // Both training categories inherit the old training preference; the retired admin
            // preference does not become a running opt-in. Existing notification history is retained.
            migrationBuilder.Sql("UPDATE UserSettings SET RunningNotificationsEnabled = StrengthNotificationsEnabled;");

            migrationBuilder.AddColumn<bool>(
                name: "DetailedPushPreviews",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MealRemindersEnabled",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NutritionProgressEnabled",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "UserSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "Europe/Copenhagen");

            migrationBuilder.AddColumn<long>(
                name: "FoodSequence",
                table: "InAppNotifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LocalDate",
                table: "InAppNotifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreviousFoodSequence",
                table: "InAppNotifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ScheduleSlot",
                table: "InAppNotifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledAtUtc",
                table: "InAppNotifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationOccurrences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOccurrences", x => new { x.UserId, x.Date, x.Slot });
                    table.ForeignKey(
                        name: "FK_NotificationOccurrences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutritionLogAdditions",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionLogAdditions", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_NutritionLogAdditions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Auth = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Binding = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InstallationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebPushSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebPushSubscriptions_UserSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "UserSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Finished = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => new { x.NotificationId, x.SubscriptionId });
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_InAppNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "InAppNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_WebPushSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "WebPushSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Finished_NextAttemptAtUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Finished", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_SubscriptionId",
                table: "NotificationDeliveries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_NutritionLogAdditions_EntryId",
                table: "NutritionLogAdditions",
                column: "EntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NutritionLogAdditions_UserId_Date_Sequence",
                table: "NutritionLogAdditions",
                columns: new[] { "UserId", "Date", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_Binding",
                table: "WebPushSubscriptions",
                column: "Binding",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_Endpoint",
                table: "WebPushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_SessionId",
                table: "WebPushSubscriptions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_UserId_InstallationId",
                table: "WebPushSubscriptions",
                columns: new[] { "UserId", "InstallationId" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO NutritionLogAdditions (EntryId, UserId, Date)
                SELECT entry.Id, meal.UserId, meal.Date
                FROM NutritionFoodEntries AS entry JOIN NutritionMeals AS meal ON meal.Id = entry.MealId;

                CREATE TRIGGER NutritionFoodEntry_TrackAddition AFTER INSERT ON NutritionFoodEntries
                BEGIN
                    INSERT INTO NutritionLogAdditions (EntryId, UserId, Date)
                    SELECT NEW.Id, meal.UserId, meal.Date FROM NutritionMeals AS meal WHERE meal.Id = NEW.MealId;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS NutritionFoodEntry_TrackAddition;");
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationOccurrences");

            migrationBuilder.DropTable(
                name: "NutritionLogAdditions");

            migrationBuilder.DropTable(
                name: "WebPushSubscriptions");

            migrationBuilder.DropColumn(
                name: "DetailedPushPreviews",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "MealRemindersEnabled",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "NutritionProgressEnabled",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "FoodSequence",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "LocalDate",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "PreviousFoodSequence",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "ScheduleSlot",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "ScheduledAtUtc",
                table: "InAppNotifications");

            migrationBuilder.RenameColumn(
                name: "StrengthNotificationsEnabled",
                table: "UserSettings",
                newName: "TrainingRemindersEnabled");

            migrationBuilder.RenameColumn(
                name: "RunningNotificationsEnabled",
                table: "UserSettings",
                newName: "AdminRequestNotificationsEnabled");
        }
    }
}
