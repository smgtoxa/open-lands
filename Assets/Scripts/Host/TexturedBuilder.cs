// Unity build only: the "Textured world" packs, made in the game from the player's own game files (the result is the
// original art with photo materials over it, so it is never shipped: each player builds it from their copy).
// A hidden engine on a background thread visits every level and reads its wall tiles, wall-set views, backdrop and
// the shapes of its decorations, doors, items and thrown things (as tools/textured/export_hd_*.mjs do); the texture
// step is tools/textured/texture.py in C#. The packs are the HD pack format (HdScene.cs): <folder>/level<N>/
// manifest.json, tiles.png, shapes/*.png. Creatures are left out, so they keep their original sprites.
// The materials are CC0 photo textures (Poly Haven) shipped in StreamingAssets/materials.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Lol;

namespace LolHost
{
    public sealed class TexturedBuilder
    {
        public const int VERSION = 1;
        const int SCALE = 4, TS = 8 * SCALE, COLUMNS = 32, LEVELS = 29;
        const double STRENGTH = 0.85, TEXEL = 0.5;
        static readonly int[] FRONT = { 7, 5, 2, 1 };
        // the nine wall views: VMP offset inside a set, width and height in tiles (scene.mjs)
        static readonly int[][] VIEWS = { new[] { 102, 3, 5 }, new[] { 97, 1, 5 }, new[] { 129, 6, 5 }, new[] { 117, 2, 6 }, new[] { 81, 2, 8 }, new[] { 159, 10, 8 }, new[] { 45, 3, 12 }, new[] { 239, 16, 12 }, new[] { 0, 3, 15 } };
        // wall, floor, ceiling, wood, green
        static readonly Dictionary<string, string[]> THEMES = new Dictionary<string, string[]>
        {
            ["KEEP"] = new[] { "plastered_wall_04", "marble_01", "worn_planks", "worn_planks", "forest_leaves_03" },
            ["FOREST"] = new[] { "bark_brown_02", "forest_ground_04", "forest_leaves_03", "bark_willow", "forest_leaves_03" },
            ["SWAMP"] = new[] { "bark_willow", "brown_mud_leaves_01", "forest_leaves_03", "bark_willow", "forest_leaves_03" },
            ["CAVE"] = new[] { "rock_wall_08", "rocky_trail", "rock_wall_08", "worn_planks", "forest_leaves_03" },
            ["MINE"] = new[] { "rock_boulder_dry", "rocky_trail", "rock_boulder_dry", "worn_planks", "forest_leaves_03" },
            ["CATWALK"] = new[] { "rock_wall_08", "rocky_trail", "rock_wall_08", "worn_planks", "forest_leaves_03" },
            ["URBISH"] = new[] { "worn_planks", "rocky_trail", "worn_planks", "worn_planks", "forest_leaves_03" },
            ["MANOR"] = new[] { "plastered_wall_04", "wood_table_worn", "worn_planks", "worn_planks", "forest_leaves_03" },
            ["TOWER"] = new[] { "marble_01", "marble_01", "marble_01", "worn_planks", "forest_leaves_03" },
            ["YVEL"] = new[] { "plastered_wall_04", "rocky_trail", "worn_planks", "worn_planks", "forest_leaves_03" },
            ["CIMMERIA"] = new[] { "castle_wall_slates", "marble_01", "rock_surface", "worn_planks", "forest_leaves_03" },
            ["RUIN"] = new[] { "rock_surface", "rocky_trail", "rock_surface", "worn_planks", "forest_leaves_03" },
        };
        static readonly string[] DEFAULT_THEME = { "rock_surface", "rocky_trail", "rock_surface", "worn_planks", "forest_leaves_03" };
        public static IEnumerable<string> MaterialNames => THEMES.Values.SelectMany(t => t).Concat(DEFAULT_THEME).Distinct();

        /// <summary>a material's luminance (PIL "L"), decoded by the caller on Unity's main thread</summary>
        public sealed class Material { public int w, h; public byte[] lum; }

        readonly string data, folder;
        readonly Dictionary<string, Material> materials;
        readonly Dictionary<string, Gray> grains = new Dictionary<string, Gray>();
        public volatile int level;            // the level being built (0: starting)
        public volatile bool done, failed;
        public volatile string error;
        public readonly CancellationTokenSource cancel = new CancellationTokenSource();
        public Action<int> levelReady;        // called on the builder's thread: the level's pack is in place

