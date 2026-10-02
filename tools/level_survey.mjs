// What is where on a level, for turning a walkthrough into a scripted run: per block, the items on the
// floor, the special walls (doors, locks, levers, plates, secret walls...) and what the block's own script
// does (the engine opcodes it calls, with their literal arguments; subroutines followed a few deep).
//
//   node tools/level_survey.mjs LEVEL [save.json]     (default: a new game, teleported there)
//
// The web engine (the reference) does the loading; nothing is played.
import fs from "fs";
import path from "path";

const LANDS = process.env.LANDS || (process.env.LANDS || (process.env.HOME || "") + "/lands");
const { LandsOfLore, Resources } = await import(path.join(LANDS, "src/game/lol.mjs"));
const { PakArchive } = await import(path.join(LANDS, "src/formats/pak.mjs"));
const { OPCODE_NAMES } = await import(path.join(LANDS, "src/game/opcode-names.mjs"));

const LEVEL = Number(process.argv[2]);
const SAVE = process.argv[3];
const privateRoot = path.join(LANDS, "private/game");
const manifest = JSON.parse(fs.readFileSync(path.join(privateRoot, "import-manifest.json"), "utf8"));
const dataRoot = path.join(privateRoot, manifest.dataRoot || "");
const resources = new Resources("", async (name) => new Uint8Array(fs.readFileSync(path.join(dataRoot, name))));
const engine = new LandsOfLore({ resources, log: () => {} });
engine.audioContext = () => null;
engine.openTalkArchive = async (name) => {
  const file = path.join(dataRoot, name);
  if (!fs.existsSync(file)) return null;
  const archive = new PakArchive(new Uint8Array(fs.readFileSync(file)));
  return { has: (n) => archive.has(n), get: async (n) => archive.get(n) };
};
engine.playIntro = false;
await engine.preInit(); engine.setupTimers(); await engine.startup();
try { engine.uiRegisterExtraSpells(); } catch { /* fine */ }
// answer whatever the level opens on arrival (a press and a release on separate ticks)
let down = false;
const pump = setInterval(() => { try { engine.pushMouse(160, 100, down ? 0 : 1); down = !down; } catch { /* not ready */ } }, 90);
pump.unref();
const drain = async (n = 40) => { for (let i = 0; i < n; i += 1) await engine.drainAsync(); };
if (SAVE) engine.resumeGame(JSON.parse(fs.readFileSync(SAVE, "utf8"))).catch(() => {});
else engine.playNewGame(0).catch(() => {});
await new Promise((r) => setTimeout(r, 3000));
await drain();
if (engine.currentLevel !== LEVEL) {
  engine.queueAsync(() => engine.debugTeleport(LEVEL));
  await new Promise((r) => setTimeout(r, 4000));
  await drain();
}
clearInterval(pump);
console.log(`level ${engine.currentLevel} ${engine.levelName()} - party at block ${engine.currentBlock} facing ${engine.currentDirection}`);

// ---- walls ----
const TYPES = { 13: "door", 10: "plate", 15: "button/lever", 22: "secret", 17: "teleporter" };
const SIDE = ["N", "E", "S", "W"];
const walls = new Map();
const typeCount = {};
for (let b = 0; b < 1024; b += 1) {
  const l = engine.levelBlockProperties[b];
  for (let side = 0; side < 4; side += 1) {
    const w = l.walls[side];
    if (!w) continue;
    const type = engine.wllAutomapData[w] & 0x1f;
    typeCount[type] = (typeCount[type] || 0) + 1;
    if (!TYPES[type] && !(engine.wllWallFlags[w] & 8) && !(engine.specialWallTypes[w] >= 1)) continue;
    const what = TYPES[type] || (engine.specialWallTypes[w] ? `special${engine.specialWallTypes[w]}` : `openable(t${type})`);
    if (!walls.has(b)) walls.set(b, []);
    walls.get(b).push(`${SIDE[side]}:${what}#${w}`);
  }
}

