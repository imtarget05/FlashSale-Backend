using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRecoveryCommand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxRecoveryCommands",
                columns: table => new
                {
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IntentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpectedDeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxRecoveryCommands", x => x.IdempotencyKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxRecoveryCommands_MessageId",
                table: "OutboxRecoveryCommands",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxRecoveryCommands");
        }
    }
}
