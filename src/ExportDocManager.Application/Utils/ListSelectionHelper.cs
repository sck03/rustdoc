namespace ExportDocManager.Utils
{
    public static class ListSelectionHelper
    {
        public static T GetFallbackItemAfterRemoval<T>(IReadOnlyList<T> items, Func<T, bool> removedPredicate)
        {
            ArgumentNullException.ThrowIfNull(removedPredicate);

            if (items == null || items.Count <= 1)
            {
                return default;
            }

            var removedIndex = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (removedPredicate(items[i]))
                {
                    removedIndex = i;
                    break;
                }
            }

            if (removedIndex < 0)
            {
                return default;
            }

            return removedIndex < items.Count - 1
                ? items[removedIndex + 1]
                : items[removedIndex - 1];
        }
    }
}
