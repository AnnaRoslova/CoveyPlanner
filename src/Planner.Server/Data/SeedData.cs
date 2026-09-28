using Microsoft.EntityFrameworkCore;
using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Server.Data;

/// <summary>
/// Начальные данные для пустой базы: 4 измерения пилы и стартовые роли.
/// Категорий нет — пользователь создаёт их сам (лента подскажет).
/// </summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(PlannerDbContext db, CancellationToken ct = default)
    {
        if (await db.Roles.AnyAsync(ct))
        {
            return;
        }

        // Цвета — как в docs/design-mockup.html
        db.Roles.AddRange(
            Role.CreateSawDimension(SawDimension.Body, "Тело", "#f4c3cf"),
            Role.CreateSawDimension(SawDimension.Mind, "Ум", "#94b4b5"),
            Role.CreateSawDimension(SawDimension.Spirit, "Дух", "#6fbfae"),
            Role.CreateSawDimension(SawDimension.Social, "Общение и эмоции", "#f29ea6"));

        (string Name, string Color)[] roles =
        [
            ("Я сама", "#a8dcd0"),
            ("Семья", "#e9b0b8"),
            ("Работа", "#8ac2c4"),
            ("Дом", "#f7d3db"),
        ];
        for (var i = 0; i < roles.Length; i++)
        {
            var role = Role.Create(roles[i].Name, roles[i].Color);
            role.SortOrder = i;
            db.Roles.Add(role);
        }

        await db.SaveChangesAsync(ct);
    }
}
