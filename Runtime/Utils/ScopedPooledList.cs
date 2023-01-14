using System;
using System.Collections.Generic;
using UnityEngine.Pool;

public struct ScopedPooledList<T> : IDisposable
{
    public List<T> Value { get; private set; }

    public static implicit operator List<T>(ScopedPooledList<T> value) => value.Value;

    public static ScopedPooledList<T> Get() => new() { Value = ListPool<T>.Get() };

    public void Dispose() => ListPool<T>.Release(Value);
}
