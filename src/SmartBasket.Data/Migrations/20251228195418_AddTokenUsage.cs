using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SmartBasket.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitOfMeasure",
                table: "Items");

            migrationBuilder.AddColumn<decimal>(
                name: "BaseUnitQuantity",
                table: "ReceiptItems",
                type: "TEXT",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUnitId",
                table: "ReceiptItems",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "шт");

            migrationBuilder.AddColumn<string>(
                name: "BaseUnitId",
                table: "Products",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "шт");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitQuantity",
                table: "Items",
                type: "TEXT",
                precision: 10,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseUnitQuantity",
                table: "Items",
                type: "TEXT",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "UnitId",
                table: "Items",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "шт");

            migrationBuilder.CreateTable(
                name: "TokenUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AiFunction = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    PrecachedPromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    ReasoningTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitOfMeasures",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BaseUnitId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Coefficient = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    IsBase = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOfMeasures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitOfMeasures_UnitOfMeasures_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "UnitOfMeasures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "UnitOfMeasures",
                columns: new[] { "Id", "BaseUnitId", "Coefficient", "IsBase", "Name" },
                values: new object[,]
                {
                    { "кг", "кг", 1m, true, "килограмм" },
                    { "л", "л", 1m, true, "литр" },
                    { "м", "м", 1m, true, "метр" },
                    { "м²", "м²", 1m, true, "кв. метр" },
                    { "шт", "шт", 1m, true, "штука" },
                    { "г", "кг", 0.001m, false, "грамм" },
                    { "мл", "л", 0.001m, false, "миллилитр" },
                    { "мм", "м", 0.001m, false, "миллиметр" },
                    { "см", "м", 0.01m, false, "сантиметр" },
                    { "см²", "м²", 0.0001m, false, "кв. сантиметр" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptItems_QuantityUnitId",
                table: "ReceiptItems",
                column: "QuantityUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_BaseUnitId",
                table: "Products",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_UnitId",
                table: "Items",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_AiFunction",
                table: "TokenUsages",
                column: "AiFunction");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_DateTime",
                table: "TokenUsages",
                column: "DateTime");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_Provider",
                table: "TokenUsages",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_SessionId",
                table: "TokenUsages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitOfMeasures_BaseUnitId",
                table: "UnitOfMeasures",
                column: "BaseUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_UnitOfMeasures_UnitId",
                table: "Items",
                column: "UnitId",
                principalTable: "UnitOfMeasures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_UnitOfMeasures_BaseUnitId",
                table: "Products",
                column: "BaseUnitId",
                principalTable: "UnitOfMeasures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptItems_UnitOfMeasures_QuantityUnitId",
                table: "ReceiptItems",
                column: "QuantityUnitId",
                principalTable: "UnitOfMeasures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_UnitOfMeasures_UnitId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_UnitOfMeasures_BaseUnitId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptItems_UnitOfMeasures_QuantityUnitId",
                table: "ReceiptItems");

            migrationBuilder.DropTable(
                name: "TokenUsages");

            migrationBuilder.DropTable(
                name: "UnitOfMeasures");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptItems_QuantityUnitId",
                table: "ReceiptItems");

            migrationBuilder.DropIndex(
                name: "IX_Products_BaseUnitId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Items_UnitId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "BaseUnitQuantity",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "QuantityUnitId",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "BaseUnitId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BaseUnitQuantity",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "Items");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitQuantity",
                table: "Items",
                type: "TEXT",
                precision: 10,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 3);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasure",
                table: "Items",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }
    }
}
