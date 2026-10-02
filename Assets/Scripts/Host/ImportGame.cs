// scripts/import_game.mjs: extract a local GOG Lands of Lore installation (the ISO 9660 image GAME.DAT plus
// the loose music / speech files) into the app's data folder, with import-manifest.json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace LolHost
{
    public static class ImportGame
    {
        const int SECTOR = 2048;
        const int PVD_SECTOR = 16;
        static readonly string[] LOOSE_FILES = { "ENG.LM", "FRE.LM", "GER.LM", "LORE01A.ADL", "LORE01B.ADL", "LORE01C.ADL", "LORE01D.ADL", "LORE02A.ADL", "LORE03A.ADL", "LORE03B.ADL", "LORE04A.ADL", "LORE05A.ADL", "LORE05B.ADL", "LORE06A.ADL", "LORE06B.ADL", "LORE07A.ADL", "LORE08A.ADL", "LORE08B.ADL", "LOREINTR.ADL", "LORESFX.ADL", "VOC.PAK" };

        sealed class Entry { public string path, name; public long extent, size; public bool isDirectory; }

        sealed class Iso9660 : IDisposable
        {
            readonly FileStream fd;
            readonly long size;
            public readonly string volumeId;
            readonly Entry root;

            public Iso9660(string image)
            {
                fd = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read);
                size = fd.Length;
                var pvd = readAt((long)PVD_SECTOR * SECTOR, SECTOR);
                if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001" || pvd[6] != 1) throw new Exception($"{image} is not an ISO9660 primary volume");
                volumeId = Latin1(pvd, 40, 32).TrimEnd(' ');
                root = parseRecord(pvd, 156, "");
            }

            public void Dispose() => fd.Dispose();

            public byte[] readAt(long offset, int count)
            {
                if (offset < 0 || count < 0 || offset + count > size) throw new Exception($"ISO read outside image: {offset}+{count}");
                var buf = new byte[count];
                fd.Position = offset;
                int got = 0;
                while (got < count)
                {
                    int n = fd.Read(buf, got, count - got);
                    if (n == 0) throw new Exception("Unexpected end of ISO image");
                    got += n;
                }
                return buf;
            }

            Entry parseRecord(byte[] data, int offset, string parent)
            {
                int length = data[offset];
                if (length < 34 || offset + length > data.Length) throw new Exception($"Invalid ISO directory record at {offset}");
                long extent = BitConverter.ToUInt32(data, offset + 2);
                long fileSize = BitConverter.ToUInt32(data, offset + 10);
                int flags = data[offset + 25];
                int nameLength = data[offset + 32];
                if (offset + 33 + nameLength > offset + length) throw new Exception("Invalid ISO filename length");
                string name = nameLength == 1 && data[offset + 33] <= 1 ? "." : Latin1(data, offset + 33, nameLength).Split(';')[0].TrimEnd('.');
                return new Entry { path = name == "." ? parent : parent.Length > 0 ? $"{parent}/{name}" : name, name = name, extent = extent, size = fileSize, isDirectory = (flags & 2) != 0 };
            }

            public List<Entry> entries()
            {
                var result = new List<Entry>();
                var visited = new HashSet<string>();
                void visit(Entry dir)
                {
                    string key = $"{dir.extent}:{dir.size}";
                    if (!visited.Add(key)) return;
                    var data = readAt(dir.extent * SECTOR, (int)dir.size);
                    int offset = 0;
                    while (offset < data.Length)
                    {
                        int length = data[offset];
                        if (length == 0) { offset = (offset / SECTOR + 1) * SECTOR; continue; }
                        var entry = parseRecord(data, offset, dir.path);
                        offset += length;
                        if (entry.name == ".") continue;
                        if (entry.isDirectory) visit(entry); else result.Add(entry);
                    }
                }
                visit(root);
                return result;
            }

            public string extract(Entry entry, string destination)
            {
                if (entry.path.Split('/').Contains("..") || Path.IsPathRooted(entry.path)) throw new Exception($"Unsafe ISO path: {entry.path}");
                string target = Path.Combine(new[] { destination }.Concat(entry.path.Split('/')).ToArray());
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var hash = SHA256.Create())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                {
                    long remaining = entry.size, at = entry.extent * SECTOR;
                    while (remaining > 0)
                    {
                        var chunk = readAt(at, (int)Math.Min(1 << 20, remaining));
                        output.Write(chunk, 0, chunk.Length);
                        hash.TransformBlock(chunk, 0, chunk.Length, null, 0);
                        remaining -= chunk.Length;
                        at += chunk.Length;
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return Hex(hash.Hash);
                }
            }
        }

        static string Latin1(byte[] b, int offset, int count)
        {
            var chars = new char[count];
            for (int i = 0; i < count; i += 1) chars[i] = (char)b[offset + i];
            return new string(chars);
        }

        static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        static string hashFile(string file, HashAlgorithm algo)
        {
            using (algo) using (var s = File.OpenRead(file)) return Hex(algo.ComputeHash(s));
        }

        sealed class FileInfoEntry { public string path, sha256, source; public long size; }

        static string detectDataRoot(List<FileInfoEntry> files)
        {
            var paths = new HashSet<string>(files.Select(f => f.path.ToUpperInvariant()));
            string join(string dir, string name) => dir == "." || dir.Length == 0 ? name : $"{dir}/{name}";
            var parents = files.Where(f => Path.GetFileName(f.path).ToUpperInvariant() == "GENERAL.PAK")
                .Select(f => { int slash = f.path.LastIndexOf('/'); return slash < 0 ? "." : f.path.Substring(0, slash); })
                .Where(dir => paths.Contains(join(dir, "STARTUP.PAK").ToUpperInvariant()) && paths.Contains(join(dir, "L01.PAK").ToUpperInvariant())).ToList();
            if (parents.Count == 0) throw new Exception("Could not locate a game root containing GENERAL.PAK, STARTUP.PAK, and L01.PAK");
            parents.Sort((a, b) => a.Split('/').Length != b.Split('/').Length ? a.Split('/').Length - b.Split('/').Length : string.CompareOrdinal(a, b));
            return parents[0] == "." ? "" : parents[0];
        }

        /// <summary>importGame(source, destination, progress(done, total, name)) -> the manifest (volumeId, files)</summary>
        public static (int files, string volumeId, string dataRoot) importGame(string source, string destination, Action<int, int, string> progress = null)
        {
            string image = Path.Combine(source, "GAME.DAT");
            if (!File.Exists(image)) throw new Exception($"Missing {image}");
            Directory.CreateDirectory(destination);
            var files = new List<FileInfoEntry>();
            string volumeId;
            using (var iso = new Iso9660(image))
            {
                volumeId = iso.volumeId;
                var entries = iso.entries();
                for (int i = 0; i < entries.Count; i += 1)
                {
                    var entry = entries[i];
                    progress?.Invoke(i, entries.Count, entry.path);
                    files.Add(new FileInfoEntry { path = entry.path, size = entry.size, sha256 = iso.extract(entry, destination), source = "GAME.DAT" });
                }
            }
            string dataRoot = detectDataRoot(files);
            var extracted = new HashSet<string>(files.Select(f => Path.GetFileName(f.path).ToUpperInvariant()));
            foreach (var name in LOOSE_FILES)
            {
                string src = Path.Combine(source, name);
                if (!File.Exists(src) || extracted.Contains(name)) continue;
                string target = Path.Combine(destination, name);
                File.Copy(src, target, true);
                files.Add(new FileInfoEntry { path = name, size = new FileInfo(target).Length, sha256 = hashFile(target, SHA256.Create()), source = "GOG directory" });
            }
            files.Sort((a, b) => string.CompareOrdinal(a.path.ToUpperInvariant(), b.path.ToUpperInvariant()));
            var manifest = new JsonObject
            {
                ["formatVersion"] = 1, ["game"] = "Lands of Lore: The Throne of Chaos", ["volumeId"] = volumeId, ["dataRoot"] = dataRoot, ["sourceImage"] = "GAME.DAT",
                ["sourceImageSize"] = new FileInfo(image).Length, ["sourceImageMd5"] = hashFile(image, MD5.Create()),
                ["files"] = new JsonArray(files.Select(f => (JsonNode)new JsonObject { ["path"] = f.path, ["size"] = f.size, ["sha256"] = f.sha256, ["source"] = f.source }).ToArray()),
            };
            File.WriteAllText(Path.Combine(destination, "import-manifest.json"), manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
            return (files.Count, volumeId, dataRoot);
        }
    }
}
