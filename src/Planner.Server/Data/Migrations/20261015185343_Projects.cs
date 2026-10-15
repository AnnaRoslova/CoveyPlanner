using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Projects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProject",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Проект (N6.17): каждое дело, у которого есть хотя бы один шаг (в любом состоянии, включая корзину), —
            // теперь явно проект: уходит из ленты в «🛠 Проекты» с той же категорией. Проект цели — тоже (у цели он выбран явно)
            migrationBuilder.Sql("""
                UPDATE Tasks SET IsProject = 1
                WHERE ParentId IS NULL AND (
                    EXISTS (SELECT 1 FROM Tasks s WHERE s.ParentId = Tasks.Id)
                    OR Id IN (SELECT ProjectId FROM WeeklyGoals WHERE ProjectId IS NOT NULL)
                    OR Id IN (SELECT ProjectId FROM GoalSeries WHERE ProjectId IS NOT NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProject",
                table: "Tasks");
        }
    }
}
