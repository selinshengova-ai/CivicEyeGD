using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicEyeGD.Migrations
{
    /// <inheritdoc />
    public partial class StatusHistoryAuthorSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatusHistories_AspNetUsers_ChangedByUserId",
                table: "StatusHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_StatusHistories_AspNetUsers_ChangedByUserId",
                table: "StatusHistories",
                column: "ChangedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatusHistories_AspNetUsers_ChangedByUserId",
                table: "StatusHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_StatusHistories_AspNetUsers_ChangedByUserId",
                table: "StatusHistories",
                column: "ChangedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
