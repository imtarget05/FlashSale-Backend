using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductContentDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ShortDescription = table.Column<string>(type: "text", nullable: false),
                    SeoDescription = table.Column<string>(type: "text", nullable: false),
                    KeywordsJson = table.Column<string>(type: "text", nullable: false),
                    SocialCaption = table.Column<string>(type: "text", nullable: false),
                    FaqJson = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    PromptTokens = table.Column<long>(type: "bigint", nullable: false),
                    CompletionTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductContentDrafts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductContentDrafts_ProductId",
                table: "ProductContentDrafts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductContentDrafts_Status",
                table: "ProductContentDrafts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductContentDrafts");
        }
    }
}
