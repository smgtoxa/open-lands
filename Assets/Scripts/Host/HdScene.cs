// src/platform/hd-scene.mjs, ported 1:1. C# 9.
// HD scene renderer: replays the engine's last scene draw (wall tile grid + shape draws, see
// LolEngine.hdFrame) with high-resolution PNG assets from an HD pack (private/hd/level<N>/,
// produced by scripts/export_hd_sources.mjs and replaced with custom art).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Lol;
using UnityEngine;

namespace LolHost
{
    public static class HdImages
    {
        // new Image() + src: the file is read on a worker thread, then decoded on the main thread
        // (decode) once the read is done. A missing or broken file decodes to null (onerror).
        public static Task<byte[]> read(string file) => Task.Run(() => File.Exists(file) ? File.ReadAllBytes(file) : null);

        public static Texture2D decode(Task<byte[]> read)
        {
            if (read.IsFaulted || read.Result == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            return tex.LoadImage(read.Result) ? tex : null;
        }

        /// <summary>await loadImage(url): the file read off the main thread, the page's loop waiting a frame at a time.</summary>
        public static async Task<Texture2D> loadImage(Scheduler sched, string file)
        {
            var reading = read(file);
            while (!reading.IsCompleted) await sched.Sleep(0);
            return decode(reading);
        }
    }

    /// <summary>manifest.json of a pack: { level, scale, built, tiles: { columns, count, tile }, shapes: { key: { file, width, height, kind } } }</summary>
    public sealed class HdManifest
    {
        public int level, scale;
        public double built;
        public int tilesColumns, tilesTile;
        public JsonObject shapes;
    }

    public sealed class HdPack
    {
        public readonly string baseUrl;
        readonly Scheduler sched;
        public HdManifest manifest;
        public string stamp = "";
        public Texture2D tiles;
        // key -> Texture2D | null (missing) | Task<byte[]> (loading)
        public readonly Dictionary<string, object> shapes = new Dictionary<string, object>();
        public bool ready;

        /// <summary>baseUrl: the pack's folder on disk (null when there is no HD folder: load() fails, as a 404 would).</summary>
        public HdPack(string baseUrl, Scheduler sched)
        {
            this.baseUrl = baseUrl;
            this.sched = sched;
        }

        public async Task<bool> load()
        {
            try
            {
                string file = baseUrl == null ? null : Path.Combine(baseUrl, "manifest.json");
                if (file == null || !File.Exists(file)) return false; // !response.ok
                var reading = HdImages.read(file);
                while (!reading.IsCompleted) await sched.Sleep(0);
                var json = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(reading.Result)).AsObject();
                var t = json["tiles"].AsObject();
                manifest = new HdManifest
                {
                    level = (int)json["level"], scale = (int)json["scale"], built = json["built"] != null ? (double)json["built"] : 0,
                    tilesColumns = (int)t["columns"], tilesTile = (int)t["tile"], shapes = json["shapes"]?.AsObject() ?? new JsonObject(),
                };
                // stamp: the browser's cache-buster (?v=built); files on disk are read fresh, so it is unused
                stamp = manifest.built != 0 ? $"?v={manifest.built}" : "";
                tiles = await HdImages.loadImage(sched, Path.Combine(baseUrl, "tiles.png"));
                ready = tiles != null;
            }
            catch (Exception)
            {
                ready = false;
            }
            return ready;
        }

        /// <summary>The picture for a shape key: Texture2D, null (not in the pack / broken), or NotLoaded while it loads (JS undefined).</summary>
        public object shape(string key)
        {
            if (!shapes.ContainsKey(key))
            {
                var entry = manifest.shapes[key];
                if (entry == null)
                {
                    shapes[key] = null;
                }
                else
                {
                    string rel = (string)entry["file"];
                    shapes[key] = HdImages.read(Path.Combine(baseUrl, rel.Replace('/', Path.DirectorySeparatorChar)));
                }
            }
            var value = shapes[key];
            if (value is Task<byte[]> promise)
            {
                if (!promise.IsCompleted) return NotLoaded;
                value = shapes[key] = HdImages.decode(promise); // .then((image) => { this.shapes.set(key, image); })
            }
            return value;
        }

        /// <summary>JS undefined: the picture is still loading.</summary>
        public static readonly object NotLoaded = new object();
    }

    public sealed class HdScene
    {
        const int SCENE_X = 112;
        const int SCENE_W = 176;
        const int SCENE_H = 120;

        public readonly CanvasEl canvas;
        public readonly Ctx2D ctx;
        public HdPack pack;
        public int serial = -1;
        public int pending;
        CanvasEl tileCanvas;
        int tileHash;

        /// <summary>the screen palette (6-bit), for the sprites the pack lacks</summary>
        public Func<byte[]> palette;
        Texture2D origTex;

