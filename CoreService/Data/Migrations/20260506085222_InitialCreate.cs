using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlacklistedUsers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PageId = table.Column<string>(type: "text", nullable: false),
                    SpamCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSpamAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSpamAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistedUsers", x => new { x.UserId, x.PageId });
                });

            migrationBuilder.CreateTable(
                name: "EventStates",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Intent = table.Column<string>(type: "text", nullable: true),
                    Sentiment = table.Column<string>(type: "text", nullable: true),
                    IsSpam = table.Column<bool>(type: "boolean", nullable: false),
                    SpamSeverity = table.Column<int>(type: "integer", nullable: false),
                    ActionTaken = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    PageId = table.Column<string>(type: "text", nullable: true),
                    CommentId = table.Column<string>(type: "text", nullable: true),
                    DecisionReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventStates", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "ReviewQueueItems",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "text", nullable: false),
                    CommentId = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsReviewed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewQueueItems", x => x.EventId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlacklistedUsers");

            migrationBuilder.DropTable(
                name: "EventStates");

            migrationBuilder.DropTable(
                name: "ReviewQueueItems");
        }
    }
}
