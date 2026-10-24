namespace LuaLoom.Sample;

public class Player
{
    public Player()
    {
    }

    public Player(int hp)
    {
        Hp = hp;
    }

    public int Hp;

    public string Name { get; set; } = string.Empty;

    public static int Count { get; set; }

    public int Add(int value) => Hp + value;

    public int Add(int a, int b) => a + b;

    public static int Double(int value) => value * 2;
}

public class SampleConfig
{
    public int Hp;

    public string Name = string.Empty;
}

public delegate int IntCallback(int value);
