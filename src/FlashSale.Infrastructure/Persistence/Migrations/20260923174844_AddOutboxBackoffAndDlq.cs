using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxBackoffAndDlq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAt",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: rows already parked at the old hard cap (RetryCount >= 5,
            // never processed) were unreachable — the old dispatcher filtered
            // them out silently while /api/outbox/pending reported count=0.
            // Mark them dead-lettered so they become EXPLICITLY stuck and can be
            // revived through POST /api/outbox/requeue instead of SQL by hand.
            migrationBuilder.Sql("""
                UPDATE "OutboxMessages"
                SET    "DeadLetteredAt" = COALESCE("DeadLetteredAt", now())
                WHERE  "ProcessedAt" IS NULL
                  AND  "RetryCount" >= 5
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "OutboxMessages");
        }
    }
}
