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
    /// <summary>Новая категория — в конец списка, а если <paramref name="atTop"/> — в начало.</summary>
    public async Task<Category> CreateAsync(string name, string? emoji, bool atTop = false, CancellationToken ct = default)
    {
        var all = await categories.GetAllAsync(ct);
        var category = Category.Create(name, emoji);
        category.SortOrder = all.Count == 0 ? 0 : atTop ? all.Min(c => c.SortOrder) - 1 : all.Max(c => c.SortOrder) + 1;
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(ct);
        return category;
    }

    /// <summary>null — поле не менять; emoji = "" — убрать иконку.</summary>
    public async Task<Category> UpdateAsync(
        int id, string? name, string? emoji, bool? isExpanded, CancellationToken ct = default)
    {
        var category = await GetAsync(id, ct);
        if (name is not null)
        {
            category.Rename(name);
        }

        if (emoji is not null)
        {
            category.SetEmoji(emoji);
        }

        if (isExpanded is { } expanded)
        {
            category.IsExpanded = expanded;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return category;
    }

    /// <summary>Раскрыть или свернуть все категории.</summary>
    public async Task SetAllExpandedAsync(bool isExpanded, CancellationToken ct = default)
    {
        foreach (var category in await categories.GetAllAsync(ct))
        {
            category.IsExpanded = isExpanded;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        var all = await categories.GetAllAsync(ct);
        SortOrdering.Reorder(all, orderedIds, c => c.Id);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Удалить категорию. Её дела из ленты уходят в корзину (CategoryId обнулится на уровне БД);
    /// при восстановлении категорию спросят.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await GetAsync(id, ct);

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
