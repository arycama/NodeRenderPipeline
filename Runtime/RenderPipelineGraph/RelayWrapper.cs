using NodeGraph;

public class RelayWrapper
{

}

public class RelayWrapper<T> : RelayWrapper, RelayInput<T>
{
    public T Value { get; set; }

    public RelayWrapper(T value)
    {
        Value = value;
    }

    public T GetValue()
    {
        return Value;
    }
}
