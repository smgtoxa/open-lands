// What the game's data holds that the game never uses: for every level, the items placed on it and the
// items, music tracks and flags its scripts refer to (literal arguments, every script function); then the
// item properties and music tracks nothing refers to.   node tools/unused_scan.mjs > scan.txt
import fs from "fs";
import path from "path";

const LANDS = process.env.LANDS || (process.env.LANDS || (process.env.HOME || "") + "/lands");
const { LandsOfLore, Resources } = await import(path.join(LANDS, "src/game/lol.mjs"));
const { PakArchive } = await import(path.join(LANDS, "src/formats/pak.mjs"));
const { OPCODE_NAMES } = await import(path.join(LANDS, "src/game/opcode-names.mjs"));
const privateRoot = path.join(LANDS, "private/game");
const manifest = JSON.parse(fs.readFileSync(path.join(privateRoot, "import-manifest.json"), "utf8"));
const dataRoot = path.join(privateRoot, manifest.dataRoot || "");
const engine = new LandsOfLore({ resources: new Resources("", async (n) => new Uint8Array(fs.readFileSync(path.join(dataRoot, n)))), log: () => {} });
engine.audioContext = () => null;
engine.openTalkArchive = async (name) => { const f = path.join(dataRoot, name); if (!fs.existsSync(f)) return null; const a = new PakArchive(new Uint8Array(fs.readFileSync(f))); return { has: (n) => a.has(n), get: async (n) => a.get(n) }; };
engine.playIntro = false;
await engine.preInit(); engine.setupTimers(); await engine.startup();
let down = false;
setInterval(() => { try { engine.pushMouse(160, 100, down ? 0 : 1); down = !down; } catch { /* not ready */ } }, 90).unref();
const drain = async (n = 40) => { for (let i = 0; i < n; i += 1) await engine.drainAsync(); };
engine.playNewGame(0).catch(() => {});
await new Promise((r) => setTimeout(r, 3000)); await drain();

const ITEM_OPS = new Set(["makeItem", "createHandItem", "giveItem", "createLevelItem", "checkPartyForItemType"]);
const placed = new Map();   // prop -> [levels]
const scripted = new Map(); // prop -> [level:op]
const music = new Map();    // track -> [levels]
const add = (m, k, v) => { if (!m.has(k)) m.set(k, []); if (!m.get(k).includes(v)) m.get(k).push(v); };

for (let L = 1; L <= 29; L += 1) {
  engine.queueAsync(() => engine.debugTeleport(L));
  // a level can open on something only a key answers (level 21): keys while it loads, not after
  const keys = setInterval(() => { try { engine.pushKey("Enter"); } catch { /* fine */ } }, 400);
  await new Promise((r) => setTimeout(r, 3500)); await drain();
  clearInterval(keys);
  if (engine.currentLevel !== L) { console.log(`level ${L}: did not load`); continue; }
  engine.itemsInPlay.forEach((it, i) => { if (it && it.itemPropertyIndex && it.block && (it.level === L || it.level === -1 || it.level === undefined)) add(placed, it.itemPropertyIndex, L); });
  const { data } = engine.scriptData;
  const decode = (ip) => { const code = (data[ip++] << 16) >> 16; let op = (code >> 8) & 0x1f; let param = 0; if (code & 0x8000) { op = 0; param = code & 0x7fff; } else if (code & 0x4000) param = (code << 24) >> 24; else if (code & 0x2000) param = (data[ip++] << 16) >> 16; return [op, param, ip]; };
  let pushes = [];
  for (let ip = 0; ip < data.length;) {
    const [op, param, next] = decode(ip);
    if (op === 3 || op === 4) pushes.push(param);
    else if (op === 7 || op === 5 || op === 6) pushes.push(null);
    else {
      if (op === 14) {
        const name = OPCODE_NAMES[param & 0xff] || "";
        const args = pushes.slice().reverse();
        if (ITEM_OPS.has(name)) {
          const prop = name === "checkPartyForItemType" ? args[1] : args[0];
          if (typeof prop === "number" && prop > 0) add(scripted, prop, `${L}:${name}`);
        }
        if (name === "playMusicTrack" && typeof args[0] === "number") add(music, args[0], L);
      }
      pushes = [];
    }
    ip = next;
  }
  // shop stock: offered by the merchants' scripts (the page's uiWares reads it the same way)
  for (let b = 0; b < 1024; b += 1) for (const w of engine.uiWares(b)) add(scripted, w.type, `${L}:shop@${b}`);
  console.log(`level ${L} ${engine.levelName(L)}: scanned`);
}

// items the scene scripts (TIM) give: command 21 (execOpcode) with in-game TIM opcode 3 (giveItem)
for (const f of fs.readdirSync(dataRoot).filter((n) => /\.PAK$/i.test(n))) {
  let pak; try { pak = new PakArchive(new Uint8Array(fs.readFileSync(path.join(dataRoot, f)))); } catch { continue; }
  for (const e of pak.list()) {
    if (!/\.TIM$/i.test(e.name)) continue;
    const bytes = await pak.get(e.name);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let avtl = null;
    for (let off = 12; off + 8 <= bytes.length;) {
      const id = String.fromCharCode(...bytes.subarray(off, off + 4)); const size = view.getUint32(off + 4, false);
      if (id === "AVTL") { avtl = new Uint16Array(size >> 1); for (let i = 0; i < avtl.length; i += 1) avtl[i] = view.getUint16(off + 8 + i * 2, true); }
      off += 8 + size + (size & 1);
    }
    if (!avtl) continue;
    for (let ip = 0; ip + 2 < avtl.length && avtl[ip] > 0; ip += avtl[ip]) {
      if (((avtl[ip + 2] << 24) >> 24) === 21 && avtl[ip + 3] === 3) add(scripted, avtl[ip + 4], `tim:${e.name}`);
    }
  }
}
// what the joining characters bring
for (const d of engine.static.CharacterDefs) for (const it of d.items) if (it) add(scripted, it, `start:${d.name}`);

const names = engine.itemProperties.map((p) => engine.getLangString(p.nameStringId) || "");
console.log("\n== item properties never placed on a level nor named by a level script ==");
names.forEach((n, i) => { if (i > 0 && n && !placed.has(i) && !scripted.has(i)) console.log(`${i} ${n}`); });
console.log("\n== placed only (never scripted), for reference: count " + [...placed.keys()].filter((k) => !scripted.has(k)).length);
console.log("\n== music tracks the level scripts play ==");
console.log([...music.keys()].sort((a, b) => a - b).map((t) => `${t}:[${music.get(t).join(",")}]`).join(" "));
process.exit(0);
