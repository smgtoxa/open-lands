// The browser build's engine, run headless on its REAL runLoop, one tick at a time on a virtual
// clock with seeded dice, printing the same trace format as Harness/Program.cs. tools/diff.sh runs
// both and compares them: the web engine is the reference.
//
//   node tools/trace_js.mjs "new:0,wait:2,state,key:enter,wait:3,mon"
//
// Env: LANDS (default ~/lands), LOL_TRACE_OPS=1, LOL_TRACE_UI=1.
import fs from "fs";
import path from "path";

const LANDS = process.env.LANDS || (process.env.LANDS || (process.env.HOME || "") + "/lands");
const { LandsOfLore, Resources } = await import(path.join(LANDS, "src/game/lol.mjs"));
const { PakArchive } = await import(path.join(LANDS, "src/formats/pak.mjs"));
const { OPCODE_NAMES } = await import(path.join(LANDS, "src/game/opcode-names.mjs"));

const privateRoot = path.join(LANDS, "private/game");
const manifest = JSON.parse(fs.readFileSync(path.join(privateRoot, "import-manifest.json"), "utf8"));
const dataRoot = path.join(privateRoot, manifest.dataRoot || "");
const resources = new Resources("", async (name) => new Uint8Array(fs.readFileSync(path.join(dataRoot, name))));
const traceUi = process.env.LOL_TRACE_UI === "1";
const engine = new LandsOfLore({ resources, log: (m) => console.log(`LOG ${m}`) });
engine.audioContext = () => null;
engine.openTalkArchive = async (name) => {
  const file = path.join(dataRoot, name);
  if (!fs.existsSync(file)) return null;
  const archive = new PakArchive(new Uint8Array(fs.readFileSync(file)));
  return { has: (n) => archive.has(n), get: async (n) => archive.get(n) };
};
engine.ui = {
  message: (t, k) => { if (traceUi) console.log(`MSG ${String(t).replace(/[\x01-\x1f]/g, " ").replace(/\s+/g, " ").trim()}`); },
  miss: (e) => { if (traceUi) console.log(`MISS ${e.attacker} -> ${e.target}`); },
  damage: (e) => { if (traceUi) { const a = e.attacker & 0x8000 ? engine.monsters[e.attacker & 0x7fff] : null; console.log(`DAMAGE ${e.monster !== undefined ? "m" + e.monster : "c" + e.character} ${e.damage} by ${e.attacker}${a ? ` (ps ${a.pitScale || 0} ng ${a.ngplus || 0})` : ""}`); } },
  kill: (e) => { if (traceUi) console.log(`KILL m${e.monster} by ${e.attacker}`); },
  dialogue: (t) => { if (traceUi) console.log(`SAY ${String(t).replace(/[\x01-\x1f]/g, " ").replace(/\s+/g, " ").trim()}`); },
};
if (process.env.LOL_TRACE_TIMERS === "1") {
  const add = engine.addTimer.bind(engine);
  engine.addTimer = (id, func, ...rest) => add(id, (i) => { console.log(`  TIMER 0x${id.toString(16)} @${Math.round(engine.getMillis() / engine.tickLength)}`); return func(i); }, ...rest);
}
if (process.env.LOL_TRACE_DICE === "1") {
  const rd = engine.rollDice.bind(engine);
  engine.rollDice = (t, p, i = 0) => { const r = rd(t, p, i); console.log(`  DICE ${t}d${p}+${i} -> ${r}`); return r; };
  let dice = 0;
  const rf = engine.randomFloat.bind(engine);
  engine.randomFloat = () => { const r = rf(); console.log(`  RND#${++dice} ${Number(r.toFixed(5))} ${new Error().stack.split("\n").slice(2, 4).map((l) => l.trim().split(" ")[1]).join(" < ")}`); return r; };
}
if (process.env.LOL_TRACE_OPS === "1") {
  const rls = engine.runLevelScriptCustom.bind(engine);
  engine.runLevelScriptCustom = async (b, f, ...rest) => { console.log(`runLevelScript(${b}, 0x${(f & 0xffff).toString(16)})`); return rls(b, f, ...rest); };
  engine.opcodes = engine.opcodes.map((f, i) => f && (async (st) => {
    const r = await f(st);
    if (OPCODE_NAMES[i] !== "updateBlockAnimations") console.log(`  op ${OPCODE_NAMES[i] || "op" + i}(${[0, 1, 2].map((k) => st.arg(k)).join(",")}) -> ${r}`);
    return r;
  }));
}

