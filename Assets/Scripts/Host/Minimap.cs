// src/platform/minimap.mjs
// Live minimap overlay: draws the explored automap cells around the party on a small canvas.
// Uses the same data as the in-game automap (block flags & 7 = explored, wllAutomapData for walls/doors).
// C# 9 (Unity compiles this).
using System;
using Lol;

namespace LolHost
{
    public sealed class Minimap
    {
        const int DEFAULT_RADIUS = 6; // cells visible on each side of the party (wheel on the canvas zooms)

        public readonly CanvasEl canvas;
        public readonly Ctx2D ctx;
        public bool visible;
        public int radius;
        public string lastKey;
        public double lastDraw;
        /// <summary>The page's own map style (main.mjs: fullMap.render): (ctx, w, h, cell, [ox, oy]).</summary>
        public Action<Ctx2D, int, int, int, float[]> renderer;
        /// <summary>Markers drawn over the map after each draw: (map, engine).</summary>
        public Action<Minimap, LandsOfLore> afterDraw;
        public Action<int> onRadius;

        public Minimap(CanvasEl canvas)
        {
            this.canvas = canvas;
            this.ctx = canvas.getContext("2d");
            this.visible = true;
            this.radius = DEFAULT_RADIUS;
            this.lastKey = "";
            canvas.On("wheel", (DomEvent @event) =>
            {
                @event.preventDefault();
                this.setRadius(this.radius + (@event.deltaY > 0 ? 1 : -1));
            }); // { passive: false }
            this.lastDraw = 0;
        }

        // Returns true when the party owns the magic map and the minimap is enabled.
        public bool active(LandsOfLore engine)
        {
            return this.visible && (engine.flagsTable[31] & 0x10) != 0 && (engine.updateFlags & 4) == 0;
        }

        public void setRadius(int radius)
        {
            this.radius = Math.Max(3, Math.Min(12, radius));
            this.lastKey = "";
            if (this.onRadius != null) this.onRadius(this.radius);
        }

        // Block under a mouse event on the canvas, or -1 outside the map.
        public int blockAt(LandsOfLore engine, DomEvent @event)
        {
            var rect = this.canvas.worldBound;
            double cx = ((@event.clientX - rect.xMin) * this.canvas.width) / rect.width;
            double cy = ((@event.clientY - rect.yMin) * this.canvas.height) / rect.height;
            int cw = this.renderer != null ? 12 : 7; int chh = this.renderer != null ? 12 : 6;
            int x = (engine.currentBlock & 0x1f) + (int)Math.Floor(cx / cw) - this.radius;
            int y = (engine.currentBlock >> 5) + (int)Math.Floor(cy / chh) - this.radius;
            if (x < 0 || x > 31 || y < 0 || y > 31) return -1;
            return (y << 5) + x;
        }

        public void draw(LandsOfLore engine)
        {
            bool on = this.active(engine);
            this.canvas.SetHidden(!on);
            if (!on) return;
            string key = $"{engine.currentLevel}:{engine.currentBlock}:{engine.currentDirection}:{engine.levelBlockProperties[engine.currentBlock].flags}:{this.radius}";
            double now = Web.now();
            if (key == this.lastKey && now - this.lastDraw < (this.renderer != null ? 1000 : 250)) return; // doors opening etc. show up within a tick
            this.lastKey = key;
            this.lastDraw = now;
            if (this.renderer != null) { // the page's own map style (shared with the full map)
                int cells = this.radius * 2 + 1;
                int cell = 12;
                int size = cells * cell;
                if (this.canvas.width != size || this.canvas.height != size) { this.canvas.width = size; this.canvas.height = size; }
                float ox = size / 2f - ((engine.currentBlock & 31) + 0.5f) * cell;
                float oy = size / 2f - ((engine.currentBlock >> 5) + 0.5f) * cell;
                this.renderer(this.ctx, size, size, cell, new[] { ox, oy });
                if (this.afterDraw != null) this.afterDraw(this, engine);
                return;
            }
            // The engine draws the map with its own parchment, wall and door graphics; blit that region here.
            var region = engine.drawMinimap(this.radius);
            if (this.canvas.width != region.w || this.canvas.height != region.h)
            {
                this.canvas.width = region.w;
                this.canvas.height = region.h;
            }
            var page = engine.screen.page(region.page);
            var image = this.ctx.createImageData(region.w, region.h);
            var pal = region.palette;
            for (int y = 0; y < region.h; y += 1)
            {
                for (int x = 0; x < region.w; x += 1)
                {
                    int c = page[(region.y + y) * 320 + region.x + x] * 3;
                    int o = (y * region.w + x) * 4;
                    image.data[o] = Clamped((pal[c] * 255) / 63.0); // 6-bit VGA palette
                    image.data[o + 1] = Clamped((pal[c + 1] * 255) / 63.0);
                    image.data[o + 2] = Clamped((pal[c + 2] * 255) / 63.0);
                    image.data[o + 3] = 255;
                }
            }
            this.ctx.putImageData(image, 0, 0);
            if (this.afterDraw != null) this.afterDraw(this, engine);
        }

        /// <summary>A Uint8ClampedArray store: clamp, round half to even.</summary>
        static byte Clamped(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

        // Canvas centre of a block, or null when it is outside the drawn window.
        public float[] cellCenter(LandsOfLore engine, int block)
        {
            int dx = (block & 0x1f) - (engine.currentBlock & 0x1f);
            int dy = (block >> 5) - (engine.currentBlock >> 5);
            if (Math.Abs(dx) > this.radius || Math.Abs(dy) > this.radius) return null;
            if (this.renderer != null) return new float[] { (dx + this.radius) * 12 + 6, (dy + this.radius) * 12 + 6 };
            return new float[] { (dx + this.radius) * 7 + 3.5f, (dy + this.radius) * 6 + 3 };
        }
    }
}
