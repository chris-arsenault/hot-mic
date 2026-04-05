namespace HotMic.Common;

/// <summary>
/// Extension methods for IList collections (replaces List-specific methods when using IList property types).
/// </summary>
public static class CollectionExtensions
{
    public static void AddRange<T>(this IList<T> list, IEnumerable<T> items)
    {
        if (list is List<T> concreteList)
        {
            concreteList.AddRange(items);
            return;
        }

        foreach (var item in items)
            list.Add(item);
    }

    public static int RemoveAll<T>(this IList<T> list, Predicate<T> match)
    {
        if (list is List<T> concreteList)
            return concreteList.RemoveAll(match);

        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (match(list[i]))
            {
                list.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    public static int FindIndex<T>(this IList<T> list, Predicate<T> match)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (match(list[i]))
                return i;
        }
        return -1;
    }
}
