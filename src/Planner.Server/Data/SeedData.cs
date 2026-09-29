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
            Role.CreateSawDimension(SawDimension.Body, "Тело", "#9fd3bd"),
            Role.CreateSawDimension(SawDimension.Mind, "Ум", "#a9cde8"),
            Role.CreateSawDimension(SawDimension.Spirit, "Дух", "#c9b8ec"),
            Role.CreateSawDimension(SawDimension.Social, "Общение и эмоции", "#f9d0e0"));

        (string Name, string Color)[] roles =
        [
            ("Я сама", "#dcb8e6"),
            ("Семья", "#b9d3b0"),
            ("Работа", "#8fb4e3"),
            ("Дом", "#f3e3a6"),
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