// Seeded dice and a clock of our own: the C# harness does exactly the same.
const SEED = Number(process.env.LOL_SEED || 12345);
engine.randomSeed = SEED;
engine.presentationSeed = 1;
engine.clockBase = 0;
engine.virtualClock = 0;
engine.clockTicks = 0;

// Tick gate, with the browser's timing: delay() waits in steps of at most 16 ms, running timers and
// update() itself only when asked (update = true) - moving the clock does not fire timers, exactly
// as Date.now() moving on does not. Every step is one tick, released by the driver.
// Every waiter wakes on the next tick - several async chains can be waiting at once (a fade started
// without await next to runLoop), exactly as they run side by side in the browser. The C# scheduler
// wakes all sleepers of a pump the same way.
let waiters = [];
const nextTick = () => new Promise((resolve) => waiters.push(resolve));
engine.delay = async function (ms, update = false) {
  if (this.quit) throw new Error("quit");
  const end = this.getMillis() + ms;
  do {
    if (update) { this.timerUpdate(); if (this.update) this.update(); }
    this.present();
    if (process.env.LOL_TRACE_SLEEP === "1") console.log(`  SLEEP ${Math.min(16, Math.max(0, end - this.getMillis())).toFixed(2)} @${engine.clockTicks} ${new Error().stack.split("\n").slice(2, 5).map((l) => l.trim().split(" ")[1]).join(" < ")}`);
    await nextTick();
  } while (this.getMillis() < end);
};
// Browser timing: timers are scheduled on the wall clock, not quantised to a virtual one.
engine.quantiseToTick = (moment) => moment;
engine.advanceClock = () => {};
const settle = () => new Promise((resolve) => setImmediate(resolve));
// Let what was just started run up to its first wait, before any time passes (C#: Scheduler.Run drains).
async function quiesce() { for (let k = 0; k < 400 && !waiters.length; k += 1) await settle(); }
async function run(ticks) {
  for (let i = 0; i < ticks; i += 1) {
    engine.clockTicks += 1;
    engine.virtualClock = engine.clockBase + engine.clockTicks * engine.tickLength;
    const due = waiters;
    waiters = [];
    for (const w of due) w();
    for (let k = 0; k < 400 && !waiters.length; k += 1) await settle();
    for (let k = 0; k < 5; k += 1) await settle();
  }
}