// ---- block scripts ----
const { ordr, data } = engine.scriptData || { ordr: [], data: [] };
const decode = (ip) => { const code = (data[ip++] << 16) >> 16; let op = (code >> 8) & 0x1f; let param = 0; if (code & 0x8000) { op = 0; param = code & 0x7fff; } else if (code & 0x4000) param = (code << 24) >> 24; else if (code & 0x2000) param = (data[ip++] << 16) >> 16; return [op, param, ip]; };
const entries = [];
for (let ip = 0, wasCall = false; ip < data.length;) {
  const [op, param, next] = decode(ip);
  if (op === 0 && wasCall && param > 0 && param < data.length) entries.push(param);
  wasCall = op === 2 && param === 1;
  ip = next;
}
for (const o of ordr) if (o !== 0xffff) entries.push(o + 1);
entries.sort((a, b) => a - b);
const endOf = (at) => { const later = entries.find((o) => o > at); return later === undefined ? data.length : later; };
const BORING = new Set(["updateBlockAnimations", "getGlobalVar", "setGlobalVar", "getWallType", "getWallFlags", "clearDialogueField", "update", "delay"]);
const ops = (start, end, depth, seen) => {
  const out = [];
  let pushes = []; let prevCall = false;
  for (let ip = start; ip < end;) {
    const [op, param, next] = decode(ip);
    if (op === 3 || op === 4) pushes.push(param);
    else if (op === 7 || op === 5 || op === 6) pushes.push("?");
    else {
      if (op === 14) {
        const name = OPCODE_NAMES[param & 0xff] || `op${param & 0xff}`;
        if (!BORING.has(name)) out.push(`${name}(${pushes.slice().reverse().join(",")})`);
      } else if (op === 0 && prevCall && depth < 3 && param > 0 && param < data.length && !seen.has(param)) {
        seen.add(param);
        out.push(...ops(param, endOf(param), depth + 1, seen));
      }
      pushes = [];
    }
    prevCall = op === 2 && param === 1;
    ip = next;
  }
  return out;
};

// SURVEY_OP=name: every script function (not only the blocks' own) that calls that opcode, with the
// function's other calls - for what no block reaches directly (monster deaths, level functions)
if (process.env.SURVEY_OP) {
  for (const at of [...new Set(entries)]) {
    const calls = [...new Set(ops(at, endOf(at), 3, new Set()))];
    if (calls.some((c) => c.startsWith(process.env.SURVEY_OP + "("))) console.log(`function @${at} (block entry of ${[...ordr].map((o, b) => (o + 1 === at ? b : -1)).filter((b) => b >= 0).join(",") || "none"}): ${calls.join(" ").slice(0, 1500)}`);
  }
}

// ---- print ----
for (let b = 0; b < 1024; b += 1) {
  const items = engine.uiItemsOnBlock(b).map((i) => i.name);
  const w = walls.get(b);
  const o = ordr[b];
  const script = o !== undefined && o !== 0xffff ? [...new Set(ops(o + 1, endOf(o + 1), 0, new Set()))] : [];
  if (!items.length && !w && !script.length) continue;
  const parts = [`block ${b} (x${b & 31} y${b >> 5})`];
  if (items.length) parts.push(`items: ${items.join(", ")}`);
  if (w) parts.push(`walls: ${w.join(" ")}`);
  if (script.length) parts.push(`script: ${script.join(" ").slice(0, Number(process.env.SURVEY_WIDTH || 400))}`);
  console.log(parts.join(" | "));
}
console.log(`wall types seen: ${JSON.stringify(typeCount)}`);
// SURVEY_ITEMS=1: every item record placed on a block of this level, hidden ones (0x8000) too
if (process.env.SURVEY_ITEMS) {
  engine.itemsInPlay.forEach((it, i) => {
    if (!it || !it.itemPropertyIndex || !it.block) return;
    console.log(`item #${i} ${engine.itemName(i)}(${it.itemPropertyIndex}) block ${it.block} level ${it.level ?? "?"} flg 0x${(it.shpCurFrame_flg || 0).toString(16)}`);
  });
}
process.exit(0);
