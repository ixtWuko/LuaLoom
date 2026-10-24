namespace LuaLoom.UnitTests.Sample;

public class Player
{
    public int Hp;
    public string Name { get; set; } = string.Empty;

    public Player()
    {
    }

    public Player(int hp)
    {
        Hp = hp;
    }

    public static int Count;

    public int Add(int a, int b) => a + b;

    public int Add(int value) => Hp + value;

    public static int Double(int value) => value * 2;
}

public class Config
{
    public int Hp;
    public string Name = string.Empty;
    public Vec2 Pos;
}

public struct Vec2
{
    public float X;
    public float Y;
}

public interface ICalculator
{
    int Add(int a, int b);

    int Value { get; set; }
}

public sealed class Calculator : ICalculator
{
    public int Add(int a, int b) => a + b;

    public int Value { get; set; }
}

public delegate int IntOp(int a, int b);

public static class Thrower
{
    public static void Boom() => throw new InvalidOperationException("boom");
}

public sealed class Money
{
    public Money(int amount)
    {
        Amount = amount;
    }

    public int Amount;

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator -(Money value) => new(-value.Amount);

    public static bool operator ==(Money left, Money right) => left.Amount == right.Amount;

    public static bool operator !=(Money left, Money right) => !(left == right);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public override bool Equals(object? obj) => obj is Money other && this == other;

    public override int GetHashCode() => Amount;
}

public sealed class Bag
{
    private readonly Dictionary<int, int> _values = new();

    public int this[int index]
    {
        get => _values.TryGetValue(index, out var value) ? value : 0;
        set => _values[index] = value;
    }
}

public sealed class Emitter
{
    public event Action<int>? Changed;

    public void Raise(int value) => Changed?.Invoke(value);
}

public static class Generics
{
    public static string Describe<T>(T value) => typeof(T).Name + ":" + value;
}

public sealed class GenericBox
{
    public string Show<T>(T value) => typeof(T).Name + "=" + value;
}

public class Box<T>
{
    public T? Value { get; set; }
}
