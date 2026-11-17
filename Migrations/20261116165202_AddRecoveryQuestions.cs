using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicEyeGD.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecoveryQuestions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    QuestionKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AnswerSalt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AnswerHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryQuestions", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_RecoveryQuestions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryQuestions");
        }
    }
}
