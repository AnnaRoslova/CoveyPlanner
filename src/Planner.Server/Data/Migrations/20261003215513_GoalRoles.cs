using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class GoalRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalRoles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalRoles", x => new { x.GoalId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_GoalRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalRoles_WeeklyGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "WeeklyGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoalSeriesRoles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalSeriesRoles", x => new { x.SeriesId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_GoalSeriesRoles_GoalSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "GoalSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalSeriesRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_WeekStart",
                table: "WeeklyGoals",
                column: "WeekStart");

            migrationBuilder.CreateIndex(
                name: "IX_GoalRoles_RoleId",
                table: "GoalRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalSeriesRoles_RoleId",
                table: "GoalSeriesRoles",
                column: "RoleId");

            // Роли целей и серий — из прежних колонок; место цели в роли — прежний SortOrder
            migrationBuilder.Sql("INSERT INTO GoalRoles (GoalId, RoleId, SortOrder) SELECT Id, RoleId, SortOrder FROM WeeklyGoals;");
            migrationBuilder.Sql("INSERT INTO GoalSeriesRoles (SeriesId, RoleId) SELECT Id, RoleId FROM GoalSeries;");

            // У дела теперь одна цель — главная (Position = 0). Роли дела с несколькими целями — роль главной цели
            migrationBuilder.Sql("""
                DELETE FROM TaskRoles
                WHERE TaskId IN (SELECT TaskId FROM TaskGoals WHERE Position > 0)
                  AND RoleId NOT IN (SELECT g.RoleId FROM TaskGoals tg JOIN WeeklyGoals g ON g.Id = tg.GoalId
                                     WHERE tg.TaskId = TaskRoles.TaskId AND tg.Position = 0);
                """);
            migrationBuilder.Sql("DELETE FROM TaskGoals WHERE Position > 0;");

            migrationBuilder.DropForeignKey(
                name: "FK_GoalSeries_Roles_RoleId",
                table: "GoalSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_WeeklyGoals_Roles_RoleId",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_RoleId",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_WeekStart_RoleId_SortOrder",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_GoalSeries_RoleId",
                table: "GoalSeries");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "GoalSeries");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_WeekStart",
                table: "WeeklyGoals");

            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "GoalSeries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_RoleId",
                table: "WeeklyGoals",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_WeekStart_RoleId_SortOrder",
                table: "WeeklyGoals",
                columns: new[] { "WeekStart", "RoleId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalSeries_RoleId",
                table: "GoalSeries",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoalSeries_Roles_RoleId",
                table: "GoalSeries",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WeeklyGoals_Roles_RoleId",
                table: "WeeklyGoals",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                UPDATE WeeklyGoals SET
                    RoleId = (SELECT MIN(RoleId) FROM GoalRoles r WHERE r.GoalId = WeeklyGoals.Id),
                    SortOrder = COALESCE((SELECT r.SortOrder FROM GoalRoles r WHERE r.GoalId = WeeklyGoals.Id
                                          ORDER BY r.RoleId LIMIT 1), 0);
                """);
            migrationBuilder.Sql("UPDATE GoalSeries SET RoleId = (SELECT MIN(RoleId) FROM GoalSeriesRoles r WHERE r.SeriesId = GoalSeries.Id);");

            migrationBuilder.DropTable(
                name: "GoalRoles");

            migrationBuilder.DropTable(
                name: "GoalSeriesRoles");

        }
    }
}
