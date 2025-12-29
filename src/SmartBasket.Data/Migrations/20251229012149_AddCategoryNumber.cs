using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBasket.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "ProductCategories",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Number",
                table: "ProductCategories");
        }
    }
}
