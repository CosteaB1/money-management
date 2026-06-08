using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                opening_date = table.Column<DateOnly>(type: "date", nullable: false),
                is_archived = table.Column<bool>(type: "boolean", nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                balance_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                balance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_accounts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "budget_periods",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                month = table.Column<int>(type: "integer", nullable: false),
                spent_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                spent_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_budget_periods", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "budgets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                monthly_limit_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                monthly_limit_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_budgets", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "categories",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                flow = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                is_archived = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_categories", x => x.id);
                table.ForeignKey(
                    name: "fk_categories_categories_parent_id",
                    column: x => x.parent_id,
                    principalTable: "categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "fx_rates",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                to_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                as_of = table.Column<DateOnly>(type: "date", nullable: false),
                source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Manual"),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fx_rates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "import_batches",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                bank_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                imported_count = table.Column<int>(type: "integer", nullable: false),
                skipped_duplicate_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_import_batches", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "savings_goals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                target_date = table.Column<DateOnly>(type: "date", nullable: true),
                linked_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                manual_saved_amount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                manual_saved_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                target_amount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                target_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_savings_goals", x => x.id);
                table.ForeignKey(
                    name: "fk_savings_goals_accounts_linked_account_id",
                    column: x => x.linked_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "transactions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: true),
                transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                original_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                original_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                is_transfer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                counter_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                is_adjustment = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                amount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_transactions", x => x.id);
                table.ForeignKey(
                    name: "fk_transactions_accounts_counter_account_id",
                    column: x => x.counter_account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "category_patterns",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                keyword = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_category_patterns", x => x.id);
                table.ForeignKey(
                    name: "fk_category_patterns_categories_category_id",
                    column: x => x.category_id,
                    principalTable: "categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "savings_goal_contributions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                amount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_savings_goal_contributions", x => x.id);
                table.ForeignKey(
                    name: "fk_savings_goal_contributions_savings_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "savings_goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_accounts_name",
            table: "accounts",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "ix_budget_periods_budget_id_year_month",
            table: "budget_periods",
            columns: new[] { "budget_id", "year", "month" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_budgets_category_id_active",
            table: "budgets",
            column: "category_id",
            unique: true,
            filter: "is_archived = false");

        migrationBuilder.CreateIndex(
            name: "ix_categories_name",
            table: "categories",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "ix_categories_parent_id",
            table: "categories",
            column: "parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_category_patterns_category_id",
            table: "category_patterns",
            column: "category_id");

        migrationBuilder.CreateIndex(
            name: "ix_category_patterns_keyword",
            table: "category_patterns",
            column: "keyword",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_fx_rates_from_currency_to_currency_as_of_source",
            table: "fx_rates",
            columns: new[] { "from_currency", "to_currency", "as_of", "source" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_import_batches_account_id_file_hash",
            table: "import_batches",
            columns: new[] { "account_id", "file_hash" });

        migrationBuilder.CreateIndex(
            name: "ix_savings_goal_contributions_goal_id_occurred_on",
            table: "savings_goal_contributions",
            columns: new[] { "goal_id", "occurred_on" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_savings_goals_linked_account_id",
            table: "savings_goals",
            column: "linked_account_id");

        migrationBuilder.CreateIndex(
            name: "ix_transactions_account_id_transaction_date",
            table: "transactions",
            columns: new[] { "account_id", "transaction_date" });

        migrationBuilder.CreateIndex(
            name: "ix_transactions_counter_account_id",
            table: "transactions",
            column: "counter_account_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "budget_periods");

        migrationBuilder.DropTable(
            name: "budgets");

        migrationBuilder.DropTable(
            name: "category_patterns");

        migrationBuilder.DropTable(
            name: "fx_rates");

        migrationBuilder.DropTable(
            name: "import_batches");

        migrationBuilder.DropTable(
            name: "savings_goal_contributions");

        migrationBuilder.DropTable(
            name: "transactions");

        migrationBuilder.DropTable(
            name: "categories");

        migrationBuilder.DropTable(
            name: "savings_goals");

        migrationBuilder.DropTable(
            name: "accounts");
    }
}
