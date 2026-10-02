// src/platform/full-map.mjs
// Full-screen map overlay: the explored part of the level drawn from the engine's automap data
// (same wall/door knowledge as the original map), with pan/zoom, notes, click-to-walk and a legend.
// C# 9 (Unity compiles this).
//
// `const engine = this.view || this.getEngine()`: the JS reads the same members from either the engine
// or the plain object uiLevelView returns for another level. C# needs one type: `engine` is a LevelView
// (the engine's own state wrapped by Live()), and the members only the engine has (uiLevelExits,
// calcNewBlockPosition, uiInDungeon, uiDungeonMarks, itemsInPlay) come from `live` (null while a
// remembered level is shown, where the JS finds them undefined).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lol;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class FullMap
    {
        static readonly int[][] SIDE_NEIGHBOUR = { new[] { 0, -1, 2 }, new[] { 1, 0, 3 }, new[] { 0, 1, 0 }, new[] { -1, 0, 1 } }; // dx, dy, wall of the neighbour facing us
        // Glyph offsets from the original map (a 7x6 cell): x, y per side, for the 16x16 shape box.
        // Map symbols by automap shape index: bright badges with a letter/arrow, readable on the dark floor
        // (the original parchment glyphs are dark ink and vanish here). Doors are drawn as orange wall bars.
        static readonly Dictionary<int, (string, string)> SYMBOLS = new Dictionary<int, (string, string)>
        {
            [14] = ("N", "#9fd6ff"), [25] = ("up", "#ffe36b"), [26] = ("down", "#ffe36b"), [10] = ("P", "#c6e05f"), [11] = ("X", "#ff5a5a"),
            [15] = ("B", "#f4ead0"), [22] = ("S", "#ff6bd6"), [23] = ("@", "#d9a4ff"), [17] = ("T", "#7fb0ff"), [18] = ("$", "#ffd400"),
        };
        static readonly float[][] SIDE_SHIFT = { new[] { 0f, -0.3f }, new[] { 0.3f, 0f }, new[] { 0f, 0.3f }, new[] { -0.3f, 0f } };

        public static void drawSymbol(Ctx2D ctx, int type, float cx, float cy, float size)
        {
            if (!SYMBOLS.TryGetValue(type, out var sym)) return;
            float r = size / 2;
            ctx.fillStyle = "#000";
            ctx.beginPath(); ctx.arc(cx, cy, r + 1.2f, 0, (float)Math.PI * 2); ctx.fill();
            ctx.fillStyle = sym.Item2;
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, (float)Math.PI * 2); ctx.fill();
            ctx.fillStyle = "#000";
            if (sym.Item1 == "up" || sym.Item1 == "down")
            {
                int d = sym.Item1 == "up" ? -1 : 1; float h = r * 0.6f;
                ctx.beginPath(); ctx.moveTo(cx, cy + d * h); ctx.lineTo(cx + h, cy - d * h * 0.7f); ctx.lineTo(cx - h, cy - d * h * 0.7f); ctx.closePath(); ctx.fill();
                return;
            }
            ctx.font = $"bold {JsRound(size * 0.85)}px system-ui, sans-serif";
            ctx.textAlign = "center"; ctx.textBaseline = "middle";
            ctx.fillText(sym.Item1, cx, cy + size * 0.06f);
        }

        static readonly Dictionary<string, string> COLORS = new Dictionary<string, string>
        {
            ["exit"] = "#ffb457", ["floor"] = "#2b2418", ["floorEdge"] = "#1a150e", ["wall"] = "#d8c68f", ["door"] = "#ff9f43", ["symbol"] = "#5fa8ff",
            ["party"] = "#5fe0ff", ["note"] = "#ffd400", ["pit"] = "#d1a0e0", ["frontier"] = "#5fe0ff", ["item"] = "#9fd66b", ["monster"] = "#ff5a5a",
            ["grid"] = "rgba(255,255,255,.04)",
        };

        /// <summary>sideInfo's result: { symbol, name, door, wall } or { wall: true }.</summary>
        public sealed class SideInfo
        {
            public int? symbol;
            public string name;
            public bool door, wall;
        }

        sealed class Drag
        {
            public float x, y, ox, oy;
            public bool moved;
        }

        sealed class Region
        {
            public string name;
            public int[] levels;
            public int x, y;
        }

        public readonly VisualElement root;
        public readonly Func<bool> getHints;
        public readonly Func<Dictionary<int, VisitedEntry>> getVisited;
        public readonly Action<int> travelTo;
        public readonly Func<LandsOfLore> getEngine;
        public LevelView view; // another level being looked at (see uiLevelView), else null
        public readonly Func<int, List<MapNote>> getNotes;
        public readonly Func<int, Task> addNote;
        public readonly Action<int> walkTo;
        public readonly Action explore;
        public float cell;
        public float[] offset;
        public int hover;

        public VisualElement title, exploreBtn, landsBtn, info, legend, notes, lands, wrap;
        public DomSelect levelSelect;
        public CanvasEl canvas;
        public Ctx2D ctx;

        public FullMap(VisualElement root, Func<LandsOfLore> getEngine, Func<int, List<MapNote>> getNotes, Func<int, Task> addNote, Action<int> walkTo, Action explore,
            Func<Dictionary<int, VisitedEntry>> getVisited = null, Action<int> travelTo = null, Func<bool> getHints = null)
        {
            this.root = root;
            this.getHints = getHints;
            this.getVisited = getVisited ?? (() => new Dictionary<int, VisitedEntry>());
            this.travelTo = travelTo ?? (_ => { });
            this.getEngine = getEngine;
            this.view = null; // another level being looked at (see uiLevelView), else null
            this.getNotes = getNotes;
            this.addNote = addNote;
            this.walkTo = walkTo;
            this.explore = explore;
            this.cell = 20;
            this.offset = new float[] { 0, 0 };
            this.hover = -1;

            this.build();
        }

        VisualElement el(string tag, string cls = null, string text = null) => Dom.El(tag, cls, text);

        /// <summary>The JS `this.view || this.getEngine()`, as one type (see the file header).</summary>
        LevelView source()
        {
            if (this.view != null) return this.view;
            var e = this.getEngine();
            return e == null ? null : Live(e);
        }

        /// <summary>The engine itself, only while the current level is shown (the JS view lacks these members).</summary>
        LandsOfLore live() => this.view == null ? this.getEngine() : null;

        /// <summary>host-ui uiLevelView's `return this` for the current level.</summary>
        static LevelView Live(LandsOfLore e) => new LevelView
        {
            levelBlockProperties = e.levelBlockProperties, wllAutomapData = e.wllAutomapData, monsters = e.monsters, currentLevel = e.currentLevel,
            currentBlock = e.currentBlock, currentDirection = e.currentDirection, findObject = e.findObject, uiFrontier = e.uiFrontier,
            defaultLegendData = e.defaultLegendData, getLangString = e.getLangString, levelName = l => e.levelName(l),
        };

        void build()
        {
            var box = this.el("div", "modal-box map-box");
            var head = this.el("div", "modal-head");
            this.title = this.el("h2", "", "Map");
            var tools = this.el("div", "map-head-tools");
            this.exploreBtn = this.el("button", "", "Nearest unexplored");
            this.exploreBtn.SetAttr("type", "button");
            this.exploreBtn.On("click", () => { this.close(); this.explore(); });
            this.landsBtn = this.el("button", "", "The Lands");
            this.landsBtn.SetAttr("type", "button");
            this.landsBtn.SetTitle("All the regions of the game: where you have been and how to get back");
            this.landsBtn.On("click", () => this.toggleLands());
            this.levelSelect = (DomSelect)Dom.El("select");
            this.levelSelect.AddToClassList("map-level");
            this.levelSelect.SetTitle("Look at the map of another level you have explored");
            this.levelSelect.On("change", () => { _ = this.showLevel(JsNumber(this.levelSelect.value)); });
            tools.Append(this.levelSelect, this.landsBtn);
            var fit = this.el("button", "", "Fit");
            fit.SetAttr("type", "button");
            fit.On("click", () => this.fit());
            var close = this.el("button", "modal-close", "Close");
            close.SetAttr("type", "button");
            close.On("click", () => this.close());
            tools.Append(this.exploreBtn, fit, close);
            head.Append(this.title, tools);
            var body = this.el("div", "map-body");
            this.canvas = (CanvasEl)Dom.El("canvas");
            this.canvas.AddToClassList("map-canvas");
            this.canvas.width = 900;
            this.canvas.height = 620;
            this.ctx = this.canvas.getContext("2d");
            var side = this.el("div", "map-side");
            this.info = this.el("div", "map-info", "Click a cell to walk there. Right-click leaves a note. Drag to pan, wheel to zoom.");
            this.legend = this.el("div", "map-legend");
            side.Append(this.el("h3", "", "Legend"), this.legend, this.el("h3", "", "Notes"), (this.notes = this.el("div", "map-notes")), this.info);
            var wrap = this.el("div", "map-canvas-wrap");
            var zoom = this.el("div", "zoom-buttons");
            var zin = this.el("button", "", "+"); zin.SetAttr("type", "button"); zin.SetTitle("Zoom in");
            var zout = this.el("button", "", "−"); zout.SetAttr("type", "button"); zout.SetTitle("Zoom out");
            zin.On("click", () => this.zoomBy(1.25f));
            zout.On("click", () => this.zoomBy(0.8f));
            zoom.Append(zin, zout);
            wrap.Append(this.canvas, zoom);
            this.lands = this.el("div", "lands-view");
            this.lands.SetHidden(true);
            body.Append(wrap, this.lands, side);
            this.wrap = wrap;
            box.Append(head, body);
            this.root.Append(box);
            this.root.On("click", (DomEvent @event) => { if (@event.target == this.root) this.close(); });
            // interaction
            Drag drag = null;
            this.canvas.On("mousedown", (DomEvent @event) => { if (@event.button == 0) drag = new Drag { x = @event.clientX, y = @event.clientY, ox = this.offset[0], oy = this.offset[1], moved = false }; });
            // window.addEventListener -> the page root
            Dom.document.On("mousemove", (DomEvent @event) =>
            {
                if (drag != null)
                {
                    float dx = @event.clientX - drag.x; float dy = @event.clientY - drag.y;
                    if (Math.Abs(dx) + Math.Abs(dy) > 4) drag.moved = true;
                    this.offset = new[] { drag.ox + dx * this.scale(), drag.oy + dy * this.scale() };
                    this.draw();
                }
                else if (!this.root.IsHidden()) { int b = this.blockAt(@event); if (b != this.hover) { this.hover = b; this.draw(); this.describe(b); } }
            });
            Dom.document.On("mouseup", (DomEvent @event) =>
            {
                if (drag == null) return;
                bool wasClick = !drag.moved;
                drag = null;
                if (wasClick && @event.target == this.canvas && this.view == null) { int b = this.blockAt(@event); if (b >= 0) { this.close(); this.walkTo(b); } }
            });
            this.canvas.On("contextmenu", (DomEvent @event) =>
            {
                @event.preventDefault();
                if (this.view != null) return;
                int b = this.blockAt(@event);
                if (b >= 0) _ = noteThenDraw(b);
            });
            this.canvas.On("wheel", (DomEvent @event) =>
            {
                @event.preventDefault();
                float before = this.cell;
                this.cell = Math.Max(8, Math.Min(48, this.cell * (@event.deltaY > 0 ? 0.85f : 1.18f)));
                // zoom around the pointer
                var rect = this.canvas.worldBound;
                float px = ((@event.clientX - rect.xMin) * this.canvas.width) / rect.width;
                float py = ((@event.clientY - rect.yMin) * this.canvas.height) / rect.height;
                float k = this.cell / before;
                this.offset = new[] { px - (px - this.offset[0]) * k, py - (py - this.offset[1]) * k };
                this.draw();
            }); // { passive: false }
        }

        /// <summary>this.addNote(b).then(() => this.draw())</summary>
        async Task noteThenDraw(int b)
        {
            await this.addNote(b);
            this.draw();
        }

        float scale() => this.canvas.width / this.canvas.worldBound.width;

        // Zoom around the canvas centre (the +/- buttons).
        public void zoomBy(float k)
        {
            float before = this.cell;
            this.cell = Math.Max(8, Math.Min(48, this.cell * k));
            float f = this.cell / before;
            float cx = this.canvas.width / 2f; float cy = this.canvas.height / 2f;
            this.offset = new[] { cx - (cx - this.offset[0]) * f, cy - (cy - this.offset[1]) * f };
            this.draw();
        }

        public void open()
        {
            this.view = null;
            var engine = this.getEngine();
            if (engine == null) return;
            this.root.SetHidden(false);
            this.title.SetText(engine.levelName());
            this.title.SetTitle($"Level {engine.currentLevel}");
            // level picker: every level whose map the party has walked (the automap's temp data)
            var known = new List<int>();
            for (int l = 1; l <= 29; l += 1) if (l == engine.currentLevel || (engine.hasTempDataFlags & (1 << (l - 1))) != 0) known.Add(l);
            this.levelSelect.ClearOptions();
            foreach (int l in known) this.levelSelect.AddOption(l.ToString(CultureInfo.InvariantCulture), engine.levelName(l) + (l == engine.currentLevel ? " (here)" : ""));
            this.levelSelect.value = engine.currentLevel.ToString(CultureInfo.InvariantCulture);
            this.levelSelect.SetHidden(known.Count < 2);
            this.toggleLands(false);
            this.fit();
        }

        public async Task showLevel(int level)
        {
            var engine = this.getEngine();
            if (engine == null) return;
            this.view = level == engine.currentLevel ? null : await engine.uiLevelView(level);
            this.title.SetText(engine.levelName(level) + (this.view != null ? " (remembered)" : ""));
            this.title.SetTitle($"Level {level}");
            this.hover = -1;
            this.fit();
        }

        public void close() { this.root.SetHidden(true); this.hover = -1; this.view = null; }

        // Overworld: the game's regions with every level, visited or not.
        public void toggleLands(bool? show = null)
        {
            bool on = show == null ? this.lands.IsHidden() : show.Value;
            this.lands.SetHidden(!on);
            this.wrap.SetHidden(on);
            this.landsBtn.SetText(on ? "Level map" : "The Lands");
            if (on) this.renderLands();
        }

        // Where each region sits on the world map picture (1000 x 620; tools/gen_world_map.py draws its landmark there).
        static readonly Region[] REGIONS =
        {
            new Region { name = "Gladstone", levels = new[] { 1, 2 }, x = 500, y = 128 },
            new Region { name = "Northland Forest", levels = new[] { 2 }, x = 700, y = 112 },
            new Region { name = "The Southland", levels = new[] { 3, 4, 5 }, x = 470, y = 318 },
            new Region { name = "The Draracle's Caves", levels = new[] { 6, 7, 8, 9 }, x = 270, y = 450 },
            new Region { name = "Opinwood & Gorkha Swamp", levels = new[] { 10, 11, 17 }, x = 205, y = 280 },
            new Region { name = "Urbish & the Mines", levels = new[] { 12, 13, 14, 15, 16 }, x = 225, y = 112 },
            new Region { name = "The White Tower", levels = new[] { 18, 19, 20, 21 }, x = 500, y = 505 },
            new Region { name = "Yvel", levels = new[] { 22, 24 }, x = 720, y = 360 },
            new Region { name = "The Catwalk Caves", levels = new[] { 23, 25 }, x = 855, y = 215 },
            new Region { name = "Castle Cimmeria", levels = new[] { 26, 27, 28, 29 }, x = 840, y = 500 },
        };

        // Unity build: the regions on an ink and parchment map (Resources/UI/worldmap.png), uncharted ones under a
        // cloud; the page draws them as circles in an SVG.
        public void renderLands()
        {
            var engine = source();
            var visited = this.getVisited();
            bool seen(int l) => (visited != null && visited.ContainsKey(l) && visited[l] != null) || l == engine.currentLevel;
            this.lands.Clear();

            // the picture keeps its proportions at whatever width the window gives it
            var map = new VisualElement();
            map.AddToClassList("wm-map");
            map.style.backgroundImage = UnityEngine.Resources.Load<UnityEngine.Texture2D>("UI/worldmap");
            map.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float h = UnityEngine.Mathf.Round(map.layout.width * 0.62f);
                if (!float.IsNaN(h) && UnityEngine.Mathf.Abs(map.resolvedStyle.height - h) > 1) map.style.height = h;
            });
            var fog = UnityEngine.Resources.Load<UnityEngine.Texture2D>("UI/worldfog");
            StyleLength px(float v, float of) => new Length(v * 100 / of, LengthUnit.Percent);
            foreach (var region in REGIONS)
            {
                bool known = region.levels.Any(seen);
                bool here = region.levels.Contains(engine.currentLevel);
                if (!known)
                {
                    var cloud = new VisualElement { pickingMode = PickingMode.Ignore };
                    cloud.AddToClassList("wm-fog");
                    cloud.style.backgroundImage = fog;
                    cloud.style.left = px(region.x - 95, 1000); cloud.style.top = px(region.y - 70, 620);
                    cloud.style.width = px(190, 1000); cloud.style.height = px(152, 620);
                    map.Add(cloud);
                }
                if (here)
                {
                    var ring = new VisualElement { pickingMode = PickingMode.Ignore };
                    ring.AddToClassList("wm-here");
                    ring.style.left = px(region.x - 40, 1000); ring.style.top = px(region.y - 38, 620);
                    ring.style.width = px(80, 1000); ring.style.height = px(80, 620);
                    map.Add(ring);
                }
                var tag = this.el("div", $"wm-region{(known ? " known" : "")}{(here ? " here" : "")}");
                tag.style.left = px(region.x, 1000); tag.style.top = px(region.y + (known ? 32 : 0), 620);
                tag.Append(this.el("div", "wm-name", known ? region.name : "Uncharted"));
                if (known)
                {
                    int walked = region.levels.Where(seen).Count();
                    tag.Append(this.el("div", "wm-sub", here ? "you are here" : $"{walked} of {region.levels.Length} explored"));
                    tag.SetTitle($"{region.name}: {string.Join(", ", region.levels.Where(seen).Select(l => engine.levelName(l)))}");
                    int first = region.levels.First(seen);
                    tag.On("click", () => { this.toggleLands(false); this.levelSelect.value = first.ToString(CultureInfo.InvariantCulture); _ = this.showLevel(first); });
                }
                else tag.SetTitle("Somewhere out there.");
                map.Add(tag);
            }

            var frame = this.el("div", "world-wrap");
            frame.Append(map);
            int knownCount = REGIONS.Count(r => r.levels.Any(seen));
            frame.Append(this.el("p", "world-note", $"{knownCount} of {REGIONS.Length} regions charted. Click a charted region to open its map."));
            this.lands.Append(frame);
        }

        public bool isOpen => !this.root.IsHidden();

        // Bounding box of explored blocks.
        public int[] bounds()
        {
            var engine = source();
            int x0 = 32, y0 = 32, x1 = -1, y1 = -1;
            for (int b = 0; b < 1024; b += 1)
            {
                if ((engine.levelBlockProperties[b].flags & 7) != 7) continue;
                int x = b & 31; int y = b >> 5;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
            if (x1 < 0) { int b = engine.currentBlock; return new[] { b & 31, b >> 5, b & 31, b >> 5 }; }
            return new[] { x0, y0, x1, y1 };
        }

        public void fit()
        {
            var bb = this.bounds();
            int x0 = bb[0], y0 = bb[1], x1 = bb[2], y1 = bb[3];
            int w = x1 - x0 + 3; int h = y1 - y0 + 3;
            this.cell = Math.Max(8, Math.Min(40, (float)Math.Floor(Math.Min((double)this.canvas.width / w, (double)this.canvas.height / h))));
            this.offset = new[] { (this.canvas.width - (x1 + x0 + 1) * this.cell) / 2, (this.canvas.height - (y1 + y0 + 1) * this.cell) / 2 };
            this.draw();
        }

        public int blockAt(DomEvent @event)
        {
            var rect = this.canvas.worldBound;
            float px = ((@event.clientX - rect.xMin) * this.canvas.width) / rect.width;
            float py = ((@event.clientY - rect.yMin) * this.canvas.height) / rect.height;
            double x = Math.Floor((px - this.offset[0]) / this.cell); double y = Math.Floor((py - this.offset[1]) / this.cell);
            if (!(x >= 0 && x <= 31 && y >= 0 && y <= 31)) return -1; // NaN (no layout yet) is outside, as in JS
            return ((int)y << 5) + (int)x;
        }

        // What the map knows about one side of an explored block: "wall", a symbol type, or nothing.
        public SideInfo sideInfo(LevelView engine, int block, int side)
        {
            int x = (block & 31) + SIDE_NEIGHBOUR[side][0]; int y = (block >> 5) + SIDE_NEIGHBOUR[side][1];
            if (x < 0 || x > 31 || y < 0 || y > 31) return null;
            int nb = (y << 5) + x;
            int wall = engine.levelBlockProperties[nb].walls[SIDE_NEIGHBOUR[side][2]];
            if (wall == 0) return null;
            // Original rules: bits 0xc0 = draw a wall line; low 5 bits = legend symbol (13 = door, 31 = none).
            int a = engine.wllAutomapData[wall];
            int type = a & 0x1f;
            bool line = (a & 0xc0) != 0 || type == 13;
            if (type != 0x1f) return new SideInfo { symbol = type, name = this.legendName(engine, type), door = type == 13, wall = line };
            return line ? new SideInfo { wall = true } : null;
        }

        public string legendName(LevelView engine, int type)
        {
            // Legend entries are matched by their shape index, like the original map does.
            var entry = engine.defaultLegendData != null ? engine.defaultLegendData.Find(d => d.shapeIndex == type) : null;
            string name = entry != null ? Regex.Replace(engine.getLangString(entry.stringId) ?? "", "[\x01-\x1f]", "").Trim() : "";
            return name.Length > 0 ? name : "Feature";
        }

        public void draw()
        {
            var engine = source();
            if (engine == null || this.root.IsHidden()) return;
            var seen = this.render(this.ctx, this.canvas.width, this.canvas.height, this.cell, this.offset, this.hover);
            this.renderLegend(engine, seen);
        }

        // Draws the explored level into any canvas context; returns the map symbols met (name -> type).
        public Dictionary<string, int> render(Ctx2D ctx, float width, float height, float cell, float[] offset, int hover = -1)
        {
            float ox = offset[0], oy = offset[1];
            var engine = source();
            var live = this.live();
            const float TAU = (float)(Math.PI * 2);
            ctx.fillStyle = "#0b0d0e";
            ctx.fillRect(0, 0, width, height);
            var seen = new Dictionary<string, int>();
            var glyphs = new List<(int type, int side, float x, float y)>();
            (float, float) at(int b) => (ox + (b & 31) * cell, oy + (b >> 5) * cell);
            // floor
            for (int b = 0; b < 1024; b += 1)
            {
                if ((engine.levelBlockProperties[b].flags & 7) != 7) continue;
                var (x, y) = at(b);
                ctx.fillStyle = b == hover ? "#3a3220" : COLORS["floor"];
                ctx.fillRect(x, y, cell, cell);
                ctx.strokeStyle = COLORS["grid"]; ctx.strokeRect(x + .5f, y + .5f, cell - 1, cell - 1);
            }
            // walls and symbols
            float lw = Math.Max(2, cell / 8);
            for (int b = 0; b < 1024; b += 1)
            {
                if ((engine.levelBlockProperties[b].flags & 7) != 7) continue;
                var (x, y) = at(b);
                for (int side = 0; side < 4; side += 1)
                {
                    var info = this.sideInfo(engine, b, side);
                    if (info == null) continue;
                    var seg = new[] { new[] { x, y, x + cell, y }, new[] { x + cell, y, x + cell, y + cell }, new[] { x, y + cell, x + cell, y + cell }, new[] { x, y, x, y + cell } }[side];
                    if (info.wall)
                    {
                        ctx.lineWidth = lw;
                        ctx.strokeStyle = info.door ? COLORS["door"] : COLORS["wall"];
                        ctx.beginPath(); ctx.moveTo(seg[0], seg[1]); ctx.lineTo(seg[2], seg[3]); ctx.stroke();
                    }
                    if (info.symbol.HasValue && SYMBOLS.ContainsKey(info.symbol.Value)) { seen[info.name] = info.symbol.Value; glyphs.Add((info.symbol.Value, side, x, y)); }
                }
            }
            ctx.lineWidth = 1;
            // symbols on top of the lines, pulled toward their wall; never smaller than readable
            {
                float size = Math.Max(9, cell * 0.62f);
                foreach (var (type, side, x, y) in glyphs) drawSymbol(ctx, type, x + cell / 2 + SIDE_SHIFT[side][0] * cell, y + cell / 2 + SIDE_SHIFT[side][1] * cell, size);
            }
            // frontier (unexplored passages)
            foreach (int b in engine.uiFrontier()) { var (x, y) = at(b); ctx.fillStyle = COLORS["frontier"]; ctx.fillRect(x + cell / 2 - 1.5f, y + cell / 2 - 1.5f, 3, 3); }
            // items and monsters on explored blocks
            // (a remembered level has no itemsInPlay of its own in the JS view; its item ids index the engine's)
            var itemsInPlay = (live ?? this.getEngine()).itemsInPlay;
            for (int b = 0; b < 1024; b += 1)
            {
                if ((engine.levelBlockProperties[b].flags & 7) != 7) continue;
                int o = engine.levelBlockProperties[b].assignedObjects; int guard = 0; int items = 0; int monsters = 0;
                // Only things that are really lying there: an entry pointing at an empty item slot, or one
                // flagged invisible, draws nothing in the scene and must not draw a dot here either.
                while (o != 0 && guard++ < 64)
                {
                    if ((o & 0x8000) != 0) { var m = At(engine.monsters, o & 0x7fff); if (m != null && m.hitPoints > 0 && m.mode < 13) monsters += 1; }
                    else { var it = At(itemsInPlay, o); if (it != null && it.itemPropertyIndex != 0 && (it.shpCurFrame_flg & 0x8000) == 0) items += 1; }
                    o = engine.findObject(o).nextAssignedObject;
                }
                var (x, y) = at(b);
                if (items != 0) { ctx.fillStyle = COLORS["item"]; ctx.beginPath(); ctx.arc(x + cell * .3f, y + cell * .7f, Math.Max(2, cell / 7), 0, TAU); ctx.fill(); }
                if (monsters != 0) { ctx.fillStyle = COLORS["monster"]; ctx.beginPath(); ctx.arc(x + cell * .7f, y + cell * .3f, Math.Max(2, cell / 6), 0, TAU); ctx.fill(); }
            }
            // level exits: explored ones always, the rest only when hints are on
            if (live != null)
            {
                foreach (var ex in live.uiLevelExits())
                {
                    bool explored = (engine.levelBlockProperties[ex.block].flags & 7) == 7 || new[] { 0, 1, 2, 3 }.Any(d => (engine.levelBlockProperties[live.calcNewBlockPosition(ex.block, d)].flags & 7) == 7);
                    if (!explored && !(this.getHints != null && this.getHints())) continue;
                    var (x, y) = at(ex.block);
                    float size = Math.Max(10, cell * 0.7f);
                    ctx.globalAlpha = explored ? 1 : 0.45f;
                    ctx.fillStyle = "#000"; ctx.beginPath(); ctx.arc(x + cell / 2, y + cell / 2, size / 2 + 1.2f, 0, TAU); ctx.fill();
                    ctx.fillStyle = COLORS["exit"]; ctx.beginPath(); ctx.arc(x + cell / 2, y + cell / 2, size / 2, 0, TAU); ctx.fill();
                    ctx.fillStyle = "#000"; ctx.font = $"bold {JsRound(size * 0.8)}px system-ui, sans-serif"; ctx.textAlign = "center"; ctx.textBaseline = "middle";
                    ctx.fillText(ex.level != 0 ? ex.level.ToString(CultureInfo.InvariantCulture) : "→", x + cell / 2, y + cell / 2 + size * 0.05f);
                    ctx.globalAlpha = 1;
                    seen[ex.level != 0 ? $"Exit to {engine.levelName(ex.level)}" : "Exit"] = -1;
                }
            }
            // the imp's pit: the vault, its levers and whatever the floor asks you to find
            if (live != null && live.uiInDungeon() && this.view == null)
            {
                foreach (var mark in live.uiDungeonMarks())
                {
                    var (x, y) = at(mark.block);
                    bool done = mark.kind.Contains("pulled") || mark.kind.Contains("open");
                    ctx.fillStyle = done ? COLORS["frontier"] : COLORS["pit"];
                    ctx.beginPath();
                    ctx.arc(x + cell / 2, y + cell / 2, Math.Max(3, cell / 4), 0, TAU);
                    ctx.fill();
                    ctx.strokeStyle = "#1a150e";
                    ctx.lineWidth = 1;
                    ctx.stroke();
                    seen[mark.kind] = -1;
                }
            }
            // notes
            foreach (var note in this.getNotes(source().currentLevel)) { var (x, y) = at(note.block); ctx.fillStyle = COLORS["note"]; ctx.beginPath(); ctx.arc(x + cell / 2, y + cell / 2, Math.Max(3, cell / 5), 0, TAU); ctx.fill(); }
            // party
            if (engine.currentBlock < 0) return seen;
            var (px, py) = at(engine.currentBlock);
            float cx = px + cell / 2; float cy = py + cell / 2; int dir = engine.currentDirection;
            int dx = new[] { 0, 1, 0, -1 }[dir]; int dy = new[] { -1, 0, 1, 0 }[dir];
            ctx.strokeStyle = COLORS["party"]; ctx.fillStyle = COLORS["party"]; ctx.lineWidth = Math.Max(2, cell / 8);
            ctx.beginPath(); ctx.arc(cx, cy, Math.Max(3, cell / 4), 0, TAU); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(cx + dx * cell * .45f, cy + dy * cell * .45f); ctx.stroke();
            ctx.lineWidth = 1;
            return seen;
        }

        string legendKey, notesKey;

        public void renderLegend(LevelView engine, Dictionary<string, int> seen)
        {
            // only when what it lists changed: rebuilt on every redraw it showed its rows a frame without their
            // pictures, the names jumping left and back while the map was dragged or zoomed
            string legendKey = string.Join("|", seen.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}={e.Value}"));
            if (legendKey != this.legendKey || this.legend.childCount == 0)
            {
            this.legendKey = legendKey;
            var items = new[] { ("Wall", COLORS["wall"]), ("You", COLORS["party"]), ("Unexplored passage", COLORS["frontier"]), ("Item on the floor", COLORS["item"]), ("Monster", COLORS["monster"]), ("Note", COLORS["note"]), ("Pit: what to find", COLORS["pit"]) };
            var rows = new List<VisualElement>();
            foreach (var (name, color) in items) { var row = this.el("div", "map-legend-row"); var sw = this.el("i"); sw.SetStyle("background", color); row.Append(sw, this.el("span", "", name)); rows.Add(row); }
            foreach (var entry in seen.OrderBy(e => e.Key, StringComparer.CurrentCulture))
            {
                string name = entry.Key; int type = entry.Value;
                var row = this.el("div", "map-legend-row");
                var sw = (CanvasEl)Dom.El("canvas");
                sw.AddToClassList("map-legend-glyph");
                sw.width = sw.height = 18;
                if (type == -1) { var c = sw.getContext("2d"); c.fillStyle = COLORS["exit"]; c.beginPath(); c.arc(9, 9, 7, 0, (float)Math.PI * 2); c.fill(); c.fillStyle = "#000"; c.font = "bold 10px system-ui, sans-serif"; c.textAlign = "center"; c.textBaseline = "middle"; c.fillText("→", 9, 9.5f); }
                else drawSymbol(sw.getContext("2d"), type, 9, 9, 13);
                row.Append(sw, this.el("span", "", name));
                rows.Add(row);
            }
            this.legend.ReplaceChildren(rows);
            }
            var notes = this.getNotes(source().currentLevel);
            string notesKey = string.Join("|", notes.Select(n => $"{n.block}:{n.text}"));
            if (notesKey == this.notesKey && this.notes.childCount > 0) return;
            this.notesKey = notesKey;
            if (notes.Count > 0)
            {
                this.notes.ReplaceChildren(notes.Select(n =>
                {
                    var row = this.el("div", "map-note");
                    row.SetText($"{n.block}: {n.text}");
                    row.SetTitle("Click to walk there");
                    row.On("click", () => { this.close(); this.walkTo(n.block); });
                    return row;
                }).ToList());
            }
            else this.notes.ReplaceChildren(this.el("p", "panel-note", "No notes on this level. Right-click a cell to add one."));
        }

        public void describe(int b)
        {
            var engine = source();
            if (b < 0 || engine == null || (engine.levelBlockProperties[b].flags & 7) != 7) { this.info.SetText(this.view != null ? "A remembered level: what you explored there. Drag to pan, wheel to zoom." : "Click a cell to walk there. Right-click leaves a note. Drag to pan, wheel to zoom."); return; }
            var parts = new List<string> { $"Block {b} ({b & 31}, {b >> 5})" };
            for (int side = 0; side < 4; side += 1) { var info = this.sideInfo(engine, b, side); if (info != null && info.symbol.HasValue) parts.Add($"{"NESW"[side]}: {info.name}"); }
            var note = this.getNotes(source().currentLevel).Find(n => n.block == b);
            if (note != null) parts.Add($"Note: {note.text}");
            this.info.SetText(string.Join(" · ", parts));
        }

        // ---- JS helpers ----
        static T At<T>(T[] a, int i) where T : class => a != null && i >= 0 && i < a.Length ? a[i] : null;
        /// <summary>Math.round (half up).</summary>
        static int JsRound(double v) => (int)Math.Floor(v + 0.5);
        static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
        /// <summary>Number(s) for a select value (NaN -> -1, never a level).</summary>
        static int JsNumber(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
    }
}
