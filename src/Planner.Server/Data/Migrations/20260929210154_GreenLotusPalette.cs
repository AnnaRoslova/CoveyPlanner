using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Палитра ролей «Лотос» стала зеленее, розовые — холодными «зефирными».
    /// Схема не меняется; цвета ролей переводятся по смыслу, так что стартовые роли
    /// получают новые стартовые цвета (Тело, Общение, Семья, Дом), а общие цвета остаются.
    /// </summary>
    public partial class GreenLotusPalette : Migration
    {
        // Прежний цвет → новый. Бирюзовые #6fbfae #94b4b5 #a8dcd0 #8ac2c4 #b9d3b0 есть в обеих палитрах.
        private static readonly (string From, string To)[] Recolor =
        [
            ("#f29ea6", "#efb8cc"), // Общение: коралловый → зефирный розовый
            ("#f4c3cf", "#9fd3bd"), // Тело: румянец → мятно-зелёный
            ("#f7d3db", "#7fc3b4"), // Дом: бледно-розовый → нефритовый
            ("#e9b0b8", "#b9d3b0"), // Семья: пыльная роза → шалфей
            ("#f5b7a5", "#f5cddd"), // персиковый → бледный зефир
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (from, to) in Recolor)
            {
                migrationBuilder.Sql($"UPDATE Roles SET Color = '{to}' WHERE Color = '{from}';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Шалфей #b9d3b0 был и в старой палитре, поэтому «Семья» обратно не перекрашивается
            foreach (var (from, to) in Recolor.Where(r => r.To != "#b9d3b0"))
            {
                migrationBuilder.Sql($"UPDATE Roles SET Color = '{from}' WHERE Color = '{to}';");
            }
        }
    }
}
