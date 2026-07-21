using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FairShareMonApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPerMemberSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_settled",
                table: "shares",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "settled_at",
                table: "shares",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_member_settlements",
                columns: table => new
                {
                    event_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    member_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    is_settled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    settled_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "current_timestamp(6) ON UPDATE current_timestamp(6)")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_member_settlements", x => new { x.event_id, x.member_id });
                    table.ForeignKey(
                        name: "FK_event_member_settlements_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_member_settlements_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_event_member_settlements_member_id",
                table: "event_member_settlements",
                column: "member_id");

            // Data backfill (settled-per-member OQ4a): every share of an already-settled expense becomes
            // settled with the expense's settled_at, so the D1 reconciliation invariant holds from day one.
            // No Layer B (event_member_settlements) backfill - net clearance is asserted going forward, not
            // fabricated from a past whole-expense flag (OQ4a). The Down path discards this by dropping the
            // columns (the data step is intentionally not reversed).
            migrationBuilder.Sql(
                """
                UPDATE shares s
                JOIN expenses e ON e.id = s.expense_id
                SET s.is_settled = 1, s.settled_at = e.settled_at
                WHERE e.is_settled = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_member_settlements");

            migrationBuilder.DropColumn(
                name: "is_settled",
                table: "shares");

            migrationBuilder.DropColumn(
                name: "settled_at",
                table: "shares");
        }
    }
}
