using Planner.Core.Errors;

namespace Planner.Core.Sorting;

/// <summary>Сущность с ручным порядком внутри своего «контейнера» (категории, дня, роли).</summary>
public interface ISortable
{
    int SortOrder { get; set; }
}

/// <summary>
/// Операции с ручным порядком. В контейнере, куда что-то поставили, SortOrder = 0..n-1;
/// в контейнере, откуда элемент ушёл, остаётся «дыра» — это не мешает, важен только относительный порядок.
/// </summary>
public static class SortOrdering
{
    /// <summary>
    /// Ставит <paramref name="item"/> на позицию <paramref name="index"/> среди соседей
    /// и перенумеровывает всех. Индекс считается в списке БЕЗ самого элемента,
    /// выход за границы прижимается к краю.
    /// </summary>
    /// <param name="siblings">Соседи в текущем порядке (элемент может в них уже быть).</param>
    public static void Place<T>(IEnumerable<T> siblings, T item, int index) where T : class, ISortable
    {
        var list = siblings.Where(s => !ReferenceEquals(s, item)).ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), item);
        Renumber(list);
    }

    /// <summary>Проставляет SortOrder = 0..n-1 в порядке перечисления.</summary>
    public static void Renumber<T>(IEnumerable<T> items) where T : ISortable
    {
        var order = 0;
        foreach (var item in items)
        {
            item.SortOrder = order++;
        }
    }

    /// <summary>
    /// Переставляет элементы в порядке, заданном списком Id.
    /// Список должен содержать ровно те же Id, что и <paramref name="items"/>.
    /// </summary>
    public static void Reorder<T>(IReadOnlyCollection<T> items, IReadOnlyList<int> orderedIds, Func<T, int> getId)
        where T : ISortable
    {
        var byId = items.ToDictionary(getId);
        if (orderedIds.Count != byId.Count || orderedIds.Distinct().Count() != orderedIds.Count
            || orderedIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new DomainException("Список для сортировки не совпадает с текущими элементами.");
        }

        Renumber(orderedIds.Select(id => byId[id]));
    }
}
