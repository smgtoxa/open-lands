// Copied from the web project (scripts/), writing to GameData/textured/base instead of private/hd (tools/textured/README).
// Exports the original graphics of one level as PNG sources for an HD asset pack.
// Usage: node scripts/export_hd_sources.mjs [level] [scale] [plain]
// By default the graphics are smoothed with Scale2x/EPX (a pixel-art scaler) so the pack looks
// less blocky out of the box; pass "plain" for a nearest-neighbour export to paint over.
// Output: private/hd/level<N>/ with tiles.png (wall tile sheet), shapes/*.png and manifest.json.
// Replace the PNGs with your own art at the same *logical* size times `scale` (default 4x) and
// enable "HD assets" in the Settings menu; missing files fall back to the original graphics.
import fs from "fs";
import path from "path";
const { LandsOfLore, Resources } = await import((process.env.LANDS || process.env.HOME + "/lands") + "/src/game/lol.mjs");
const { writePngRgba } = await import((process.env.LANDS || process.env.HOME + "/lands") + "/scripts/png.mjs");
const { xbrUpscale4 } = await import((process.env.LANDS || process.env.HOME + "/lands") + "/scripts/hd/xbr.mjs");

const level = Number(process.argv[2] || 1);
const scale = Number(process.argv[3] || 4);
// "xbr" (default at 4x): xBR, an edge-directed pixel-art scaler; "smooth": Scale2x/EPX;
// "plain": nearest neighbour, to paint over.
const mode = process.argv[4] || (scale === 4 ? "xbr" : "smooth");
const smooth = mode === "smooth" && (scale === 2 || scale === 4 || scale === 8);
const useXbr = mode === "xbr" && scale === 4;
const dataRoot = process.env.LOL_DATA || path.resolve("GameData/DATA");
const outDir = path.resolve(process.env.LOL_OUT || "GameData/textured/base", `level${level}`);
fs.mkdirSync(path.join(outDir, "shapes"), { recursive: true });

const resources = new Resources("", async (name) => new Uint8Array(fs.readFileSync(path.join(dataRoot, name))));
const engine = new LandsOfLore({ resources, log: () => {} });
engine.audioContext = () => null;
engine.openTalkArchive = async () => null;

const TILE_COLUMNS = 32;

function rgb(palette, index) {
  return [(palette[index * 3] * 255) / 63, (palette[index * 3 + 1] * 255) / 63, (palette[index * 3 + 2] * 255) / 63];
}

// Scale2x (EPX): doubles an indexed image, extending diagonal edges instead of blocking them.
function scale2x(src, w, h) {
  const out = new Uint8Array(w * 2 * h * 2);
  const at = (x, y) => src[Math.min(h - 1, Math.max(0, y)) * w + Math.min(w - 1, Math.max(0, x))];
  for (let y = 0; y < h; y += 1) {
    for (let x = 0; x < w; x += 1) {
      const p = at(x, y);
      const a = at(x, y - 1);
      const b = at(x + 1, y);
      const c = at(x - 1, y);
      const d = at(x, y + 1);
      let e0 = p; let e1 = p; let e2 = p; let e3 = p;
      if (c === a && c !== d && a !== b) e0 = a;
      if (a === b && a !== c && b !== d) e1 = b;
      if (d === c && d !== b && c !== a) e2 = c;
      if (b === d && b !== a && d !== c) e3 = d;
      const o = (y * 2) * (w * 2) + x * 2;
      out[o] = e0; out[o + 1] = e1; out[o + w * 2] = e2; out[o + w * 2 + 1] = e3;
    }
  }
  return out;
}

// Upscales an indexed image (0 = transparent) to RGBA at the export scale.
function toRgba(pixels, width, height, palette, colorTable) {
  if (useXbr) {
    const flat = new Uint8Array(width * height * 4);
    for (let i = 0; i < width * height; i += 1) {
      const raw = pixels[i];
      if (!raw) continue;
      const [r, g, b] = rgb(palette, colorTable ? colorTable[raw] : raw);
      flat[i * 4] = r; flat[i * 4 + 1] = g; flat[i * 4 + 2] = b; flat[i * 4 + 3] = 255;
    }
    return xbrUpscale4(flat, width, height);
  }
  let src = pixels;
  let sw = width;
  let step = 1;
  if (smooth) {
    while (step < scale) { src = scale2x(src, sw, height * step); sw *= 2; step *= 2; }
  }
  const out = new Uint8Array(width * scale * height * scale * 4);
  for (let y = 0; y < height * scale; y += 1) {
    for (let x = 0; x < width * scale; x += 1) {
      const raw = src[Math.floor(y / (scale / step)) * sw + Math.floor(x / (scale / step))];
      const o = (y * width * scale + x) * 4;
      if (!raw) continue;
      const [r, g, b] = rgb(palette, colorTable ? colorTable[raw] : raw);
      out[o] = r; out[o + 1] = g; out[o + 2] = b; out[o + 3] = 255;
    }
  }
  return out;
}