        public TexturedBuilder(string data, string folder, Dictionary<string, Material> materials)
        {
            this.data = data; this.folder = folder; this.materials = materials;
        }

        // ---- the stamp: rebuilt when the generator or the game files change ----
        public static string Stamp(string data)
        {
            var sb = new StringBuilder($"v{VERSION}");
            foreach (var f in Directory.GetFiles(data, "*.PAK").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                sb.Append($"|{Path.GetFileName(f).ToUpperInvariant()}:{new FileInfo(f).Length}");
            return sb.ToString();
        }

        public static bool LevelBuilt(string folder, string data, int lvl)
        {
            try
            {
                return File.Exists(Path.Combine(folder, $"level{lvl}", "manifest.json"))
                    && File.Exists(Path.Combine(folder, "stamp.txt")) && File.ReadAllText(Path.Combine(folder, "stamp.txt")) == Stamp(data);
            }
            catch (Exception) { return false; }
        }

        public static bool AllBuilt(string folder, string data)
        {
            try
            {
                if (!File.Exists(Path.Combine(folder, "stamp.txt")) || File.ReadAllText(Path.Combine(folder, "stamp.txt")) != Stamp(data)) return false;
                return File.Exists(Path.Combine(folder, "done.txt"));
            }
            catch (Exception) { return false; }
        }

        public Task Start() => Task.Run(() =>
        {
            try { Build(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { failed = true; error = e.Message; UnityEngine.Debug.LogException(e); }
            finally { done = true; }
        });

        void Build()
        {
            Directory.CreateDirectory(folder);
            string stamp = Stamp(data), stampFile = Path.Combine(folder, "stamp.txt");
            bool same = File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp;
            if (!same)
            {
                // other game files or another generator: the old packs go (only the folders this builder makes)
                foreach (var d in Directory.GetDirectories(folder, "level*")) Directory.Delete(d, true);
                File.Delete(Path.Combine(folder, "done.txt"));
                File.WriteAllText(stampFile, stamp);
            }
            var sched = new Scheduler();
            long ticks = 0;
            sched.OnError = e => { };
            var res = new Resources("", name =>
            {
                var p = Path.Combine(data, name);
                if (!File.Exists(p)) throw new FileNotFoundException(name);
                return Task.FromResult(File.ReadAllBytes(p));
            });
            var engine = new LandsOfLore(res, sched);
            engine.openTalkArchive = name => Task.FromResult<ITalkArchive>(null);
            engine.playIntro = false;
            engine.sfxEnabled = false;
            engine.musicEnabled = false;
            void run(int n) { for (int i = 0; i < n; i += 1) { ticks += 1; sched.Pump(ticks * engine.tickLength); } }
            void key(string k) => sched.Run(() => engine.events.Add(new InputEvent { type = "key", key = k }));
            sched.Start(() => engine.playNewGame(0));
            for (int i = 0; i < 4000 && !(engine.currentLevel == 1 && engine.vcn != null && engine.screen.page(0)[100 * 320 + 200] != 0); i += 1)
            {
                cancel.Token.ThrowIfCancellationRequested();
                run(1);
            }
            for (int lvl = 1; lvl <= LEVELS; lvl += 1)
            {
                cancel.Token.ThrowIfCancellationRequested();
                level = lvl;
                if (LevelBuilt(folder, data, lvl)) continue;
                if (lvl != 1 || engine.currentLevel != 1)
                {
                    int to = lvl;
                    sched.Run(() => engine.queueAsync(() => engine.debugTeleport(to)));
                    // a level can open on something only a key answers: keys while it loads
                    for (int i = 0; i < 600 && engine.currentLevel != lvl; i += 1) { if (i % 20 == 10) key("Enter"); run(1); }
                    run(30);
                }
                if (engine.currentLevel != lvl || engine.vcn == null) { UnityEngine.Debug.Log($"Textured world: level {lvl} did not load"); continue; }
                try { BuildLevel(engine, lvl); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { UnityEngine.Debug.Log($"Textured world: level {lvl} failed: {e.Message}"); }
                levelReady?.Invoke(lvl);
            }
            File.WriteAllText(Path.Combine(folder, "done.txt"), stamp);
            engine.quit = true;
        }

        // ---- one level ----
        sealed class Pic { public string name; public int w, h; public List<(int x, int y, int tile, bool flipped)> cells = new List<(int, int, int, bool)>(); public Rgba img; }

        void BuildLevel(LandsOfLore engine, int lvl)
        {
            var palette = engine.screen.getPalette(0);
            var vcn = engine.vcn;
            var vmp = engine.vmp;
            // the shapes (as export_hd_sources.mjs gathers them)
            var shapes = new List<(Shape shape, string kind)>();
            var seen = new HashSet<string>();
            void add(Shape s, string kind) { if (s != null && s.key != null && s.width > 0 && s.height > 0 && seen.Add(s.key)) shapes.Add((s, kind)); }
            for (int i = 0; i < engine.decorationCount; i += 1) add(engine.getLevelDecorationShapes(i), "decoration");
            foreach (var s in engine.doorShapes ?? new Shape[0]) add(s, "door");
            foreach (var s in engine.monsterShapes ?? new Shape[0]) add(s, "monster");
            foreach (var s in engine.monsterDecorationShapes ?? new Shape[0]) add(s, "monster");
            foreach (var s in engine.itemShapes ?? new Shape[0]) add(s, "item");
            foreach (var s in engine.thrownShapes ?? new Shape[0]) add(s, "thrown");
            var family = shapes.Where(s => s.kind == "decoration").Select(s => s.shape.key.Split('.')[0].ToUpperInvariant()).ToList();
            var theme = THEMES.FirstOrDefault(t => family.Any(n => n.StartsWith(t.Key))).Value ?? DEFAULT_THEME;

            string tmp = Path.Combine(folder, $"level{lvl}.tmp"), dst = Path.Combine(folder, $"level{lvl}");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(Path.Combine(tmp, "shapes"));

            // the tile sheet: every VCN tile, EPX-smoothed (0: see-through)
            int rows = (vcn.tileCount + COLUMNS - 1) / COLUMNS, sw = COLUMNS * 8, sh = rows * 8;
            var sheet = new byte[sw * sh];
            for (int t = 0; t < vcn.tileCount; t += 1) TilePixels(vcn, t, false, sheet, (t % COLUMNS) * 8, (t / COLUMNS) * 8, sw);
            var tiles = Indexed(sheet, sw, sh, palette, null, true);
            var baseAlpha = new byte[tiles.w * tiles.h];
            for (int i = 0; i < baseAlpha.Length; i += 1) baseAlpha[i] = tiles.p[i * 4 + 3] > 127 ? (byte)255 : (byte)0;

            // the pictures (export_hd_walls.mjs): the backdrop and every set's nine views, nearest at the source size
            var pics = new Dictionary<string, Pic>();
            Pic emit(string name, int w, int h, Func<int, int, int> tileAt)
            {
                var px = new byte[w * 8 * h * 8];
                var mask = new bool[w * 8 * h * 8];
                var pic = new Pic { name = name, w = w, h = h };
                for (int y = 0; y < h; y += 1) for (int x = 0; x < w; x += 1)
                {
                    int enc = tileAt(x, y);
                    if (enc == 0 || (enc & 0x3fff) == 0) continue;
                    TilePixels(vcn, enc & 0x3fff, (enc & 0x4000) != 0, px, x * 8, y * 8, w * 8);
                    for (int yy = 0; yy < 8; yy += 1) for (int xx = 0; xx < 8; xx += 1) mask[(y * 8 + yy) * w * 8 + x * 8 + xx] = true;
                    pic.cells.Add((x, y, enc & 0x3fff, (enc & 0x4000) != 0));
                }
                if (pic.cells.Count == 0) return null;
                var img = new Rgba(w * 8, h * 8);
                for (int i = 0; i < px.Length; i += 1)
                {
                    int o = i * 4;
                    if (!mask[i]) { img.p[o] = img.p[o + 1] = img.p[o + 2] = 128; img.p[o + 3] = 255; continue; }
                    int c = px[i] * 3;
                    img.p[o] = (byte)(palette[c] * 255 / 63); img.p[o + 1] = (byte)(palette[c + 1] * 255 / 63); img.p[o + 2] = (byte)(palette[c + 2] * 255 / 63); img.p[o + 3] = 255;
                }
                pic.img = img;
                pics[name] = pic;
                return pic;
            }
            emit("backdrop", 22, 15, (x, y) => vmp[y * 22 + x]);
            int sets = (vmp.Length - 330) / 431;
            for (int m = 1; m <= sets; m += 1)
                for (int v = 0; v < VIEWS.Length; v += 1)
                {
                    int mm = m, off = VIEWS[v][0], vw = VIEWS[v][1];
                    emit($"set{m}_view{v}", vw, VIEWS[v][2], (x, y) => { int i = (mm - 1) * 431 + off + 330 + y * vw + x; return i < vmp.Length ? vmp[i] : 0; });
                }

            void paste(Pic p, Rgba img)
            {
                foreach (var c in p.cells)
                {
                    int tx = (c.tile % COLUMNS) * TS, ty = (c.tile / COLUMNS) * TS;
                    if (ty + TS > tiles.h) continue;
                    for (int y = 0; y < TS; y += 1) for (int x = 0; x < TS; x += 1)
                    {
                        int sx = c.x * TS + (c.flipped ? TS - 1 - x : x), sy = c.y * TS + y;
                        int s = (sy * img.w + sx) * 4, d = ((ty + y) * tiles.w + tx + x) * 4;
                        tiles.p[d] = img.p[s]; tiles.p[d + 1] = img.p[s + 1]; tiles.p[d + 2] = img.p[s + 2];
                        tiles.p[d + 3] = baseAlpha[(ty + y) * tiles.w + tx + x];
                    }
                }
            }
            if (pics.TryGetValue("backdrop", out var back)) paste(back, TextureBackdrop(Smooth4(back.img), theme));
            for (int m = 1; m <= sets; m += 1)
            {
                cancel.Token.ThrowIfCancellationRequested();
                int nearW = pics.TryGetValue($"set{m}_view7", out var np) ? np.w : 16;
                foreach (int v in new[] { 1, 2, 5, 0, 3, 4, 6, 8, 7 })
                {
                    if (!pics.TryGetValue($"set{m}_view{v}", out var p)) continue;
                    double texel = TEXEL * (FRONT.Contains(v) ? (double)p.w / nearW : 1.0);
                    paste(p, TextureFlat(Smooth4(p.img), theme, texel, m));
                }
            }
            WritePng(Path.Combine(tmp, "tiles.png"), tiles);

            // the things that are not alive
            var manifestShapes = new JsonObject();
            foreach (var (s, kind) in shapes)
            {
                cancel.Token.ThrowIfCancellationRequested();
                if (kind == "monster") continue;
                string file = $"shapes/{System.Text.RegularExpressions.Regex.Replace(s.key, "[^A-Za-z0-9]+", "_")}.png";
                var img = Indexed(s.pixels, s.width, s.height, palette, s.colorTable, true);
                double texel = TEXEL * (kind == "item" || kind == "thrown" ? 0.6 : 1.0);
                WritePng(Path.Combine(tmp, file), TextureFlat(img, theme, texel, StableHash(s.key) % 97));
                manifestShapes[s.key] = new JsonObject { ["file"] = file, ["width"] = s.width, ["height"] = s.height, ["kind"] = kind };
            }
            var manifest = new JsonObject
            {
                ["level"] = lvl, ["scale"] = SCALE, ["built"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["tiles"] = new JsonObject { ["columns"] = COLUMNS, ["count"] = vcn.tileCount, ["tile"] = 8 },
                ["shapes"] = manifestShapes,
            };
            File.WriteAllText(Path.Combine(tmp, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            // in place at once: never a half-written pack under the level's name
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.Move(tmp, dst);
        }

        static int StableHash(string s) { unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }

        static void TilePixels(VcnData vcn, int tile, bool flipped, byte[] dst, int ox, int oy, int stride)
        {
            if (tile <= 0 || tile >= vcn.tileCount) return;
            int shift = vcn.shifts[tile];
            for (int y = 0; y < 8; y += 1) for (int x = 0; x < 8; x += 1)
            {
                int sx = flipped ? 7 - x : x;
                int packed = vcn.tiles[tile * 32 + y * 4 + (sx >> 1)];
                int nibble = (sx & 1) != 0 ? packed & 0x0f : packed >> 4;
                dst[(oy + y) * stride + ox + x] = vcn.colorTable[nibble | shift];
            }
        }

        // ---- pictures ----
        public sealed class Rgba { public int w, h; public byte[] p; public Rgba(int w, int h) { this.w = w; this.h = h; p = new byte[w * h * 4]; } }
        sealed class Gray { public int w, h; public byte[] p; public Gray(int w, int h) { this.w = w; this.h = h; p = new byte[w * h]; } }

        /// <summary>An indexed picture (0 see-through) in colour, EPX-smoothed to 4x.</summary>
        static Rgba Indexed(byte[] pixels, int w, int h, byte[] palette, byte[] colorTable, bool zeroClear)
        {
            var img = new Rgba(w, h);
            for (int i = 0; i < w * h; i += 1)
            {
                int raw = pixels[i];
                if (raw == 0 && zeroClear) continue;
                int c = (colorTable != null ? colorTable[raw] : raw) * 3;
                int o = i * 4;
                img.p[o] = (byte)(palette[c] * 255 / 63); img.p[o + 1] = (byte)(palette[c + 1] * 255 / 63); img.p[o + 2] = (byte)(palette[c + 2] * 255 / 63); img.p[o + 3] = 255;
            }
            return Epx(Epx(img));
        }

        static Rgba Smooth4(Rgba img) => Epx(Epx(img));

        static Rgba Epx(Rgba src)
        {
            int w = src.w, h = src.h;
            var o = new Rgba(w * 2, h * 2);
            uint at(int x, int y) { x = Math.Max(0, Math.Min(w - 1, x)); y = Math.Max(0, Math.Min(h - 1, y)); return BitConverter.ToUInt32(src.p, (y * w + x) * 4); }
            void put(int x, int y, uint c) { int i = (y * o.w + x) * 4; o.p[i] = (byte)c; o.p[i + 1] = (byte)(c >> 8); o.p[i + 2] = (byte)(c >> 16); o.p[i + 3] = (byte)(c >> 24); }
            for (int y = 0; y < h; y += 1) for (int x = 0; x < w; x += 1)
            {
                uint p = at(x, y), a = at(x, y - 1), b = at(x + 1, y), c = at(x - 1, y), d = at(x, y + 1);
                put(2 * x, 2 * y, c == a && c != d && a != b ? a : p);
                put(2 * x + 1, 2 * y, a == b && a != c && b != d ? b : p);
                put(2 * x, 2 * y + 1, d == c && d != b && c != a ? c : p);
                put(2 * x + 1, 2 * y + 1, b == d && b != a && d != c ? d : p);
            }
            return o;
        }

        static int L(int r, int g, int b) => (r * 299 + g * 587 + b * 114 + 500) / 1000;

        /// <summary>Gaussian blur (sigma = radius), separable, edges repeated.</summary>
        static float[] Blur(float[] src, int w, int h, double radius)
        {
            int k = (int)Math.Ceiling(radius * 3);
            var kern = new float[2 * k + 1];
            double sum = 0;
            for (int i = -k; i <= k; i += 1) { kern[i + k] = (float)Math.Exp(-i * i / (2 * radius * radius)); sum += kern[i + k]; }
            for (int i = 0; i < kern.Length; i += 1) kern[i] /= (float)sum;
            var tmp = new float[w * h];
            var dst = new float[w * h];
            for (int y = 0; y < h; y += 1) for (int x = 0; x < w; x += 1)
            {
                float a = 0; for (int i = -k; i <= k; i += 1) a += kern[i + k] * src[y * w + Math.Max(0, Math.Min(w - 1, x + i))];
                tmp[y * w + x] = a;
            }
            for (int y = 0; y < h; y += 1) for (int x = 0; x < w; x += 1)
            {
                float a = 0; for (int i = -k; i <= k; i += 1) a += kern[i + k] * tmp[Math.Max(0, Math.Min(h - 1, y + i)) * w + x];
                dst[y * w + x] = a;
            }
            return dst;
        }

        /// <summary>The material's grain: its luminance minus its broad light, centred on 128, strengthened.</summary>
        Gray Grain(string name)
        {
            lock (grains)
            {
                if (grains.TryGetValue(name, out var g)) return g;
                var m = materials.TryGetValue(name, out var mm) ? mm : materials.Values.First();
                var f = new float[m.w * m.h];
                for (int i = 0; i < f.Length; i += 1) f[i] = m.lum[i];
                var blur = Blur(f, m.w, m.h, 10);
                g = new Gray(m.w, m.h);
                for (int i = 0; i < f.Length; i += 1)
                {
                    int hp = Math.Max(0, Math.Min(255, (int)Math.Round(m.lum[i] - blur[i] + 128)));
                    g.p[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(128 + (hp - 128) * 1.6)));
                }
                grains[name] = g;
                return g;
            }
        }

        /// <summary>The grain scaled by texel (area average) and tiled over w x h from (ox, oy).</summary>
        Gray Tiled(string name, int w, int h, double texel, int ox, int oy)
        {
            var g = Grain(name);
            int mw = Math.Max(8, (int)Math.Round(g.w * texel)), mh = Math.Max(8, (int)Math.Round(g.h * texel));
            var small = new Gray(mw, mh);
            double fx = (double)g.w / mw, fy = (double)g.h / mh;
            for (int y = 0; y < mh; y += 1) for (int x = 0; x < mw; x += 1)
            {
                int x0 = (int)(x * fx), x1 = Math.Max(x0 + 1, (int)((x + 1) * fx)), y0 = (int)(y * fy), y1 = Math.Max(y0 + 1, (int)((y + 1) * fy));
                int sum = 0, n = 0;
                for (int yy = y0; yy < y1 && yy < g.h; yy += 1) for (int xx = x0; xx < x1 && xx < g.w; xx += 1) { sum += g.p[yy * g.w + xx]; n += 1; }
                small.p[y * mw + x] = (byte)(n > 0 ? sum / n : 128);
            }
            var o = new Gray(w, h);
            int sx0 = ((ox % mw) + mw) % mw, sy0 = ((oy % mh) + mh) % mh;
            for (int y = 0; y < h; y += 1) for (int x = 0; x < w; x += 1) o.p[y * w + x] = small.p[((y + sy0) % mh) * mw + (x + sx0) % mw];
            return o;
        }

        /// <summary>texture.py classes(): green, warm (wood), the rest; flat grey filler, black and see-through left alone.</summary>
        static (float[] green, float[] warm, float[] rest) Classes(Rgba img)
        {
            int n = img.w * img.h;
            var green = new float[n]; var warm = new float[n]; var rest = new float[n];
            for (int i = 0; i < n; i += 1)
            {
                int r = img.p[i * 4], g = img.p[i * 4 + 1], b = img.p[i * 4 + 2], a = img.p[i * 4 + 3];
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                int s = max == 0 ? 0 : (max - min) * 255 / max;
                double hue = 0;
                if (max != min)
                {
                    double d = max - min;
                    hue = max == r ? (g - b) / d : max == g ? 2 + (b - r) / d : 4 + (r - g) / d;
                    hue = (hue / 6) % 1; if (hue < 0) hue += 1;
                }
                int hh = (int)(hue * 255);
                bool grey = L(Math.Abs(r - 128), Math.Abs(g - 128), Math.Abs(b - 128)) >= 10;
                bool lit = max > 22;
                float bas = grey && lit && a > 0 ? 255 : 0;
                float gr = hh >= 38 && hh <= 125 && s > 55 ? 255 : 0;
                float wa = hh >= 8 && hh <= 30 && s > 120 ? 255 : 0;
                wa = Math.Max(0, wa - gr);
                green[i] = gr * bas / 255; warm[i] = wa * bas / 255;
                rest[i] = Math.Max(0, Math.Max(0, bas - gr) - wa);
            }
            return (Blur(green, img.w, img.h, 1.2), Blur(warm, img.w, img.h, 1.2), Blur(rest, img.w, img.h, 1.2));
        }

        /// <summary>img with the grain soft-lit into it where the mask is (PIL soft_light, blend, composite).</summary>
        static void ApplyGrain(Rgba img, Gray detail, float[] mask)
        {
            for (int i = 0; i < img.w * img.h; i += 1)
            {
                float m = mask[i] / 255f;
                if (m <= 0.001f) continue;
                int d = detail.p[i];
                for (int c = 0; c < 3; c += 1)
                {
                    int a = img.p[i * 4 + c];
                    int sl = ((255 - a) * (a * d) / 65536 + a * (255 - (255 - a) * (255 - d) / 255)) / 255;
                    double lit = a + (sl - a) * STRENGTH;
                    img.p[i * 4 + c] = (byte)Math.Max(0, Math.Min(255, Math.Round(lit * m + a * (1 - m))));
                }
            }
        }

        Rgba TextureFlat(Rgba img, string[] theme, double texel, int seed)
        {
            var (green, warm, rest) = Classes(img);
            ApplyGrain(img, Tiled(theme[0], img.w, img.h, texel, seed * 37, seed * 53), rest);
            ApplyGrain(img, Tiled(theme[3], img.w, img.h, texel, seed * 11, 0), warm);
            ApplyGrain(img, Tiled(theme[4], img.w, img.h, texel * 1.3, seed * 7, seed * 3), green);
            return img;
        }

        /// <summary>texture.py texture_backdrop(): floor and ceiling materials on their planes in perspective.</summary>
        Rgba TextureBackdrop(Rgba img, string[] theme)
        {
            int w = img.w, h = img.h;
            var rows = new double[h];
            for (int y = 0; y < h; y += 1) { long s = 0; for (int x = 0; x < w; x += 1) { int o = (y * w + x) * 4; s += L(img.p[o], img.p[o + 1], img.p[o + 2]); } rows[y] = (double)s / w; }
            int mid = h / 2, horizon = h / 4;
            for (int y = h / 4; y < 3 * h / 4; y += 1) if (rows[y] + Math.Abs(y - mid) * 0.5 < rows[horizon] + Math.Abs(horizon - mid) * 0.5) horizon = y;
            var (_, _, rest) = Classes(img);
            foreach (var (plane, name) in new[] { ("floor", theme[1]), ("ceiling", theme[2]) })
            {
                var src = Grain(name);
                var det = new Gray(w, h);
                for (int i = 0; i < det.p.Length; i += 1) det.p[i] = 128;
                var mask = new float[w * h];
                for (int y = 0; y < h; y += 1)
                {
                    bool inPlane = plane == "floor" ? y >= horizon : y < horizon;
                    if (inPlane) for (int x = 0; x < w; x += 1) mask[y * w + x] = rest[y * w + x];
                    int dy = plane == "floor" ? y - horizon : horizon - y;
                    if (dy <= 2) continue;
                    double z = h * 0.9 / dy, fade = Math.Min(1.0, dy / (h * 0.12));
                    int v = (((int)(z * 260 / TEXEL)) % src.h + src.h) % src.h;
                    for (int x = 0; x < w; x += 1)
                    {
                        int u = (((int)((x - w / 2.0) * z * 0.9 / TEXEL)) % src.w + src.w) % src.w;
                        det.p[y * w + x] = (byte)Math.Round(128 + (src.p[v * src.w + u] - 128) * fade);
                    }
                }
                ApplyGrain(img, det, mask);
            }
            return img;
        }

        // ---- PNG (RGBA 8 bit, zlib by DeflateStream) ----
        public static void WritePng(string file, Rgba img)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                BE(ihdr, 0, img.w); BE(ihdr, 4, img.h); ihdr[8] = 8; ihdr[9] = 6;
                Chunk(ms, "IHDR", ihdr);
                var raw = new byte[(img.w * 4 + 1) * img.h];
                for (int y = 0; y < img.h; y += 1) Buffer.BlockCopy(img.p, y * img.w * 4, raw, y * (img.w * 4 + 1) + 1, img.w * 4);
                using (var z = new MemoryStream())
                {
                    z.WriteByte(0x78); z.WriteByte(0x9c);
                    using (var d = new DeflateStream(z, CompressionLevel.Fastest, true)) d.Write(raw, 0, raw.Length);
                    uint a = 1, b = 0;
                    foreach (byte x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
                    var ad = new byte[4]; BE(ad, 0, (int)((b << 16) | a)); z.Write(ad, 0, 4);
                    Chunk(ms, "IDAT", z.ToArray());
                }
                Chunk(ms, "IEND", new byte[0]);
                string tmp = file + ".part";
                File.WriteAllBytes(tmp, ms.ToArray());
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
        }

        static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n => { uint c = (uint)n; for (int k = 0; k < 8; k += 1) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();

        static void Chunk(Stream s, string type, byte[] body)
        {
            var len = new byte[4]; BE(len, 0, body.Length); s.Write(len, 0, 4);
            var t = Encoding.ASCII.GetBytes(type); s.Write(t, 0, 4); s.Write(body, 0, body.Length);
            uint c = 0xffffffff;
            foreach (byte x in t) c = CrcTable[(c ^ x) & 0xff] ^ (c >> 8);
            foreach (byte x in body) c = CrcTable[(c ^ x) & 0xff] ^ (c >> 8);
            var cb = new byte[4]; BE(cb, 0, (int)(c ^ 0xffffffff)); s.Write(cb, 0, 4);
        }
    }
}
