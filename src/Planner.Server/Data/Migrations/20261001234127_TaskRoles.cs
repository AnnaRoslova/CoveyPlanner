using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Несколько ролей у дела: таблица TaskRoles (TaskId, RoleId) вместо колонки Tasks.RoleId; роли переносятся.
    /// </summary>
    public partial class TaskRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Сначала новая таблица и перенос ролей, потом удаление старой колонки — роли дел не теряются
            migrationBuilder.CreateTable(
                name: "TaskRoles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRoles", x => new { x.TaskId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_TaskRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskRoles_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRoles_RoleId",
                table: "TaskRoles",
                column: "RoleId");

            migrationBuilder.Sql("INSERT INTO TaskRoles (TaskId, RoleId) SELECT Id, RoleId FROM Tasks WHERE RoleId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Roles_RoleId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RoleId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "Tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RoleId",
                table: "Tasks",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Roles_RoleId",
                table: "Tasks",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // У дела в старой схеме одна роль — берём первую
            migrationBuilder.Sql("UPDATE Tasks SET RoleId = (SELECT MIN(RoleId) FROM TaskRoles WHERE TaskRoles.TaskId = Tasks.Id);");

            migrationBuilder.DropTable(
                name: "TaskRoles");
        }
    }
}
