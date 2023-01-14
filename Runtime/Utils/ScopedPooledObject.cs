using System;
using UnityEngine.Pool;

public struct ScopedPooledObject<T> : IDisposable where T : class, new()
{
    public T Value { get; private set; }

    public static implicit operator T(ScopedPooledObject<T> value) => value.Value;

    public static ScopedPooledObject<T> Get() => new() { Value = GenericPool<T>.Get() };

    public void Dispose() => GenericPool<T>.Release(Value);
}
