using System.Collections.Generic;

public class LruCache<T, K>
{
    private readonly Dictionary<T, LinkedListNode<(T, K)>> lookup = new();
    private readonly LinkedList<(T, K)> cache = new();

    public int Count { get; private set; }

    public void Clear()
    {
        lookup.Clear();
        cache.Clear();
        Count = 0;
    }

    public bool TryGetValue(T key, out (T, K) value)
    {
        if (lookup.TryGetValue(key, out var node))
        {
            value = node.Value;
            return true;
        }

        value = default;
        return false;
    }

    public void SetValue(T key, K value)
    {
        var node = lookup[key];
        lookup[key].Value = (key, value);
        cache.Remove(node);
        cache.AddLast(node);
    }

    public void Update(T key)
    {
        var node = lookup[key];
        cache.Remove(node);
        cache.AddLast(node);
    }

    public void Add(T key, K value)
    {
        var lruNode = cache.AddLast((key, value));
        lookup.Add(key, lruNode);
        Count++;
    }

    public (T, K) Remove()
    {
        var result = cache.First;
        cache.RemoveFirst();

        // Remove the tileId from the cache
        lookup.Remove(result.Value.Item1);
        Count--;
        return result.Value;
    }

    public (T, K) Peek()
    {
        return cache.First.Value;
    }

    public bool Contains(T key)
    {
        return lookup.ContainsKey(key);
    }
}