using Microsoft.EntityFrameworkCore;
using Planner.Core.Entities;
using Planner.Core.Enums;

namespace Planner.Server.Data;

/// <summary>Начальные данные для пустой базы: «Входящие», 4 измерения пилы, стартовые роли, настройки.</summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(PlannerDbContext db, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(c => c.IsSystem, ct))
        {
            db.Categories.Add(Category.CreateInbox());
        }

        if (!await db.Roles.AnyAsync(ct))
        {
            db.Roles.AddRange(
                Role.CreateSawDimension(SawDimension.Body, "Тело", "#7fa66a"),
                Role.CreateSawDimension(SawDimension.Mind, "Ум", "#6b8fb5"),
                Role.CreateSawDimension(SawDimension.Spirit, "Дух", "#8a76b8"),
                Role.CreateSawDimension(SawDimension.Social, "Общение и эмоции", "#d08a4e"));

            string[] names = ["Я сама", "Семья", "Работа", "Дом"];
            string[] colors = ["#5f9e8f", "#c46c6c", "#8c7a6b", "#c2a54f"];
            for (var i = 0; i < names.Length; i++)
            {
                var role = Role.Create(names[i], colors[i]);
                role.SortOrder = i;
                db.Roles.Add(role);
            }
        }

        if (!await db.Settings.AnyAsync(ct))
        {
            db.Settings.Add(new UserSettings());
        }

        await db.SaveChangesAsync(ct);
    }
}
