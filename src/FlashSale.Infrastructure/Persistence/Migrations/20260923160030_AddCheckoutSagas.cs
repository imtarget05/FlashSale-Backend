using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutSagas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutSagas",
                columns: table => new
                {
                    SagaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InventoryStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CompensationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutSagas", x => x.SagaId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagas_IdempotencyKey",
                table: "CheckoutSagas",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSagas_OrderId",
                table: "CheckoutSagas",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutSagas");
        }
    }
}
