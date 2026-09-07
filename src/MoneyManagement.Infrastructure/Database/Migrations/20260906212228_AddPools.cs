using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class AddPools : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pools",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                inception_date = table.Column<DateOnly>(type: "date", nullable: false),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pools", x => x.id);
                table.ForeignKey(
                    name: "fk_pools_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "pool_participants",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                is_owner = table.Column<bool>(type: "boolean", nullable: false),
                joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pool_participants", x => x.id);
                table.ForeignKey(
                    name: "fk_pool_participants_pools_pool_id",
                    column: x => x.pool_id,
                    principalTable: "pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "pool_unit_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pool_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                units = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                nav_per_unit = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                pool_value_pre_money = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                cash_value = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                cash_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                settled_on = table.Column<DateOnly>(type: "date", nullable: true),
                movement_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pool_unit_events", x => x.id);
                table.ForeignKey(
                    name: "fk_pool_unit_events_pool_participants_participant_id",
                    column: x => x.participant_id,
                    principalTable: "pool_participants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_pool_unit_events_pools_pool_id",
                    column: x => x.pool_id,
                    principalTable: "pools",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_pool_unit_events_transactions_movement_transaction_id",
                    column: x => x.movement_transaction_id,
                    principalTable: "transactions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_pool_participants_pool_id_is_archived",
            table: "pool_participants",
            columns: new[] { "pool_id", "is_archived" });

        migrationBuilder.CreateIndex(
            name: "ux_pool_participants_pool_id_owner",
            table: "pool_participants",
            column: "pool_id",
            unique: true,
            filter: "\"is_owner\"");

        migrationBuilder.CreateIndex(
            name: "ix_pool_unit_events_movement_transaction_id",
            table: "pool_unit_events",
            column: "movement_transaction_id");

        migrationBuilder.CreateIndex(
            name: "ix_pool_unit_events_participant_id",
            table: "pool_unit_events",
            column: "participant_id");

        migrationBuilder.CreateIndex(
            name: "ix_pool_unit_events_pool_id_occurred_on",
            table: "pool_unit_events",
            columns: new[] { "pool_id", "occurred_on" });

        migrationBuilder.CreateIndex(
            name: "ix_pools_account_id",
            table: "pools",
            column: "account_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "pool_unit_events");

        migrationBuilder.DropTable(
            name: "pool_participants");

        migrationBuilder.DropTable(
            name: "pools");
    }
}
