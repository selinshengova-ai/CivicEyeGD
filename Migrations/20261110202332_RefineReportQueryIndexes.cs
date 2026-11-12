using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicEyeGD.Migrations
{
    /// <inheritdoc />
    public partial class RefineReportQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_Category_Latitude_Longitude",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_Status",
                table: "Reports");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Latitude_Longitude",
                table: "Reports",
                columns: new[] { "Latitude", "Longitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_Latitude_Longitude",
                table: "Reports");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Category_Latitude_Longitude",
                table: "Reports",
                columns: new[] { "Category", "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status",
                table: "Reports",
                column: "Status");
        }
    }
}