// Navigation, identical in Trace/Program.cs: path over passable walls (closed doors with a switch
// count), turn with keys, open the door ahead by clicking its switch, answer boxes with Enter.
function passable(from, dir) {
  const to = engine.calcNewBlockPosition(from, dir);
  const wall = engine.levelBlockProperties[to].walls[dir ^ 2];
  const flags = engine.wllWallFlags[wall];
  if (!(flags & 1)) return true;
  return (flags & 8) !== 0 && engine.wllShapeMap[wall] > 0;
}
function findPath(from, to) {
  const prev = new Map([[from, null]]);
  const queue = [from];
  while (queue.length) {
    const b = queue.shift();
    if (b === to) break;
    for (let d = 0; d < 4; d += 1) {
      if (!passable(b, d)) continue;
      const n = engine.calcNewBlockPosition(b, d);
      if (prev.has(n)) continue;
      prev.set(n, [b, d]);
      queue.push(n);
    }
  }
  if (!prev.has(to)) return null;
  const dirs = [];
  for (let b = to; prev.get(b); b = prev.get(b)[0]) dirs.unshift(prev.get(b)[1]);
  return dirs;
}
async function pressKey(key) { engine.events.push({ type: "key", key }); await run(8); }
// a player's click: press, and release a few ticks later (a press and release in one tick is no click to the
// engine, and leaves its button state behind)
async function clickSlow(x, y) { engine.pushMouse(x, y, 1); await run(3); engine.events.push({ type: "mouseup", x, y, button: 1 }); await run(5); }
async function clickAt(x, y) { engine.pushMouse(x, y, 1); engine.events.push({ type: "mouseup", x, y, button: 1 }); await run(8); }
async function waitIdle(max = 300) {
  for (let i = 0; i < max; i += 1) {
    if (!(engine.updateFlags & 3) && !engine.needSceneRestore && !engine.weaponsDisabled) return true;
    if (engine.needSceneRestore && i % 40 === 39) engine.events.push({ type: "key", key: "Enter" });
    await run(1);
  }
  return false;
}
// press - click the decoration on the wall ahead (a button, a lever, a lock), where the game drew it
async function pressAhead() {
  const block = engine.calcNewBlockPosition(engine.currentBlock, engine.currentDirection);
  const wall = engine.levelBlockProperties[block].walls[engine.currentDirection ^ 2];
  for (let l = engine.wllShapeMap[wall]; l > 0; l = engine.levelDecorationProperties[l].next) {
    const p = engine.levelDecorationProperties[l];
    if (p.shapeIndex[1] === 0xffff) continue;
    const shape = engine.levelDecorationShapes[p.shapeIndex[1]];
    const x = p.shapeX[1] + engine.clickedShapeXOffs + (shape.width >> 1), y = p.shapeY[1] + engine.clickedShapeYOffs + (shape.height >> 1);
    await clickSlow(x, y);
    return `${x}x${y}`;
  }
  await clickSlow(200, 60);
  return "200x60";
}

// pick - take what is drawn in the view (a niche, a table, the floor ahead), as a player clicks it: the
// engine hit-tests items by the item buffer it redraws, so find a drawn item pixel and click there; each
// item goes to the first free inventory slot. Up to 8.
function sceneItemAt() {
  const cp = engine.screen.curPage;
  engine.screen.curPage = engine.sceneDrawPage1;
  engine.redrawSceneItem();
  let at = null;
  for (let y = 2; y < 118 && !at; y += 2) for (let x = 114; x < 286 && !at; x += 2) if (engine.screen.getPagePixel(engine.screen.curPage, x, y)) at = [x, y];
  engine.screen.curPage = cp;
  return at;
}
async function pickScene() {
  const got = [];
  for (let i = 0; i < 8; i += 1) {
    await waitIdle();
    const at = sceneItemAt();
    if (!at) break;
    await clickAt(at[0], at[1]);
    await run(4);
    if (!engine.itemInHand) break;
    got.push(engine.itemName(engine.itemInHand));
    const slot = Array.from(engine.inventory).indexOf(0);
    if (slot < 0) break;
    engine.queueAsync(() => engine.inventorySlotClick(slot));
    await run(6);
  }
  return got;
}

