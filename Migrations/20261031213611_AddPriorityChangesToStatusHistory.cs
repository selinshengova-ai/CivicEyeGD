using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicEyeGD.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorityChangesToStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewPriorityAdjustment",
                table: "StatusHistories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OldPriorityAdjustment",
                table: "StatusHistories",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewPriorityAdjustment",
                table: "StatusHistories");

            migrationBuilder.DropColumn(
                name: "OldPriorityAdjustment",
                table: "StatusHistories");
        }
    }
}
