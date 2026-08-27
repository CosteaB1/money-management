using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class AddLoans : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "loans",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                counterparty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                loan_date = table.Column<DateOnly>(type: "date", nullable: false),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                disbursement_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                principal_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                principal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_loans", x => x.id);
                table.ForeignKey(
                    name: "fk_loans_transactions_disbursement_transaction_id",
                    column: x => x.disbursement_transaction_id,
                    principalTable: "transactions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "loan_payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                amount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_loan_payments", x => x.id);
                table.ForeignKey(
                    name: "fk_loan_payments_loans_loan_id",
                    column: x => x.loan_id,
                    principalTable: "loans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_loan_payments_transactions_transaction_id",
                    column: x => x.transaction_id,
                    principalTable: "transactions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_loan_payments_loan_id_occurred_on",
            table: "loan_payments",
            columns: new[] { "loan_id", "occurred_on" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_loan_payments_transaction_id",
            table: "loan_payments",
            column: "transaction_id");

        migrationBuilder.CreateIndex(
            name: "ix_loans_disbursement_transaction_id",
            table: "loans",
            column: "disbursement_transaction_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "loan_payments");

        migrationBuilder.DropTable(
            name: "loans");
    }
}
