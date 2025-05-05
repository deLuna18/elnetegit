using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SubdivisionManagement.Migrations
{
    /// <inheritdoc />
    public partial class BillingReceips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptPath",
                table: "Billings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptPath",
                table: "Billings");
        }
    }
}
