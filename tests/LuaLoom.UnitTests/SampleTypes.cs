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

    public string Join(string separator, params object[] items) => string.Join(separator, items);

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

public delegate TResult Mapper<in T, out TResult>(T value);

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

public static class Parsing
{
    public static bool TryParse(string text, out int value) => int.TryParse(text, out value);

    public static void Bump(ref int value) => value++;

    public static int AddAndBump(int value, ref int extra)
    {
        extra += value;
        return value * 2;
    }

    public static string Join(string separator, params object[] items) => string.Join(separator, items);

    public static int Sum(params int[] values)
    {
        var total = 0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }
}

public static class PlayerExtensions
{
    public static int Triple(this Player player, int value) => player.Hp * value;
}

public static class Overloads
{
    public static string Pick(string value) => "string";

    public static string Pick(object value) => "object";

    public static string Pick(int value) => "int";

    public static string Pick(long value) => "long";

    public static int Optional(int value = 7) => value;

    [Obsolete("Use Optional.")]
    public static int Legacy() => 1;
}

public sealed class NumberList
{
    public NumberList(params int[] values)
    {
        foreach (var value in values)
        {
            Total += value;
        }
    }

    public int Total;
}