async function openDoorAhead() {
  const block = engine.calcNewBlockPosition(engine.currentBlock, engine.currentDirection);
  const wall = engine.levelBlockProperties[block].walls[engine.currentDirection ^ 2];
  if (!(engine.wllWallFlags[wall] & 1)) return true;
  let l = engine.wllShapeMap[wall];
  while (l > 0) {
    const p = engine.levelDecorationProperties[l];
    if (p.shapeIndex[1] !== 0xffff) {
      const shape = engine.levelDecorationShapes[p.shapeIndex[1]];
      await clickAt(p.shapeX[1] + engine.clickedShapeXOffs + (shape.width >> 1), p.shapeY[1] + engine.clickedShapeYOffs + (shape.height >> 1));
      break;
    }
    l = p.next;
  }
  for (let i = 0; i < 200; i += 1) {
    await run(1);
    if (!(engine.wllWallFlags[engine.levelBlockProperties[block].walls[engine.currentDirection ^ 2]] & 1)) return true;
  }
  return false;
}
// Fight what stands next to the party, as a player would (never a peaceful one - mode 1, the game's own
// test in checkMonsterTypeHostility - such as the gate guard): face it, swing with whoever is ready, until
// nothing has been in reach for a second and a half (or maxSec of game time, or the party is dead).
// The harness keeps the party alive (it tests the game along the walkthrough, not whether a new party
// survives it): full health and magic for every living hero, logged.
function harnessHeal(why, quiet = false) {
  for (const c of engine.characters) if (c.flags & 1 && c.hitPointsCur > 0) { c.hitPointsCur = c.hitPointsMax; c.magicPointsCur = c.magicPointsMax; }
  if (!quiet) console.log(`harness heal (${why})`);
}
const hurt = () => engine.characters.some((c) => c.flags & 1 && c.hitPointsCur > 0 && c.hitPointsCur < c.hitPointsMax);

async function fightNear(maxSec) {
  const limit = Math.round((maxSec * 1000) / engine.tickLength);
  let quiet = 0; let swings = 0; let heals = 0;
  for (let t = 0; t < limit;) {
    if (!engine.characters.some((c) => c.flags & 1 && c.hitPointsCur > 0)) break;
    const near = engine.monsters.filter((m) => m.properties && m.hitPoints > 0 && m.mode !== 1 && m.mode < 13 && m.block && engine.getBlockDistance(engine.currentBlock, m.block) <= 1);
    // quiet only when nothing is even on its way (within two blocks)
    const coming = near.length || engine.monsters.some((m) => m.properties && m.hitPoints > 0 && m.mode !== 1 && m.mode < 13 && m.block && engine.getBlockDistance(engine.currentBlock, m.block) <= 2);
    if (!near.length) { if (coming) quiet = 0; else if (++quiet > 90) break; await run(1); t += 1; continue; }
    quiet = 0;
    if (hurt()) { harnessHeal("fight", true); heals += 1; }
    const d = [-32, 1, 32, -1].indexOf(near[0].block - engine.currentBlock);
    if (d >= 0 && d !== engine.currentDirection && !(engine.updateFlags & 3)) { await pressKey(((d - engine.currentDirection + 4) & 3) === 3 ? "q" : "e"); t += 8; continue; }
    if (![0, 1, 2, 3].some((c) => engine.characters[c].flags & 1 && engine.characters[c].hitPointsCur > 0 && engine.uiCanAct(c))) { await run(5); t += 5; continue; }
    engine.queueAsync(() => engine.quickAttack());
    swings += 1;
    await run(10); t += 10;
  }
  if (heals) console.log(`harness heals in the fight: ${heals}`);
  return swings;
}

async function gotoBlock(target, fight = false) {
  let stuck = 0;
  for (let attempt = 0; attempt < (fight ? 400 : 60); attempt += 1) {
    if (engine.currentBlock === target) return true;
    if (fight) {
      const n = await fightNear(120); if (n) console.log(`fought at ${engine.currentBlock}: ${n} swings`);
      if (hurt()) harnessHeal("walk");
    }
    await waitIdle();
    const dirs = findPath(engine.currentBlock, target);
    if (!dirs) { console.log(`no path from ${engine.currentBlock} to ${target}`); return false; }
    const d = dirs[0];
    const diff = (d - engine.currentDirection + 4) & 3;
    if (diff === 1) await pressKey("e");
    else if (diff === 3) await pressKey("q");
    else if (diff === 2) { await pressKey("e"); await waitIdle(); await pressKey("e"); }
    await waitIdle();
    if (engine.currentDirection !== d) continue;
    if (!(await openDoorAhead())) console.log(`door ahead of ${engine.currentBlock} did not open`);
    const before = engine.currentBlock;
    await pressKey("w");
    await waitIdle();
    for (let i = 0; i < 150 && engine.currentBlock === before; i += 1) await run(1);
    if (engine.currentBlock === before) {
      console.log(`stuck at ${before} facing ${engine.currentDirection}; flags ${engine.updateFlags}`);
      if (++stuck > 6) return false;
      await run(30);
    }
  }
  return engine.currentBlock === target;
}

