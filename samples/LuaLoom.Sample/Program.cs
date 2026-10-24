using LuaLoom;
using LuaLoom.Sample;
using LuaLoom.Sample.Generated;

var failures = new List<string>();

using var env = new LuaEnv(new LuaEnvOptions { SearchPaths = ["scripts/?.lua"] });
env.AddLoader((ref string name) =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "scripts", name.Replace('.', '/') + ".lua");
    if (!File.Exists(path))
    {
        return null;
    }

    name = path;
    return File.ReadAllBytes(path);
});

LuaBindings.Register(env);

Check("init", env.MemoryBytes > 0);

var answer = env.DoString("return require('main').answer()", "flow-2");
Check("require", answer is [long value] && value == 42);

var hp = env.DoString("local p = CS.LuaLoom.Sample.Player(5); p.Hp = 7; return p.Hp", "flow-3");
Check("lua->csharp", hp is [long hpValue] && hpValue == 7);

var doubleIt = env.Global.Get<IntCallback>("double_it");
Check("csharp->lua", doubleIt(21) == 42);

using var table = (LuaTable)env.DoString("return { Hp = 3, Name = 'sample' }", "flow-5")[0]!;
var config = table.Cast<SampleConfig>();
Check("cast", config.Hp == 3 && config.Name == "sample");

env.StartProfile();
env.DoString("local total = 0; for i = 1, 10000 do total = total + i end; return total", "flow-6");
var report = env.StopProfile(ProfileSort.Total);
var memory = env.MemorySnapshot(5);
Check("diagnostics", report.Contains("CALLS", StringComparison.Ordinal) && memory.Contains("ENTRIES", StringComparison.Ordinal));

Console.WriteLine(report);
Console.WriteLine();
Console.WriteLine(memory);

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILED: " + string.Join(", ", failures));
    return 1;
}

Console.WriteLine();
Console.WriteLine("ALL PASSED");
return 0;

void Check(string name, bool condition)
{
    Console.WriteLine($"[{(condition ? "ok" : "fail")}] {name}");
    if (!condition)
    {
        failures.Add(name);
    }
}
