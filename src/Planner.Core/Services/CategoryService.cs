using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>Категории ленты «Когда-нибудь».</summary>
public class CategoryService(
    ICategoryRepository categories,
    ITaskRepository tasks,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>Новая категория — в конец списка.</summary>
    public async Task<Category> CreateAsync(string name, CancellationToken ct = default)
    {
        var all = await categories.GetAllAsync(ct);
        var category = Category.Create(name);
        category.SortOrder = all.Count == 0 ? 0 : all.Max(c => c.SortOrder) + 1;
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category> UpdateAsync(int id, string? name, bool? isExpanded, CancellationToken ct = default)
    {
        var category = await GetAsync(id, ct);
        if (name is not null)
        {
            category.Rename(name);
        }

        if (isExpanded is { } expanded)
        {
            category.IsExpanded = expanded;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return category;
    }

    public async Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        var all = await categories.GetAllAsync(ct);
        SortOrdering.Reorder(all, orderedIds, c => c.Id);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Удалить категорию. Её дела из ленты уходят в корзину;
    /// при восстановлении попадут во «Входящие» (CategoryId обнулится на уровне БД).
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await GetAsync(id, ct);
        if (category.IsSystem)
        {
            throw new DomainException("Категорию «Входящие» удалить нельзя.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var task in await tasks.GetSomedayInCategoryAsync(id, ct))
        {
            task.Trash(now);
        }

        categories.Remove(category);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Category> GetAsync(int id, CancellationToken ct) =>
        await categories.FindAsync(id, ct) ?? throw new NotFoundException("Категория", id);
}
