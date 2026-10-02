// Copied from the web project (scripts/), writing to GameData/textured/base instead of private/hd (tools/textured/README).
// Exports the wall sets of one level as whole pictures for an HD pass: the backdrop (floor and
// ceiling, 22x15 tiles) and every wall set's nine perspective views, assembled from the VCN tiles
// exactly as the scene renderer does. hdwalls (private/hd/<level>/walls.json) records which tile
// index (and flip) each 8x8 cell came from, so a generated picture can be cut back into tiles.png.
// Usage: node scripts/export_hd_walls.mjs <level> [scale=4]
import fs from "fs";
import path from "path";
const { LandsOfLore, Resources } = await import((process.env.LANDS || process.env.HOME + "/lands") + "/src/game/lol.mjs");
const { writePngRgba } = await import((process.env.LANDS || process.env.HOME + "/lands") + "/scripts/png.mjs");

const level = Number(process.argv[2] || 1);
const scale = Number(process.argv[3] || 4);
const dataRoot = process.env.LOL_DATA || path.resolve("GameData/DATA");
const outDir = path.resolve(process.env.LOL_OUT || "GameData/textured/base", `level${level}`);
fs.mkdirSync(path.join(outDir, "walls"), { recursive: true });

const resources = new Resources("", async (name) => new Uint8Array(fs.readFileSync(path.join(dataRoot, name))));
const engine = new LandsOfLore({ resources, log: () => {} });
engine.audioContext = () => null;
engine.openTalkArchive = async () => null;

// the nine wall views: VMP offset inside a set, width and height in tiles (scene.mjs)
const VIEWS = [[102, 3, 5], [97, 1, 5], [129, 6, 5], [117, 2, 6], [81, 2, 8], [159, 10, 8], [45, 3, 12], [239, 16, 12], [0, 3, 15]];

function tilePixels(vcn, encoded, out, ox, oy, stride) {
  const flipped = (encoded & 0x4000) !== 0;
  const tile = encoded & 0x3fff;
  if (!tile || tile >= vcn.tileCount) return;
  const shift = vcn.shifts[tile];
  for (let y = 0; y < 8; y += 1) for (let x = 0; x < 8; x += 1) {
    const sx = flipped ? 7 - x : x;
    const packed = vcn.tiles[tile * 32 + y * 4 + (sx >> 1)];
    const nibble = sx & 1 ? packed & 0x0f : packed >> 4;
    out[(oy + y) * stride + ox + x] = vcn.colorTable[nibble | shift];
  }
}

function toRgba(pixels, w, h, palette, mask) {
  const out = new Uint8Array(w * scale * h * scale * 4);
  for (let y = 0; y < h * scale; y += 1) for (let x = 0; x < w * scale; x += 1) {
    const i = Math.floor(y / scale) * w + Math.floor(x / scale);
    const raw = pixels[i];
    const o = (y * w * scale + x) * 4;
    if (mask && !mask[i]) { out[o] = out[o + 1] = out[o + 2] = 128; out[o + 3] = 255; continue; } // empty cell: flat grey, like the sprites' background
    out[o] = (palette[raw * 3] * 255) / 63; out[o + 1] = (palette[raw * 3 + 1] * 255) / 63; out[o + 2] = (palette[raw * 3 + 2] * 255) / 63; out[o + 3] = 255;
  }
  return out;
}

async function main() {
  const run = engine.playNewGame(0).catch((error) => { if (error.message !== "quit") throw error; });
  const started = Date.now();
  while (Date.now() - started < 30000 && !(engine.currentLevel === 1 && engine.vcn && engine.screen.page(0)[100 * 320 + 200])) await new Promise((r) => setTimeout(r, 100));
  if (level !== 1) { await engine.debugTeleport(level); await new Promise((r) => setTimeout(r, 500)); }
  const palette = engine.screen.getPalette(0);
  const vmp = engine.vmp; const vcn = engine.vcn;
  const sets = Math.floor((vmp.length - 330) / 431);
  const pictures = [];
  const emit = (name, kind, w, h, tileAt) => { // tileAt(x, y) -> encoded vmp entry
    const pixels = new Uint8Array(w * 8 * h * 8);
    const mask = new Uint8Array(w * 8 * h * 8);
    const cells = [];
    let used = 0;
    for (let y = 0; y < h; y += 1) for (let x = 0; x < w; x += 1) {
      const enc = tileAt(x, y);
      if (!enc || !(enc & 0x3fff)) continue;
      used += 1;
      tilePixels(vcn, enc, pixels, x * 8, y * 8, w * 8);
      for (let yy = 0; yy < 8; yy += 1) mask.fill(1, (y * 8 + yy) * w * 8 + x * 8, (y * 8 + yy) * w * 8 + x * 8 + 8);
      cells.push({ x, y, tile: enc & 0x3fff, flipped: !!(enc & 0x4000) });
    }
    if (!used) return;
    const file = `walls/${name}.png`;
    writePngRgba(path.join(outDir, file), w * 8 * scale, h * 8 * scale, toRgba(pixels, w * 8, h * 8, palette, mask));
    pictures.push({ file, kind, width: w, height: h, cells });
  };
  emit("backdrop", "backdrop", 22, 15, (x, y) => vmp[y * 22 + x]);
  for (let m = 1; m <= sets; m += 1) {
    VIEWS.forEach(([offset, w, h], v) => emit(`set${m}_view${v}`, "wall", w, h, (x, y) => vmp[(m - 1) * 431 + offset + 330 + y * w + x]));
  }
  fs.writeFileSync(path.join(outDir, "walls.json"), JSON.stringify({ level, scale, tileColumns: 32, tileCount: vcn.tileCount, pictures }, null, 1));
  console.log(`Exported level ${level}: ${sets} wall sets, ${pictures.length} pictures -> ${outDir}/walls`);
  engine.quit = true;
  await run.catch(() => {});
  process.exit(0);
}
main().catch((error) => { console.error(error); process.exit(1); });