        // A sprite the pack has no picture for (creatures, in the textured world) is drawn from the pixels the
        // engine itself painted for it, scaled up to the pack: exactly the original, colour variant and all.
        void drawOriginal(DrawShapeRecord op)
        {
            if (op.painted == null || op.painted.Count == 0 || palette == null) return;
            var pal = palette();
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            foreach (int i in op.painted) { int x = i % 320, y = i / 320; if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var px = new Color32[w * h];
            for (int k = 0; k < op.painted.Count; k += 1)
            {
                int i = op.painted[k], x = i % 320 - x0, y = i / 320 - y0, c = op.colors[k] * 3;
                px[(h - 1 - y) * w + x] = new Color32((byte)(pal[c] * 255 / 63), (byte)(pal[c + 1] * 255 / 63), (byte)(pal[c + 2] * 255 / 63), 255);
            }
            if (origTex == null || origTex.width != w || origTex.height != h)
            {
                if (origTex != null) UnityEngine.Object.Destroy(origTex);
                origTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            origTex.SetPixels32(px);
            origTex.Apply(false);
            int s = pack.manifest.scale;
            bool smooth = ctx.imageSmoothingEnabled;
            ctx.imageSmoothingEnabled = false;
            ctx.drawImage(origTex, (x0 - SCENE_X) * s, y0 * s, w * s, h * s);
            ctx.imageSmoothingEnabled = smooth;
        }

        public HdScene(CanvasEl canvas)
        {
            this.canvas = canvas;
            ctx = canvas.getContext("2d");
            pack = null;
            serial = -1;
            pending = 0;
        }

        // Draws frame (engine.hdFrame) at the pack's scale. Returns false when the frame could not be
        // rendered (no pack / not loaded yet) so the caller keeps the original scene visible.
        public bool draw(HdFrame frame, bool force = false)
        {
            var pack = this.pack;
            if (pack == null || !pack.ready || frame == null || frame.level != pack.manifest.level) return false;
            if (frame.serial == serial && !force && pending == 0) return true;
            int s = pack.manifest.scale;
            int w = SCENE_W * s;
            int h = SCENE_H * s;
            if (canvas.width != w || canvas.height != h)
            {
                canvas.width = w;
                canvas.height = h;
            }
            var ctx = this.ctx;
            ctx.imageSmoothingEnabled = true;
            ctx.imageSmoothingQuality = "high";
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            ctx.filter = "none";
            ctx.globalAlpha = 1;
            ctx.fillStyle = "#000";
            ctx.fillRect(0, 0, w, h);
            // 1. wall tiles: 22x15 cells of 8x8, base tile then an optional transparent overlay tile
            int columns = pack.manifest.tilesColumns; int tile = pack.manifest.tilesTile;
            int ts = tile * s;
            // the wall layer is cached: a monster stepping into view only changes the shape list
            int hash = 0; for (int i = 0; i < 660; i += 1) hash = unchecked(hash * 31 + frame.tiles[i]);
            if (tileCanvas == null || tileHash != hash || tileCanvas.width != w || tileCanvas.height != h)
            {
                if (tileCanvas == null) tileCanvas = (CanvasEl)Dom.El("canvas");
                tileCanvas.width = w; tileCanvas.height = h;
                var tctx = tileCanvas.getContext("2d");
                tctx.imageSmoothingEnabled = false;
                void blitTo(int index, int cx, int cy)
                {
                    bool flipped = (index & 0x4000) != 0; int t = index & 0x3fff; if (t == 0) return;
                    int sx = (t % columns) * ts; int sy = (t / columns) * ts;
                    if (flipped) { tctx.save(); tctx.translate((cx + 1) * ts, cy * ts); tctx.scale(-1, 1); tctx.drawImage(pack.tiles, sx, sy, ts, ts, 0, 0, ts, ts); tctx.restore(); }
                    else tctx.drawImage(pack.tiles, sx, sy, ts, ts, cx * ts, cy * ts, ts, ts);
                }
                for (int cell = 0; cell < 330; cell += 1)
                {
                    int t = frame.tiles[cell]; int overlay = 0;
                    if ((t & 0x8000) != 0) { overlay = t & 0x7fff; t = 0; }
                    if (t == 0) t = frame.tiles[cell + 330];
                    blitTo(t, cell % 22, cell / 22);
                    if (overlay != 0) blitTo(overlay, cell % 22, cell / 22);
                }
                tileHash = hash;
            }
            ctx.drawImage(tileCanvas, 0, 0);
            // 2. shapes (decorations, doors, monsters, items) in the engine's draw order
            pending = 0;
            foreach (var op in frame.ops)
            {
                if (string.IsNullOrEmpty(op.key)) continue;
                var image = pack.shape(op.key);
                if (image == HdPack.NotLoaded) { pending += 1; continue; } // still loading: redraw next frame
                if (image == null) { drawOriginal(op); continue; } // not in the pack (a creature): its original pixels
                ctx.save();
                ctx.beginPath();
                ctx.rect((op.clip[0] - SCENE_X) * s, op.clip[1] * s, (op.clip[2] - op.clip[0]) * s, (op.clip[3] - op.clip[1]) * s);
                ctx.clip();
                if (op.fade != 0) ctx.filter = $"brightness({Math.Max(0.25, 1 - op.fade * 0.22).ToString(CultureInfo.InvariantCulture)})";
                if (op.ppc == 16 || op.ppc == 20 || op.ppc == 21 || op.ppc == 48 || op.ppc == 52) ctx.globalAlpha = 0.6f;
                int dx = (op.x - SCENE_X) * s;
                int dy = op.y * s;
                int dw = op.w * s;
                int dh = op.h * s;
                ctx.translate(dx + (op.xFlip ? dw : 0), dy + (op.yFlip ? dh : 0));
                ctx.scale(op.xFlip ? -1 : 1, op.yFlip ? -1 : 1);
                ctx.drawImage((Texture2D)image, 0, 0, dw, dh);
                ctx.restore();
            }
            serial = frame.serial;
            return true;
        }
    }
}
