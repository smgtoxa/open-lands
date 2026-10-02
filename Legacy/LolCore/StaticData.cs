// The engine's static tables, as generated for the JavaScript build from ScummVM's create_kyradat
// data and exported by scripts/export_static_cs.mjs. They are constants of the original game, so
// they are carried as data rather than retyped: 73 tables, embedded in the assembly.
using System.Text.Json;

namespace LolCore;

public static class StaticData
{
    private static readonly Dictionary<string, int[]> Numbers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string[]> Strings = new(StringComparer.Ordinal);

    static StaticData()
    {
        using var stream = typeof(StaticData).Assembly.GetManifestResourceStream("LolCore.static-data.json")
            ?? throw new InvalidOperationException("static-data.json is not embedded in LolCore");
        using var doc = JsonDocument.Parse(stream);
        foreach (var table in doc.RootElement.EnumerateObject())
        {
            var first = table.Value.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.String) Strings[table.Name] = table.Value.EnumerateArray().Select(v => v.GetString()).ToArray();
            else Numbers[table.Name] = table.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        }
    }

    public static int[] Table(string name) => Numbers.TryGetValue(name, out var table) ? table : throw new KeyNotFoundException($"No static table {name}");

    public static string[] Names(string name) => Strings.TryGetValue(name, out var table) ? table : throw new KeyNotFoundException($"No static table {name}");

    /// <summary>A signed byte from a table the original stores as bytes.</summary>
    public static int SByte(string name, int index) => (sbyte)Table(name)[index];

    /// <summary>A signed word from a table the original stores as words.</summary>
    public static int Short(string name, int index) => (short)Table(name)[index];
}
