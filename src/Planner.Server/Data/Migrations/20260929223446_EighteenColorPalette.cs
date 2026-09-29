using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Палитра ролей расширена до 18 цветов (docs/role-palette.html). Схема не меняется.
    /// 1) Стартовые роли, у которых всё ещё прежний стартовый цвет, получают новый стартовый цвет —
    ///    часть прежних цветов осталась в палитре, поэтому перекраска только «по цвету» их бы не тронула.
    /// 2) Остальные роли с цветами, которых больше нет в палитре, — в ближайший новый оттенок.
    /// </summary>
    public partial class EighteenColorPalette : Migration
    {
        // Измерения пилы: SawDimension, прежний стартовый цвет → новый
        private static readonly (string Dimension, string From, string To)[] SawStart =
        [
            ("Body", "#9fd3bd", "#9fd3bd"),
            ("Mind", "#94b4b5", "#a9cde8"),
            ("Spirit", "#6fbfae", "#c9b8ec"),
            ("Social", "#efb8cc", "#f9d0e0"),
        ];

        // Стартовые пользовательские роли: название, прежний стартовый цвет → новый
        private static readonly (string Name, string From, string To)[] UserStart =
        [
            ("Я сама", "#a8dcd0", "#dcb8e6"),
            ("Семья", "#b9d3b0", "#b9d3b0"),
            ("Работа", "#8ac2c4", "#8fb4e3"),
            ("Дом", "#7fc3b4", "#f3e3a6"),
        ];

        // Цвета, которых больше нет в палитре → ближайшие новые
        private static readonly (string From, string To)[] Removed =
        [
            ("#a8dcd0", "#9fd3bd"), // светлая мята → мята
            ("#7fc3b4", "#6fbfae"), // нефритовый → бирюза
            ("#efb8cc", "#f2b3c6"), // зефирный розовый → роза
            ("#f5cddd", "#f9d0e0"), // бледный зефир → зефир
            ("#e8bfd3", "#e8c1dc"), // сиреневато-розовый → орхидея
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (dimension, from, to) in SawStart)
            {
                migrationBuilder.Sql(
                    $"UPDATE Roles SET Color = '{to}' WHERE SawDimension = '{dimension}' AND Color = '{from}';");
            }

            foreach (var (name, from, to) in UserStart)
            {
                migrationBuilder.Sql(
                    $"UPDATE Roles SET Color = '{to}' WHERE SawDimension IS NULL AND Name = '{name}' AND Color = '{from}';");
            }

            foreach (var (from, to) in Removed)
            {
                migrationBuilder.Sql($"UPDATE Roles SET Color = '{to}' WHERE Color = '{from}';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратно — только стартовые роли; остальные цвета старой палитре не нужны
            // (новые оттенки просто останутся, валидация на чтение не влияет)
            foreach (var (dimension, from, to) in SawStart)
            {
                migrationBuilder.Sql(
                    $"UPDATE Roles SET Color = '{from}' WHERE SawDimension = '{dimension}' AND Color = '{to}';");
            }

            foreach (var (name, from, to) in UserStart)
            {
                migrationBuilder.Sql(
                    $"UPDATE Roles SET Color = '{from}' WHERE SawDimension IS NULL AND Name = '{name}' AND Color = '{to}';");
            }
        }
    }
}
