using System;
using System.Collections.Generic;

public static class DictionaryExtensions
{
    public static K CreateIfNotAdded<T, K>(this Dictionary<T, K> dictionary, T key) where K : new()
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = new K();
            dictionary.Add(key, value);
        }

        return value;
    }

    public static K CreateIfNotAdded<T, K>(this Dictionary<T, K> dictionary, T key, Func<K> createDelegate)
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = createDelegate();
            dictionary.Add(key, value);
        }

        return value;
    }

    public static void Cleanup<T, K>(this Dictionary<T, K> dictionary, Action<K> cleanupAction)
    {
        foreach (var item in dictionary.Values)
        {
            cleanupAction.Invoke(item);
        }

        dictionary.Clear();
    }
}