using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>Роли Кови и измерения «Затачивания пилы».</summary>
public class RoleService(
    IRoleRepository roles,
    IGoalRepository goals,
    IGoalSeriesRepository series,
    GoalRemover remover,
    IUnitOfWork unitOfWork)
{
    /// <summary>Новая роль — в конец; цвет по умолчанию — следующий из палитры.</summary>
    public async Task<Role> CreateAsync(string name, string? color, CancellationToken ct = default)
    {
        var userRoles = (await roles.GetAllAsync(ct)).Where(r => !r.IsSharpenSaw).ToList();
        var role = Role.Create(name, color ?? RolePalette.ForIndex(userRoles.Count));
        role.SortOrder = userRoles.Count == 0 ? 0 : userRoles.Max(r => r.SortOrder) + 1;
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(ct);
        return role;
    }

    public async Task<Role> UpdateAsync(int id, string? name, string? color, CancellationToken ct = default)
    {
        var role = await GetAsync(id, ct);
        if (name is not null && name.Trim() != role.Name)
        {
            role.Rename(name);
        }

        if (color is not null)
        {
            role.SetColor(color);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return role;
    }

    /// <summary>Порядок пользовательских ролей (измерения пилы всегда сверху в своём порядке).</summary>
    public async Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        var userRoles = (await roles.GetAllAsync(ct)).Where(r => !r.IsSharpenSaw).ToList();
        SortOrdering.Reorder(userRoles, orderedIds, r => r.Id);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Удалить роль. Цели, у которых это единственная роль, удаляются; занятия — как при удалении цели
    /// (<see cref="GoalRemover"/>: занятия без дня — в ленту, в свою категорию или в «неразобранное»).
    /// У целей с другими ролями роль просто убирается (вместе с ролью их занятий); так же в шаблонах серий —
    /// серия без ролей удаляется (её цели остаются разовыми). У остальных дел роль сбрасывается (на уровне БД).
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var role = await GetAsync(id, ct);
        role.EnsureEditable();

        foreach (var goal in await goals.GetAllForRoleAsync(id, ct))
        {
            if (goal.RoleIds.Count > 1)
            {
                goal.RemoveRole(id);
            }
            else
            {
                await remover.RemoveAsync(goal, ct);
            }
        }

        foreach (var withRole in await series.GetWithRoleAsync(id, ct))
        {
            if (!withRole.RemoveRole(id))
            {
                series.Remove(withRole);
            }
        }

        roles.Remove(role);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Role> GetAsync(int id, CancellationToken ct) =>
        await roles.FindAsync(id, ct) ?? throw new NotFoundException("Роль", id);
}
