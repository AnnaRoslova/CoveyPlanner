using Planner.Core.Errors;
using Planner.Core.Sorting;

namespace Planner.Tests.Core;

public class SortOrderingTests
{
    private sealed class Item(int id, int order) : ISortable
    {
        public int Id { get; } = id;

        public int SortOrder { get; set; } = order;
    }

    private static List<Item> MakeItems(int count) =>
        Enumerable.Range(0, count).Select(i => new Item(i + 1, i)).ToList();

    private static int[] IdsInOrder(IEnumerable<Item> items) =>
        items.OrderBy(i => i.SortOrder).Select(i => i.Id).ToArray();

    [Fact]
    public void Place_NewItemAtTop_ShiftsOthers()
    {
        var items = MakeItems(3);
        var added = new Item(99, 0);

        SortOrdering.Place(items, added, 0);

        Assert.Equal([99, 1, 2, 3], IdsInOrder(items.Append(added)));
    }

    [Fact]
    public void Place_ExistingItemDown_IndexWithoutItself()
    {
        var items = MakeItems(4); // 1,2,3,4

        // Переносим «1» так, чтобы оно встало после «3» (индекс 2 в списке 2,3,4)
        SortOrdering.Place(items, items[0], 2);

        Assert.Equal([2, 3, 1, 4], IdsInOrder(items));
    }

    [Fact]
    public void Place_IndexOutOfRange_ClampsToEnd()
    {
        var items = MakeItems(2);
        var added = new Item(99, 0);

        SortOrdering.Place(items, added, 100);

        Assert.Equal([1, 2, 99], IdsInOrder(items.Append(added)));
    }

    [Fact]
    public void Reorder_ByIds()
    {
        var items = MakeItems(3);

        SortOrdering.Reorder(items, [3, 1, 2], i => i.Id);

        Assert.Equal([3, 1, 2], IdsInOrder(items));
    }

    [Fact]
    public void Reorder_MismatchedIds_Throws()
    {
        var items = MakeItems(3);

        Assert.Throws<DomainException>(() => SortOrdering.Reorder(items, [1, 2], i => i.Id));
        Assert.Throws<DomainException>(() => SortOrdering.Reorder(items, [1, 2, 2], i => i.Id));
        Assert.Throws<DomainException>(() => SortOrdering.Reorder(items, [1, 2, 7], i => i.Id));
    }
}
