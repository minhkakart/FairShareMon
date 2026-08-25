using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FairShareMonApi.Migrations
{
    /// <inheritdoc />
    public partial class AddEventMemberSettlementClearedAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cleared_amount",
                table: "event_member_settlements",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_member_settlements_cleared_amount_non_negative",
                table: "event_member_settlements",
                sql: "cleared_amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_event_member_settlements_cleared_amount_non_negative",
                table: "event_member_settlements");

            migrationBuilder.DropColumn(
                name: "cleared_amount",
                table: "event_member_settlements");
        }
    }
}
