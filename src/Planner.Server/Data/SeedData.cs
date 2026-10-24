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
    /// <summary>
    /// Стартовый набор нового пользователя (docs/specs/accounts.md, N1): 4 измерения пилы, роли «Я», «Семья», «Работа», «Дом»
    /// и категории ленты «💡 Идеи», «📚 Почитать». Первый администратор получает прежние начальные данные (<see cref="EnsureSeededAsync"/>).
    /// </summary>
    public static async Task CreateStarterSetAsync(PlannerDbContext db, CancellationToken ct = default)
    {
        db.Roles.AddRange(SawDimensions());
        (string Name, string Color)[] roles = [("Я", "#dcb8e6"), ("Семья", "#b9d3b0"), ("Работа", "#8fb4e3"), ("Дом", "#f3e3a6")];
        for (var i = 0; i < roles.Length; i++)
        {
            var role = Role.Create(roles[i].Name, roles[i].Color);
            role.SortOrder = i;
            db.Roles.Add(role);
        }

        (string Name, string Emoji)[] categories = [("Идеи", "💡"), ("Почитать", "📚")];
        for (var i = 0; i < categories.Length; i++)
        {
            var category = Category.Create(categories[i].Name, categories[i].Emoji);
            category.SortOrder = i;
            db.Categories.Add(category);
        }

        await db.SaveChangesAsync(ct);
    }

    private static Role[] SawDimensions() =>
    [
        Role.CreateSawDimension(SawDimension.Body, "Тело", "#9fd3bd"),
        Role.CreateSawDimension(SawDimension.Mind, "Ум", "#a9cde8"),
        Role.CreateSawDimension(SawDimension.Spirit, "Дух", "#c9b8ec"),
        Role.CreateSawDimension(SawDimension.Social, "Общение и эмоции", "#f9d0e0"),
    ];

    public static async Task EnsureSeededAsync(PlannerDbContext db, CancellationToken ct = default)
    {
        if (await db.Roles.AnyAsync(ct))
        {
            return;
        }

        // Цвета — как в docs/design-mockup.html
        db.Roles.AddRange(SawDimensions());

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