const KEYS = { enter: "Enter", space: " ", up: "ArrowUp", down: "ArrowDown", left: "ArrowLeft", right: "ArrowRight", turnl: "Home", turnr: "PageUp", esc: "Escape" };
const choices = () => {
  if (engine.dialogueNumButtons) return Array.from({ length: engine.dialogueNumButtons }, (_, i) => (engine.dialogueButtonString?.[i] || "...").trim());
  return null;
};

await engine.preInit();
for (const step of (process.argv[2] || "new:0,wait:2,state").split(",")) {
  const colon = step.indexOf(":");
  const cmd = colon < 0 ? step : step.slice(0, colon);
  const arg = colon < 0 ? "" : step.slice(colon + 1);
  console.log(`>> ${step} (block ${engine.currentBlock ?? ""} dir ${engine.currentDirection ?? ""})`);
  if (cmd === "load") {
    // load:FILE or load:FILE#slot - a saveState() snapshot, or a lol.saves object (slot default "auto")
    const [file, slot] = arg.split("#");
    const data = JSON.parse(fs.readFileSync(file, "utf8"));
    const state = data.state || (data.characters ? data : data[slot || "auto"].state);
    engine.playIntro = false;
    engine.resumeGame(state).catch((e) => { if (e.message !== "quit") console.log(`ENGINE ERROR ${e.stack}`); });
    await quiesce();
    await run(1);
  } else if (cmd === "tp") {
    const [lv, bl] = arg.split("/").map(Number);
    engine.queueAsync(() => engine.debugTeleport(lv, Number.isNaN(bl) ? undefined : bl));
    await run(1);
  } else if (cmd === "new") {
    engine.playIntro = false;
    engine.smoothScrollingEnabled = true;
    engine.playNewGame(Number(arg)).catch((e) => { if (e.message !== "quit") console.log(`ENGINE ERROR ${e.stack}`); });
    await quiesce();
    await run(1);
  } else if (cmd === "wait") await run(Math.round((Number(arg) * 1000) / engine.tickLength));
  else if (cmd === "key") { engine.events.push({ type: "key", key: KEYS[arg] || arg }); await run(6); }
  else if (cmd === "click" || cmd === "rclick") { const [x, y] = arg.split("x").map(Number); const b = cmd === "click" ? 1 : 2; engine.pushMouse(x, y, b); engine.events.push({ type: "mouseup", x, y, button: b }); await run(6); }
  else if (cmd === "goto") console.log(`goto ${arg} -> ${await gotoBlock(Number(arg))} at ${engine.currentBlock}`);
  // walk:B - goto, fighting whatever stands in reach on the way; fight:S - fight what is near for up to S seconds
  else if (cmd === "walk") console.log(`walk ${arg} -> ${await gotoBlock(Number(arg), true)} at ${engine.currentBlock}`);
  else if (cmd === "fight") console.log(`fight -> ${await fightNear(Number(arg))} swings`);
  else if (cmd === "heal") harnessHeal("step");
  // give:PROP - an item of that property into the pack (as the giveItem opcode does); setflag:N - a game flag
  // the story would have set by now (checkpoints start from a teleport, not a played-through game)
  else if (cmd === "give") { const it = engine.makeItem(Number(arg), 0, 0); console.log(`give ${engine.itemName(it)}(${arg}) -> ${engine.addItemToInventory(it) ? 1 : 0}`); }
  // hold:PROP - take the first item of that property from the pack into the hand
  else if (cmd === "hold") {
    const slot = Array.from(engine.inventory).findIndex((it) => it && engine.itemsInPlay[it].itemPropertyIndex === Number(arg));
    if (slot >= 0) { engine.queueAsync(() => engine.inventorySlotClick(slot)); await run(6); }
    console.log(`hold ${arg} -> ${engine.itemInHand ? engine.itemName(engine.itemInHand) : "-"}`);
  }
  // clear - the level's hostile monsters killed as the game kills them (killMonster: their items drop): a
  // checkpoint tests the level's mechanics, and a fight with a party it was not built for is noise there
  else if (cmd === "clear") {
    engine.queueAsync(() => { let n = 0; for (const m of engine.monsters) if (m.properties && m.hitPoints > 0 && m.mode !== 1 && m.mode < 13) { m.hitPoints = 0; engine.killMonster(m); n += 1; } console.log(`clear ${n}`); });
    await run(6);
  }
  // equip:PROP - the first item of that property into Ak'shel's weapon hand (as "Equip best" puts items on)
  else if (cmd === "equip") {
    const slot = Array.from(engine.inventory).findIndex((it) => it && engine.itemsInPlay[it].itemPropertyIndex === Number(arg));
    if (slot >= 0) engine.queueAsync(async () => console.log(`equip ${arg} -> ${(await engine.uiDropInventoryOn(slot, 0, 0)) ? 1 : 0}`));
    await run(10);
  }
  // prop:NAME - an engine field as it is now (true/false/number)
  else if (cmd === "prop") console.log(`prop ${arg} = ${typeof engine[arg] === "boolean" ? (engine[arg] ? "true" : "false") : engine[arg] ?? "-"}`);
  // pagesum - a checksum of every screen page that exists (what the engine has drawn where)
  else if (cmd === "pagesum") {
    const out = [];
    for (let n = 0; n < 16; n += 1) { const pg = engine.screen.pages[n]; if (!pg) continue; let h = 0; for (let i = 0; i < pg.length; i += 1) h = (Math.imul(h, 31) + pg[i]) >>> 0; out.push(`${n}:${h.toString(16)}`); }
    console.log(`pages ${out.join(" ")}`);
  }
  else if (cmd === "setflag") { engine.setGameFlag(Number(arg)); console.log(`setflag ${arg}`); }
  else if (cmd === "buttons") console.log(`buttons ${(choices() || []).map((c, i) => `${i}:${c}`).join(" | ") || "-"}`);
  else if (cmd === "dbgbuttons") console.log(`dbg n=${engine.dialogueNumButtons} x=${Array.from(engine.dialogueButtonPosX || [])} y=${Array.from(engine.dialogueButtonPosY || [])} w=${engine.dialogueButtonWidth} flags=${engine.updateFlags} tim=${engine.activeTim.map((t) => (t ? 1 : 0)).join("")}`);
  else if (cmd === "pick") console.log(`pick ${(await pickScene()).join(", ") || "-"}`);
  else if (cmd === "press") { console.log(`press ${await pressAhead()}`); await run(30); }
  // take - pick up everything on the floor in reach, as the page's "Take all" does
  else if (cmd === "take") {
    engine.queueAsync(async () => { for (const f of engine.uiFloorItems()) { const ok = await engine.uiTakeFloorItem(f.item, f.block); console.log(`take ${f.name} -> ${ok ? 1 : 0}`); if (!ok) break; } });
    await run(20);
  }
  // face:D - turn (with the turn keys) until facing D (0 N, 1 E, 2 S, 3 W)
  else if (cmd === "face") {
    const d = Number(arg);
    for (let i = 0; i < 4 && engine.currentDirection !== d; i += 1) {
      await waitIdle();
      await pressKey(((d - engine.currentDirection + 4) & 3) === 3 ? "q" : "e");
      await waitIdle();
    }
    console.log(`face ${arg} -> ${engine.currentDirection}`);
  }
  else if (cmd === "idle") await waitIdle();
  else if (cmd === "cflags") engine.characters.forEach((c, i) => { if (c.flags & 1) console.log(`char${i} ${c.name} flags 0x${c.flags.toString(16)} hp ${c.hitPointsCur}/${c.hitPointsMax} mp ${c.magicPointsCur} ev [${Array.from(c.characterUpdateEvents)}] dl [${Array.from(c.characterUpdateDelay)}]`); });
  else if (cmd === "choose") { engine.uiChoose(Number(arg)); await run(6); }
  // next:N - move a scene on N times as a player does: click the first dialogue button if there is one (MORE, OK),
  // else a key press; three seconds after each
  else if (cmd === "next") {
    for (let i = 0; i < Number(arg); i += 1) {
      if (engine.dialogueNumButtons) {
        // press and release on separate ticks: one in the same tick is not seen as a click
        const x = engine.dialogueButtonPosX[0] + ((engine.dialogueButtonWidth || 74) >> 1), y = engine.dialogueButtonPosY[0] + 4;
        engine.pushMouse(x, y, 1); await run(3); engine.events.push({ type: "mouseup", x, y, button: 1 }); await run(5);
      }
      // a key only when something waits for one (on the playfield Enter casts the selected spell)
      else if (engine.updateFlags & 3 || engine.needSceneRestore || engine.activeTim.some((t) => t)) await pressKey("Enter");
      await run(Math.round(3000 / engine.tickLength));
    }
  }
  else if (cmd === "call") {
    // call:method/arg/arg - an engine method as the page calls it (on the engine's action queue)
    const [name, ...raw] = arg.split("/");
    const args = raw.map((a) => (a === "true" ? true : a === "false" ? false : Number.isNaN(Number(a)) ? a : Number(a)));
    engine.queueAsync(async () => {
      const r = await engine[name](...args);
      const shown = r === undefined || r === null ? "-" : typeof r === "object" ? "object" : typeof r === "boolean" ? (r ? "true" : "false") : String(r);
      console.log(`call ${name} -> ${shown}`);
    });
    await run(6);
  }
  else if (cmd === "mon") console.log("monsters: " + engine.monsters.map((m, i) => ({ m, i })).filter(({ m }) => m.properties && m.hitPoints > 0).map(({ m, i }) => `#${i} t${m.type} b${m.block} mode${m.mode}`).join(" "));
  else if (cmd === "palsum") console.log("palsum " + engine.screen.screenPalette.reduce((a, b) => a + b, 0) + " fade " + engine.screen.fadeFlag);
  else if (cmd === "monall") console.log("slots: " + engine.monsters.map((m, i) => `#${i} t${m.type} b${m.block} mode${m.mode} hp${m.hitPoints}`).join(" "));
  else if (cmd === "state") console.log(JSON.stringify({ level: engine.currentLevel, block: engine.currentBlock, dir: engine.currentDirection, hand: engine.itemInHand, flags: engine.updateFlags, hp: engine.characters[0].hitPointsCur, suspend: engine.suspendScript, f73: engine.flagsTable[73], inv: Array.from(engine.inventory.slice(0, 9)), act: engine.activeTim.map((t) => (t ? 1 : 0)).join("") }));
  else if (cmd === "save") fs.writeFileSync(arg, JSON.stringify(engine.saveState()));
  // flags:4.252.11 - game flags (quest progress); items - the hand and the pack, by name and property
  else if (cmd === "flags") console.log("flags " + arg.split(".").map((f) => `${f}=${engine.queryGameFlag(Number(f)) ? 1 : 0}`).join(" "));
  else if (cmd === "items") {
    const name = (it) => `${engine.itemName(it)}(${engine.itemsInPlay[it].itemPropertyIndex})`;
    const inv = Array.from(engine.inventory).map((it, i) => (it ? `${i}:${name(it)}` : null)).filter(Boolean);
    const worn = engine.characters.filter((c) => c.flags & 1).map((c) => `${c.name}[${Array.from(c.items).map((it) => (it ? name(it) : "-")).join(" ")}]`);
    console.log(`items hand ${engine.itemInHand ? name(engine.itemInHand) : "-"} | ${inv.join(" ")} | ${worn.join(" ")} | crowns ${engine.credits}`);
  }
  else console.log(`unknown step ${step}`);
}
engine.quit = true;
for (const w of waiters) w();
process.exit(0);
