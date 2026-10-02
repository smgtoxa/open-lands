// Item property numbers by name (for give:PROP in walkthrough checkpoints).  node tools/item_names.mjs [filter]
import fs from "fs";
import path from "path";
const LANDS = process.env.LANDS || (process.env.LANDS || (process.env.HOME || "") + "/lands");
const { LandsOfLore, Resources } = await import(path.join(LANDS, "src/game/lol.mjs"));
const privateRoot = path.join(LANDS, "private/game");
const manifest = JSON.parse(fs.readFileSync(path.join(privateRoot, "import-manifest.json"), "utf8"));
const dataRoot = path.join(privateRoot, manifest.dataRoot || "");
const engine = new LandsOfLore({ resources: new Resources("", async (n) => new Uint8Array(fs.readFileSync(path.join(dataRoot, n)))), log: () => {} });
engine.audioContext = () => null;
await engine.preInit(); await engine.startup();
const want = (process.argv[2] || "").toLowerCase();
engine.itemProperties.forEach((p, i) => { const n = engine.getLangString(p.nameStringId) || ""; if (n && n.toLowerCase().includes(want)) console.log(`${i} ${n}`); });
process.exit(0);
