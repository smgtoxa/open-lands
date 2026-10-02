// File lookup: PAK archives opened in load order, first one wins - the SearchSet order of
// src/game/engine.mjs Resources, so GENERAL.PAK shadows a later level PAK exactly as the game does.
namespace LolCore;

public sealed class Resources
{
    private readonly Func<string, byte[]> _openFile;
    private readonly List<(string Name, PakArchive Archive)> _archives = new();

    /// <summary>`openFile` returns the bytes of a file in the data directory, or null if absent.</summary>
    public Resources(Func<string, byte[]> openFile) => _openFile = openFile;

    public bool LoadPak(string name)
    {
        if (_archives.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) return true;
        var bytes = _openFile(name);
        if (bytes == null) return false;
        _archives.Add((name, new PakArchive(bytes)));
        return true;
    }

    /// <summary>
    /// loadFileList: every .PAK the game's own file table lists, in its order. The speech archives
    /// are streamed on demand and the music and driver packs are not needed, so they are skipped.
    /// </summary>
    public IReadOnlyList<string> LoadFileList(byte[] bytes, Func<string, bool> skip = null)
    {
        var names = new List<string>();
        for (int p = 0; p + 4 <= bytes.Length; p += 20)
        {
            int offset = bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16) | (bytes[p + 3] << 24);
            if (offset == 0) break;
            var name = new System.Text.StringBuilder();
            for (int i = 0; i < 12 && offset + i < bytes.Length && bytes[offset + i] != 0; i += 1)
                name.Append((char)bytes[offset + i]);
            string file = name.ToString().ToUpperInvariant();
            if (!file.EndsWith(".PAK", StringComparison.Ordinal)) continue;
            if (file is "MUSIC.PAK" or "DRIVERS.PAK") continue;
            if (skip != null && skip(file)) continue;
            names.Add(file);
        }
        foreach (string file in names) LoadPak(file);
        return names;
    }

    public bool Exists(string name) => _archives.Any(a => a.Archive.Has(name));

    public byte[] Get(string name)
    {
        foreach (var (_, archive) in _archives) if (archive.Has(name)) return archive.Get(name);
        return _openFile(name) ?? throw new FileNotFoundException($"Missing resource: {name}");
    }
}
