using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FairShareMonApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBankCallbackSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "qr_correlation_codes",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    uuid = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    event_id = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    member_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    expense_id = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expected_amount_snapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "current_timestamp(6) ON UPDATE current_timestamp(6)")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_correlation_codes", x => x.id);
                    table.CheckConstraint("ck_qr_correlation_codes_amount_non_negative", "expected_amount_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_qr_correlation_codes_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_qr_correlation_codes_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_qr_correlation_codes_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_qr_correlation_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "bank_transaction_callbacks",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    uuid = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_key = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_transaction_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_incoming = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    bank_bin = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_account_number = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    extracted_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transaction_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    raw_payload = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    matched_correlation_code_id = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    resolved_user_id = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    outcome = table.Column<int>(type: "int", nullable: false),
                    failure_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    applied_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "current_timestamp(6) ON UPDATE current_timestamp(6)")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transaction_callbacks", x => x.id);
                    table.CheckConstraint("ck_bank_transaction_callbacks_amount_non_negative", "amount >= 0");
                    table.ForeignKey(
                        name: "FK_bank_transaction_callbacks_qr_correlation_codes_matched_corr~",
                        column: x => x.matched_correlation_code_id,
                        principalTable: "qr_correlation_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_bank_transaction_callbacks_users_resolved_user_id",
                        column: x => x.resolved_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_callbacks_extracted_code",
                table: "bank_transaction_callbacks",
                column: "extracted_code");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_callbacks_matched_correlation_code_id",
                table: "bank_transaction_callbacks",
                column: "matched_correlation_code_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_callbacks_resolved_user_id",
                table: "bank_transaction_callbacks",
                column: "resolved_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_callbacks_uuid",
                table: "bank_transaction_callbacks",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_bank_transaction_callbacks_provider_tx",
                table: "bank_transaction_callbacks",
                columns: new[] { "provider_key", "provider_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_qr_correlation_codes_code",
                table: "qr_correlation_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_qr_correlation_codes_event_id",
                table: "qr_correlation_codes",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_qr_correlation_codes_expense_id",
                table: "qr_correlation_codes",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "IX_qr_correlation_codes_member_id",
                table: "qr_correlation_codes",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_qr_correlation_codes_user_id",
                table: "qr_correlation_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_qr_correlation_codes_uuid",
                table: "qr_correlation_codes",
                column: "uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transaction_callbacks");

            migrationBuilder.DropTable(
                name: "qr_correlation_codes");
        }
    }
}
