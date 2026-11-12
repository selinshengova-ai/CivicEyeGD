using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicEyeGD.Migrations
{
    /// <inheritdoc />
    public partial class AddReportQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Reports_Category_Latitude_Longitude",
                table: "Reports",
                columns: new[] { "Category", "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_CreatedAt",
                table: "Reports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status",
                table: "Reports",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_Category_Latitude_Longitude",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_CreatedAt",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_Status",
                table: "Reports");
        }
    }
}