function exportTiles(palette) {
  const vcn = engine.vcn;
  const rows = Math.ceil(vcn.tileCount / TILE_COLUMNS);
  const w = TILE_COLUMNS * 8;
  const h = rows * 8;
  const pixels = new Uint8Array(w * h);
  for (let tile = 0; tile < vcn.tileCount; tile += 1) {
    const tx = (tile % TILE_COLUMNS) * 8;
    const ty = Math.floor(tile / TILE_COLUMNS) * 8;
    const shift = vcn.shifts[tile];
    for (let y = 0; y < 8; y += 1) {
      for (let x = 0; x < 8; x += 1) {
        const packed = vcn.tiles[tile * 32 + y * 4 + (x >> 1)];
        const nibble = x & 1 ? packed & 0x0f : packed >> 4;
        pixels[(ty + y) * w + tx + x] = vcn.colorTable[nibble | shift];
      }
    }
  }
  writePngRgba(path.join(outDir, "tiles.png"), w * scale, h * scale, toRgba(pixels, w, h, palette, null));
  return { columns: TILE_COLUMNS, count: vcn.tileCount, tile: 8 };
}

function exportShape(shape, palette, name) {
  if (!shape || !shape.width || !shape.height) return null;
  const file = `shapes/${name}.png`;
  writePngRgba(path.join(outDir, file), shape.width * scale, shape.height * scale, toRgba(shape.pixels, shape.width, shape.height, palette, shape.colorTable));
  return { file, width: shape.width, height: shape.height };
}

async function main() {
  const run = engine.playNewGame(0).catch((error) => { if (error.message !== "quit") throw error; });
  const started = Date.now();
  while (Date.now() - started < 30000 && !(engine.currentLevel === 1 && engine.vcn && engine.screen.page(0)[100 * 320 + 200])) await new Promise((r) => setTimeout(r, 100));
  if (level !== 1) {
    // Only the graphics are wanted: level scripts may open a cutscene that waits for a click.
    await engine.debugTeleport(level);
    await new Promise((r) => setTimeout(r, 500));
  }
  const palette = engine.screen.getPalette(0);
  const shapes = {};
  const safe = (key) => key.replace(/[^A-Za-z0-9]+/g, "_");
  // Level decorations (whole SHP file) and doors.
  for (let i = 0; i < engine.decorationCount; i += 1) {
    const shape = engine.getLevelDecorationShapes(i);
    const info = exportShape(shape, palette, safe(shape.key));
    if (info) shapes[shape.key] = { ...info, kind: "decoration" };
  }
  const add = (shape, kind) => { if (shape && shape.key && !shapes[shape.key]) { const info = exportShape(shape, palette, safe(shape.key)); if (info) shapes[shape.key] = { ...info, kind }; } };
  for (const shape of engine.doorShapes) add(shape, "door");
  // Monsters of this level (all 16 animation frames per type) and their equipment.
  for (const shape of engine.monsterShapes) add(shape, "monster");
  for (const shape of engine.monsterDecorationShapes) add(shape, "monster");
  // Items lying around and thrown objects.
  for (const shape of engine.itemShapes) add(shape, "item");
  for (const shape of engine.thrownShapes) add(shape, "thrown");
  const tiles = exportTiles(palette);
  // `built` is the cache buster: the pack files keep their names, so without it a browser that
  // already has tiles.png or a shape keeps showing the art from a previous export.
  fs.writeFileSync(path.join(outDir, "manifest.json"), JSON.stringify({ level, scale, built: Date.now(), tiles, shapes }, null, 1));
  console.log(`Exported level ${level}: ${tiles.count} tiles, ${Object.keys(shapes).length} shapes -> ${outDir}`);
  engine.quit = true;
  await run;
}

main().catch((error) => { console.error(error); process.exit(1); });
