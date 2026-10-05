// src/main.mjs, section C (lines 1326-2080): combat feedback (log lines, floating numbers, card
// flashes), the Imp's Pit, the quest log, statistics / achievements / bestiary, monster health bars.
// Ported 1:1 (docs/port/HOST.md). C# 9 (Unity compiles this).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    /// <summary>main.mjs dungeonRun: { floor, seed, finishing }</summary>
    public sealed class PitRun
    {
        public int floor;
        public string seed;
        public bool finishing;
    }

    /// <summary>main.mjs questStateCache: { key, states }</summary>
    public sealed class QuestStateCache
    {
        public string key;
        public Dictionary<string, string> states;
    }

    /// <summary>The keys the page hangs on a bestiary entry that the engine's BestiaryEntry has no field for.</summary>
    public sealed class BeastNotes
    {
        public string thumb;
        public List<string> drops, loot;
    }

    /// <summary>
    /// main.mjs `stats`: a free-form object. Its numbers (kills, damageDealt, damageTaken, seconds and every
    /// counter bump/peak writes) are `values` - engine.meta.stats once a game runs (`stats = engine.meta.stats`);
    /// hunt / pinned / familyKills are the page's own keys; `bestiary` is engine.meta.bestiary behind the
    /// getter runGame defines, or the stored one before that. The page's keys on a bestiary entry (thumb,
    /// drops, loot) live in `notes`, by monster name. ToJson() writes the same JSON the browser stores.
    /// </summary>
    public sealed class WebStats
    {
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        public Dictionary<string, double> values = new Dictionary<string, double>();
        public string hunt, pinned;
        public Dictionary<string, int> familyKills;
        public Dictionary<string, BeastNotes> notes = new Dictionary<string, BeastNotes>();
        Dictionary<string, BestiaryEntry> _bestiary = new Dictionary<string, BestiaryEntry>();
        Func<Dictionary<string, BestiaryEntry>> _bestiaryOf;

        /// <summary>stats[key] (missing keys read as 0)</summary>
        public double this[string key]
        {
            get => values.TryGetValue(key, out double v) ? v : 0;
            set => values[key] = value;
        }
        public double kills { get => this["kills"]; set => this["kills"] = value; }
        public double damageDealt { get => this["damageDealt"]; set => this["damageDealt"] = value; }
        public double damageTaken { get => this["damageTaken"]; set => this["damageTaken"] = value; }
        public double seconds { get => this["seconds"]; set => this["seconds"] = value; }

        public Dictionary<string, BestiaryEntry> bestiary => _bestiaryOf != null ? _bestiaryOf() : _bestiary;

        public BeastNotes notesOf(string name)
        {
            name = name ?? "";
            if (!notes.TryGetValue(name, out var n)) notes[name] = n = new BeastNotes();
            return n;
        }

        /// <summary>`{ kills: 0, damageDealt: 0, damageTaken: 0, seconds: 0, bestiary: {}, ...JSON.parse(stored) }`</summary>
        public static WebStats Read(string stored)
        {
            var s = new WebStats();
            s.values["kills"] = 0; s.values["damageDealt"] = 0; s.values["damageTaken"] = 0; s.values["seconds"] = 0;
            JsonObject o = null;
            try { o = JsonNode.Parse(stored ?? "null") as JsonObject; } catch (Exception) { /* ignore */ }
            if (o == null) return s;
            foreach (var kv in o)
            {
                try
                {
                    if (kv.Key == "bestiary" && kv.Value is JsonObject b)
                    {
                        s._bestiary = new Dictionary<string, BestiaryEntry>();
                        foreach (var e in b)
                        {
                            if (!(e.Value is JsonObject eo)) continue;
                            s._bestiary[e.Key] = eo.Deserialize<BestiaryEntry>(Json);
                            var n = new BeastNotes
                            {
                                thumb = eo["thumb"] is JsonValue t && t.TryGetValue(out string ts) ? ts : null,
                                drops = eo["drops"] is JsonArray d ? d.Select(x => (string)x).ToList() : null,
                                loot = eo["loot"] is JsonArray l ? l.Select(x => (string)x).ToList() : null,
                            };
                            if (n.thumb != null || n.drops != null || n.loot != null) s.notes[e.Key] = n;
                        }
                    }
                    else if (kv.Key == "hunt") s.hunt = (string)kv.Value;
                    else if (kv.Key == "pinned") s.pinned = (string)kv.Value;
                    else if (kv.Key == "familyKills" && kv.Value is JsonObject f) s.familyKills = f.Deserialize<Dictionary<string, int>>(Json);
                    else if (kv.Value is JsonValue v && v.TryGetValue(out double num)) s.values[kv.Key] = num;
                    // ponytail: other free-form keys are dropped; keep them in a JsonObject if the page ever adds one
                }
                catch (Exception) { /* a malformed key: skip it */ }
            }
            return s;
        }

        /// <summary>`stats = engine.meta.stats; Object.defineProperty(stats, "bestiary", { get: () => engine.meta.bestiary })`.
        /// Like the JS, the page's own keys (hunt, pinned, familyKills) are not carried over; the bestiary
        /// entries' keys are (metaLoad copies the stored entries whole), so `from`'s notes come along.</summary>
        public static WebStats OfEngine(LandsOfLore engine, WebStats from)
        {
            return new WebStats
            {
                values = engine.meta.stats,
                _bestiaryOf = () => engine.meta.bestiary,
                notes = from != null ? from.notes : new Dictionary<string, BeastNotes>(),
            };
        }

        /// <summary>JSON.parse(JSON.stringify(stats))</summary>
        public JsonObject ToJson()
        {
            var o = new JsonObject();
            foreach (var kv in values) o[kv.Key] = kv.Value;
            if (hunt != null) o["hunt"] = hunt;
            if (pinned != null) o["pinned"] = pinned;
            if (familyKills != null) o["familyKills"] = JsonSerializer.SerializeToNode(familyKills, Json);
            var b = new JsonObject();
            foreach (var kv in bestiary)
            {
                var e = JsonSerializer.SerializeToNode(kv.Value, Json) as JsonObject ?? new JsonObject();
                if (notes.TryGetValue(kv.Key, out var n))
                {
                    if (n.thumb != null) e["thumb"] = n.thumb;
                    if (n.drops != null) e["drops"] = JsonSerializer.SerializeToNode(n.drops, Json);
                    if (n.loot != null) e["loot"] = JsonSerializer.SerializeToNode(n.loot, Json);
                }
                b[kv.Key] = e;
            }
            o["bestiary"] = b;
            return o;
        }
    }

    public sealed partial class Web
    {
        // JS number -> text
        static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
        static string Px(double v) => N(v) + "px";
        static string St(Dictionary<string, string> states, string id) => states != null && id != null && states.TryGetValue(id, out var v) ? v : null;
        static int? PropOf(Dictionary<string, int> props, string key) => props != null && props.TryGetValue(key, out int v) ? v : (int?)null;
        // JS spots[i % spots.length]: an empty list reads undefined (block 0 here)
        static int At(List<int> list, int i) => list.Count == 0 ? 0 : list[i % list.Count];
        Monster monsterAt(int i) => engine != null && engine.monsters != null && i >= 0 && i < engine.monsters.Length ? engine.monsters[i] : null;

        // --- combat feedback: hit/miss log lines, floating damage numbers, monster health bars ---
        VisualElement fxLayer;
        void init_C01() { fxLayer = Q("#fx-layer"); }
        string heroName(int c) => engine != null && c >= 0 && c < engine.characters.Length && engine.characters[c] != null ? engine.characters[c].name : "Someone";
        string attackerName(int a) => (a & 0x8000) != 0 ? "The monster" : a == -1 || a == 0xffff ? "Something" : heroName(a);
        public void combatMiss(object e)
        {
            int attacker = JsObj.Int(e, "attacker"), target = JsObj.Int(e, "target");
            gameUi.message((target & 0x8000) != 0 ? $"{attackerName(attacker)} misses." : $"{attackerName(attacker)} misses {heroName(target)}.", "combat");
        }
        public void combatDamage(object e)
        {
            int attacker = JsObj.Int(e, "attacker"), damage = JsObj.Int(e, "damage");
            if (JsObj.Has(e, "monster"))
            {
                var m = monsterAt(JsObj.Int(e, "monster"));
                gameUi.message($"{attackerName(attacker)} hits {(m != null ? $"the {engine.monsterName(m).ToLowerInvariant()}" : "")} for {damage}.", "combat");
                if (m != null && m.drawSerial == engine.sceneSerial) floatText(m.drawX, m.drawY, $"-{damage}", "fx-dmg");
                if ((attacker & 0x8000) == 0) { stats.damageDealt += damage; statsTouched(); peak("maxHit", damage); }
                if (m != null) bestiaryMeet(m);
            }
            else
            {
                int character = JsObj.Int(e, "character");
                var m = (attacker & 0x8000) != 0 ? monsterAt(attacker & 0x7fff) : null;
                gameUi.message($"{heroName(character)} takes {damage} damage{(m != null ? $" from the {engine.monsterName(m).ToLowerInvariant()}" : "")}.", "combat");
                gameUi.cardHit(character, damage);
                stats.damageTaken += damage;
                statsTouched();
                int hp = character >= 0 && character < engine.characters.Length && engine.characters[character] != null ? engine.characters[character].hitPointsCur : 0;
                if (hp > 0 && hp <= 5) bump("closeCalls");
            }
        }
        public void combatKill(object e)
        {
            var m = monsterAt(JsObj.Int(e, "monster"));
            if (m == null) return;
            int attacker = JsObj.Int(e, "attacker");
            string name = engine.monsterName(m);
            gameUi.message($"{attackerName(attacker)} slays the {name.ToLowerInvariant()}.", "combat");
            announce(engine.metaKilled(m));
            var entry = bestiaryMeet(m);
            var entryNotes = beastNotesOf(entry);
            string family = LandsOfLore.familyOf(name).id;
            stats.familyKills = stats.familyKills ?? new Dictionary<string, int>();
            stats.familyKills[family] = (stats.familyKills.TryGetValue(family, out int fk) ? fk : 0) + 1;
            // The imp's errand trophy: a fixed chance from the creatures he asked about, straight into the bag.
            var job = CampStoreUi.readJob(engine);
            var trophy = CampStoreUi.rollErrandDrop(engine, job, family);
            if (trophy != null)
            {
                if (trophy.full) gameUi.message($"The {name.ToLowerInvariant()} leaves {trophy.name.ToLowerInvariant()}, but your pack is full.", "system");
                else
                {
                    gameUi.message($"The {name.ToLowerInvariant()} leaves {trophy.name.ToLowerInvariant()}.", "system");
                    if (m.drawSerial == engine.sceneSerial) floatText(m.drawX, m.drawY, $"+1 {trophy.name}", "fx-loot");
                    else toast($"+1 {trophy.name}", "fx-toast-ach");
                    inventoryKey = ""; impKey = ""; renderErrand();
                }
            }
            if (settings.craft)
            {
                string got = CraftingUi.rollDrop(engine, name);
                if (!string.IsNullOrEmpty(got))
                {
                    entryNotes.drops = entryNotes.drops ?? new List<string>();
                    if (!entryNotes.drops.Contains(got)) entryNotes.drops.Add(got);
                    gameUi.message($"The {name.ToLowerInvariant()} leaves {LandsOfLore.REAGENTS[got].name.ToLowerInvariant()}.", "system");
                    if (m.drawSerial == engine.sceneSerial) floatText(m.drawX, m.drawY, $"+1 {LandsOfLore.REAGENTS[got].name}", "fx-loot");
                    else toast($"+1 {LandsOfLore.REAGENTS[got].name}", "fx-toast-ach");
                    renderCraft();
                }
            }
            // What it left behind: the block's items a moment after the corpse drops its load.
            if (m.block != 0)
            {
                int block = m.block;
                var before = new HashSet<int>(engine.uiItemsOnBlock(block).Select(f => f.item));
                timers.setTimeout(() =>
                {
                    if (engine == null || !playing) return;
                    foreach (var f in engine.uiItemsOnBlock(block))
                    {
                        if (before.Contains(f.item) || string.IsNullOrEmpty(f.name)) continue;
                        entryNotes.loot = entryNotes.loot ?? new List<string>();
                        if (!entryNotes.loot.Contains(f.name)) { entryNotes.loot.Add(f.name); statsTouched(); }
                    }
                }, 2500); // the corpse drops its load a few ticks after the kill
            }
            if (engine.uiInDungeon()) { engine.dungeon.kills = engine.dungeon.kills + 1; dungeonKey = ""; }
            statsTouched();
            renderHunt();
            checkAchievements();
        }

        // ---- The Imp's Pit: a run through a generated floor ----
        // The engine half (src/game/dungeon.mjs) owns the map and the restore; this owns the floor's
        // objective, its rewards, and what the player sees.
        VisualElement dungeonNote;
        void init_C02() { dungeonNote = Q("#dungeon-note"); }
        string dungeonKey = "";
        PitRun dungeonRun = null; // { floor, objective, need, seed, finishing }

        int? sigilProperty()
        {
            var props = CraftingUi.registerPotions(engine);
            return PropOf(props, "sigil");
        }

        PitInfo dungeonState()
        {
            if (engine == null || !engine.uiInDungeon()) return null;
            var info = new PitInfo(engine.uiDungeonInfo());
            var prop = sigilProperty();
            info.sigils = prop == null ? 0 : engine.uiCountOfProperty(prop.Value);
            // Where the floor's own things are, so the line under the map can point at them.
            var d = engine.dungeon;
            info.vaultWhere = d.vault != null ? engine.uiDungeonBearing(d.vault.block) : "";
            info.exitWhere = engine.uiDungeonBearing(info.stairs != 0 ? info.stairs : info.exitBlock);
            info.leverWhere = string.Join(" and ", (d.levers ?? new List<DungeonLever>()).Where(l => !l.pulled).Select(l => engine.uiDungeonBearing(l.block)).Take(2));
            return info;
        }

        // the older floors' objectives (a save made on one): reached, they pay and send the party home as before
        static bool legacyObjective(string id) => id == "exit" || id == "sigil" || id == "shards" || id == "cull" || id == "vault";

        bool objectiveDone(PitInfo info, PitRun run)
        {
            if (info == null || run == null) return false;
            switch (info.objective)
            {
                case "clear": return info.monstersLeft == 0;
                case "boss": return !info.bossAlive;
                case "exit": return engine.currentBlock == info.exitBlock;
                case "sigil":
                case "shards":
                case "sigils":
                case "vault": return info.sigils - info.sigilsBefore >= info.need;
                case "cull": return info.kills >= info.need;
                default: return info.gateOpen;
            }
        }

        // Sends the party down. The camp has to be put back first: its borrowed walls must not be part of
        // what the floor is written over.
        public async Task enterDungeon(double floor)
        {
            if (engine == null || !playing || engine.uiInDungeon()) return;
            var plan = LandsOfLore.floorPlan(floor, visited, engine.currentLevel);
            if (plan == null) { gameUi.message("The imp has nowhere to send you yet: walk another level first.", "system"); return; }
            if (engine.uiInCamp()) leaveCampAndMaybeRespawn(false);
            closeCampSheet();
            cancelTeleport();
            engine.queueAsync(async () =>
            {
                if (await engine.uiDungeonEnter(plan) == 0) { gameUi.message("The way down will not open.", "system"); return; }
                await setupFloor(plan, true);
                var record = DungeonRun.readRun(engine);
                record.runs += 1;
                DungeonRun.writeRun(record);
                bump("pitRuns");
            });
        }

        // Fills a floor the engine has just written: its monsters, its master, the things its puzzle needs and a few
        // worth finding, then tells the party what the floor wants.
        async Task setupFloor(FloorPlan plan, bool first)
        {
            var d = engine.dungeon;
            if (d == null) return;
            dungeonRun = new PitRun { floor = plan.floor, seed = plan.seed, finishing = false };
            string objective = d.objective;   // the engine may have swapped a puzzle this level cannot furnish
            d.sigilsBefore = sigilProperty() == null ? 0 : engine.uiCountOfProperty(sigilProperty().Value);
            int placed = engine.uiDungeonPopulate(LandsOfLore.monsterCount(plan.depth), plan.depth, plan.seed);
            if (objective == "boss" && engine.uiDungeonBoss(plan.depth, plan.seed) < 0)
            {
                objective = "clear";   // nothing of this level would stand as a master
                engine.uiDungeonOpenGate(null);
            }
            if (objective == "clear" && placed == 0) { objective = "switch"; }
            d.objective = objective;
            var props = CraftingUi.registerPotions(engine);
            var spots = d.spots.Count != 0 ? d.spots.ToList() : d.plan.deadEnds.Where(b => !d.plan.regionB.Contains(b)).ToList();
            int nextSpot = 0;
            if (objective == "sigils")
            {
                // never more than the floor has hiding places for
                d.need = Math.Max(1, Math.Min(plan.need, spots.Count));
                int put = 0;
                for (int i = 0; i < d.need; i += 1) if (await engine.uiDungeonPlaceItem(sigilProperty(), At(spots, nextSpot++)) != 0) put += 1;
                d.need = Math.Max(1, Math.Min(d.need, put));
            }
            else d.need = 1;
            if (objective == "key") await engine.uiDungeonPlaceItem(PropOf(props, "pitkey"), At(spots, nextSpot++));
            // something worth the search in the hiding places left, more of it deeper down
            var loot = new[] { PropOf(props, "heal"), PropOf(props, "mana"), PropOf(props, "antidote"), PropOf(props, "strength"), PropOf(props, "agility"), PropOf(props, "arcane") }.Where(x => x != null).ToList();
            for (int i = 0; i < 1 + plan.depth / 3 && loot.Count != 0 && nextSpot < spots.Count + 2; i += 1)
                await engine.uiDungeonPlaceItem(loot[(i + plan.depth) % loot.Count], At(spots, nextSpot++));
            // Only now may the floor be finished: until everything is in place a poll would see an empty floor.
            d.ready = true;
            dungeonKey = "";
            string label = DungeonRun.OBJECTIVES.FirstOrDefault(o => o.id == objective)?.label ?? "Survive";
            gameUi.message(first ? $"The floor closes over you. Floor {plan.floor} of the imp's pit: {label.ToLowerInvariant()}." : $"Down the stairs to floor {plan.floor}: {label.ToLowerInvariant()}.", "system");
            gameUi.message(objective switch
            {
                "levers" => $"A gate bars the way down. Somewhere on this floor {d.levers.Count} levers hold it shut.",
                "switch" => "A gate bars the way down. Somewhere a loose stone works it: try the dead ends.",
                "key" => "A locked gate bars the way down. Its key is hidden on this floor.",
                "sigils" => $"A gate bars the way down. It opens for {d.need} of the imp's sigils.",
                "clear" => $"A gate bars the way down, sealed until the last of the {placed} down here falls.",
                "boss" => "The floor's master waits by the way down. Kill it, or you go no further.",
                _ => $"{placed} of them are down here with you.",
            }, "system");
            if (d.secrets.Count != 0) gameUi.message(d.clueWall != 0 ? "Some walls here are not what they seem. Look for the odd ones out." : "Some walls here are not what they seem.", "system");
            toast($"The Imp's Pit · floor {plan.floor}", "fx-toast-ach");
            impKey = "";
            renderDungeon();
        }

        // The party has reached the way down. A floor whose gate is open (and whose master is dead) is done: it pays,
        // and the party may go deeper or stay; the Climb out button takes them home whenever they like.
        void pitStairs()
        {
            if (engine == null || !engine.uiInDungeon() || dungeonRun == null) return;
            var info = dungeonState();
            if (info == null || !info.ready || legacyObjective(info.objective)) return;
            if (!info.gateOpen) return;
            if (info.objective == "boss" && info.bossAlive) { gameUi.message("The way down is here, but the floor's master still walks. It will not let you pass.", "system"); return; }
            if (info.objective == "clear" && info.monstersLeft > 0) return;
            int floor = dungeonRun.floor;
            if (!engine.dungeon.floorDone)
            {
                engine.dungeon.floorDone = true;
                var got = DungeonRun.grantRewards(engine, floor, dungeonRun.seed);
                var record = DungeonRun.readRun(engine);
                record.cleared = Math.Max(record.cleared, floor);
                record.best = record.best ?? new JsonObject();
                string fk = floor.ToString(CultureInfo.InvariantCulture);
                record.best[fk] = (record.best[fk] is JsonValue bv && bv.TryGetValue(out double bd) ? bd : 0) + 1;
                DungeonRun.writeRun(record);
                gameUi.message($"Floor {floor} is beaten. The imp pays up: {string.Join(", ", got)}.", "system");
                toast($"Floor {floor} beaten", "fx-toast-ach");
                inventoryKey = ""; impKey = ""; sidebarKey = "";
                checkAchievements();
            }
            if (pitAsking) return;
            pitAsking = true;
            _ = pitAskDeeper(floor);
        }
        bool pitAsking;

        async Task pitAskDeeper(int floor)
        {
            try
            {
                bool deeper = await askConfirm($"Stairs lead further down. Go on to floor {floor + 1}? It will be harder than this one. (You can climb out whenever you like with the button in the Objectives box.)", "The way down", $"Down to floor {floor + 1}", "Not yet");
                if (deeper) descendDungeon(floor + 1);
            }
            finally { pitAsking = false; }
        }

        void descendDungeon(int floor)
        {
            if (engine == null || !engine.uiInDungeon()) return;
            var plan = LandsOfLore.floorPlan(floor, visited, engine.currentLevel);
            if (plan == null || plan.level == engine.dungeon.level) { gameUi.message("The stairs end in rubble. The imp will have to pull you up.", "system"); return; }
            engine.queueAsync(async () =>
            {
                if (await engine.uiDungeonDescend(plan) == 0) { gameUi.message("The stairs end in rubble.", "system"); return; }
                await setupFloor(plan, false);
            });
        }

        // Finishing an older floor (a save made on one): it pays and sends the party back to the imp, as it did.
        void finishDungeon()
        {
            if (engine == null || dungeonRun == null || dungeonRun.finishing) return;
            dungeonRun.finishing = true;
            int floor = dungeonRun.floor;
            var got = DungeonRun.grantRewards(engine, floor, dungeonRun.seed);
            var record = DungeonRun.readRun(engine);
            record.cleared = Math.Max(record.cleared, floor);
            DungeonRun.writeRun(record);
            gameUi.message($"The floor is done. The imp pays up: {string.Join(", ", got)}.", "system");
            timers.setTimeout(() =>
            {
                if (engine == null || !playing) return;
                engine.queueAsync(async () =>
                {
                    await engine.uiDungeonLeave();
                    dungeonRun = null;
                    dungeonKey = "";
                    renderDungeon();
                });
            }, 1500);
        }

        // Leaving: the floors beaten on the way down are paid already; the one the party stands on is left.
        void abandonDungeon()
        {
            if (engine == null || !engine.uiInDungeon()) return;
            closeCampSheet();
            bool done = engine.dungeon.floorDone;
            engine.queueAsync(async () =>
            {
                await engine.uiDungeonLeave();
                dungeonRun = null;
                dungeonKey = "";
                gameUi.message(done ? "You climb back out of the pit." : "You climb back out of the pit. This floor was left unfinished.", "system");
                renderDungeon();
            });
        }

        VisualElement dungeonOut;
        void init_C03()
        {
            dungeonOut = Q("#dungeon-out");
            if (dungeonOut != null) dungeonOut.On("click", async (DomEvent ev) =>
            {
                if (engine == null || !engine.uiInDungeon()) return;
                bool @out = await askConfirm(engine.dungeon.floorDone ? "Climb back out of the pit? Everything you earned on the way down is yours." : "Climb back out of the pit? The floors beaten on the way down are paid; this one stays unbeaten.", "Leave the pit", "Climb out", "Stay");
                if (@out) abandonDungeon();
            });
        }

        bool pitBossToldDead;

        void renderDungeon()
        {
            if (dungeonNote == null) return;
            var info = dungeonState();
            if (dungeonOut != null) dungeonOut.SetHidden(info == null);
            if (info == null)
            {
                // hidden whenever out of the pit (a load resets the key: the old floor's note stayed on screen)
                dungeonKey = "";
                if (!dungeonNote.IsHidden()) dungeonNote.SetHidden(true);
                return;
            }
            // A save loaded inside a floor brings the run back with it; the floor itself is the source of
            // truth for what it asks.
            if (dungeonRun == null) dungeonRun = new PitRun { floor = info.depth, seed = engine.dungeon.seed, finishing = false };
            var def = DungeonRun.OBJECTIVES.FirstOrDefault(o => o.id == info.objective) ?? DungeonRun.OBJECTIVES.First();
            bool done = objectiveDone(info, dungeonRun);
            string note = def.note(info, info.need);
            string key = $"{info.depth}|{info.objective}|{note}|{(done ? "true" : "false")}";
            if (key != dungeonKey)
            {
                dungeonKey = key;
                dungeonNote.SetHidden(false);
                dungeonNote.SetText($"The Imp's Pit, floor {info.depth} · {def.label} · {note}");
                dungeonNote.ClassToggle("ready", done);
            }
            if (legacyObjective(info.objective)) { if (done && info.ready) finishDungeon(); return; }
            if (!info.ready) return;
            engine.uiDungeonBossTick();
            // the puzzles the host keeps the score of: the floor's monsters, the imp's sigils
            if (!info.gateOpen && info.objective == "clear" && info.monstersLeft == 0) engine.uiDungeonOpenGate("The last of them falls. Far off, a gate grinds open.");
            if (!info.gateOpen && info.objective == "sigils" && info.sigils - info.sigilsBefore >= info.need) engine.uiDungeonOpenGate("The sigils grow warm in your pack. Far off, a gate grinds open.");
            if (info.objective == "boss" && !info.bossAlive && !pitBossToldDead)
            {
                pitBossToldDead = true;
                gameUi.message($"The floor's master falls. The way down is free, {engine.uiDungeonBearing(info.stairs)}.", "system");
                // the master of floor five carries the one thing that can save Timothy (while it still can)
                if (info.depth == 5 && engine.uiPerfectPotionWanted() && engine.dungeon.bossBlock != 0
                    && !engine.dungeon.madeItems.Any(it => engine.uiExtraItem(it)?.id == "perfectheal"))
                {
                    var prop = PropOf(CraftingUi.registerPotions(engine), "perfectheal");
                    int block = engine.dungeon.bossBlock;
                    engine.queueAsync(async () =>
                    {
                        if (await engine.uiDungeonPlaceItem(prop, block) != 0) gameUi.message("The master drops a glowing flask: a Perfect Healing Potion.", "note");
                    });
                }
            }
            if (info.objective == "boss" && info.bossAlive) pitBossToldDead = false;
        }

        // The imp's errand, under the map: what he asked for and how far along it is. Redrawn only when the
        // numbers change, so it costs nothing per frame.
        VisualElement errandNote;
        void init_C04() { errandNote = Q("#errand-note"); }
        string errandKey = "";
        void renderErrand()
        {
            if (errandNote == null) return;
            var job = CampStoreUi.readJob(engine);
            if (job == null || string.IsNullOrEmpty(job.kind) || engine == null || !playing)
            {
                errandKey = "";
                if (!errandNote.IsHidden()) errandNote.SetHidden(true);
                return;
            }
            int have = CampStoreUi.jobProgress(engine, job);
            string key = $"{job.kind}{job.need}{(string.IsNullOrEmpty(job.item) ? job.family : job.item)}|{have}";
            if (key == errandKey) return;
            errandKey = key;
            string from = LandsOfLore.FAMILIES.FirstOrDefault(f => f.id == job.family)?.name;
            string what = job.kind == "collect" ? job.name + (from != null ? $" from {from}" : "")
                : job.kind == "fetch" ? (job.key != null && LandsOfLore.REAGENTS.TryGetValue(job.key, out var rg) ? rg.name : null)
                : $"kills of {LandsOfLore.FAMILIES.FirstOrDefault(f => f.id == job.family)?.name ?? job.family}";
            errandNote.SetHidden(false);
            errandNote.SetText($"Errand: {have}/{job.need} {what}{(have >= job.need ? " — take them to the imp" : "")}");
            errandNote.ClassToggle("ready", have >= job.need);
            errandNote.SetTitle(engine.uiJobText(job));
        }

        // The hunt list: one creature marked in the bestiary, with what it is worth hunting for - the
        // errand it counts towards and the recipe its reagents still lock.
        VisualElement huntNote;
        void init_C05() { huntNote = Q("#hunt-note"); }
        void renderHunt()
        {
            if (huntNote == null) return;
            string name = stats.hunt;
            huntNote.SetHidden(string.IsNullOrEmpty(name));
            if (string.IsNullOrEmpty(name)) return;
            BestiaryEntry entry = null;
            if (stats.bestiary != null) stats.bestiary.TryGetValue(name, out entry);
            int kills = entry != null ? entry.kills : 0;
            var parts = new List<string> { $"Hunting {name} · {kills} slain" };
            var seenOn = entry != null ? (entry.levels ?? new List<int> { entry.level }).Where(l => l != 0).ToList() : null;
            if (seenOn != null && seenOn.Count != 0 && engine != null) parts.Add($"seen in {string.Join(", ", seenOn.Take(3).Select(l => engine.levelName(l)))}");
            if (settings.craft)
            {
                var family = LandsOfLore.familyOf(name);
                var drops = LandsOfLore.dropsFor(name);
                if (drops.Count != 0) parts.Add($"leaves {drops[0].name} ({N(drops[0].chance)}% per kill)");
                var job = CampStoreUi.readJob(engine);
                if (job != null && job.kind == "hunt" && job.family == family.id)
                {
                    parts.Add($"errand {CampStoreUi.jobProgress(engine, job)}/{job.need}");
                }
                var fam = stats.familyKills ?? new Dictionary<string, int>();
                var locked = LandsOfLore.RECIPES.Where(r => !CraftingUi.recipeKnown(r, fam) && (LandsOfLore.RECIPE_UNLOCK.TryGetValue(r.id, out var u) ? u.family : null) == family.id).ToList();
                if (locked.Count != 0)
                {
                    var need = LandsOfLore.RECIPE_UNLOCK[locked[0].id];
                    parts.Add($"{locked[0].name}: {Math.Min(need.kills, fam.TryGetValue(family.id, out int fk) ? fk : 0)}/{need.kills} of {family.name}");
                }
            }
            huntNote.SetText(string.Join(" · ", parts));
        }

        // --- quest log: derived from game flags every frame; toasts on change ---
        VisualElement questPanel;
        VisualElement questList;
        void init_C06() { questPanel = Q("#quest-panel"); questList = Q("#quest-list"); }
        QuestStateCache questStateCache = null; // states at the last check (null after a game start: no toasts for the initial state)
        int questTick = 0;
        void toast(string text, string cls = null)
        {
            if (fxLayer == null) return;
            var stack = fxLayer.Q(".toast-stack");
            if (stack == null) { stack = Dom.El("div"); stack.SetClassName("toast-stack"); fxLayer.Append(stack); }
            var el = Dom.El("div");
            el.SetClassName($"fx-toast {cls ?? ""}");
            el.SetText(text);
            stack.Append(el);
            el.On("animationend", () => el.RemoveFromHierarchy());
        }
        void updateQuests()
        {
            if (questPanel == null || engine == null || !playing) return;
            if ((questTick += 1) % 20 != 0) return; // a few times a second is plenty
            var states = LandsOfLore.questStates(engine);
            string key = JsonSerializer.Serialize(states);
            if (questStateCache != null && key != questStateCache.key)
            {
                foreach (var q in LandsOfLore.QUESTS)
                {
                    string before = St(questStateCache.states, q.id);
                    if (St(states, q.id) == "active" && before == "hidden") { toast($"New objective: {q.title}"); gameUi.message($"New objective: {q.title}", "note"); }
                    if (St(states, q.id) == "done" && before != "done" && before != null) { toast($"Objective complete: {q.title}", "done"); gameUi.message($"Objective complete: {q.title}", "note"); }
                }
            }
            if (questStateCache == null || key != questStateCache.key)
            {
                questStateCache = new QuestStateCache { key = key, states = states };
                var active = LandsOfLore.QUESTS.Where(q => St(states, q.id) == "active").ToList();
                questPanel.SetHidden(active.Count == 0);
                questList.ReplaceChildren(active.Select(q =>
                {
                    var li = Dom.El("li", null, null, true);
                    li.SetClassName((q.main ? "main" : "") + (stats.pinned == q.id ? " pinned" : ""));
                    li.SetTitle(q.detail);
                    var pin = mkEl("button", "quest-pin", stats.pinned == q.id ? "★" : "☆");
                    pin.SetAttr("type", "button");
                    pin.SetTitle(stats.pinned == q.id ? "Stop following this objective" : "Follow this objective under the map");
                    pin.On("click", (DomEvent @event) =>
                    {
                        @event.stopPropagation();
                        stats.pinned = stats.pinned == q.id ? "" : q.id;
                        statsTouched(); questStateCache = null; renderPinned();
                    });
                    li.Append(pin, mkEl("span", "", q.title));
                    return li;
                }).ToList());
            }
            renderPinned();
        }

        // The followed objective, shown where the hunt line is: one thing to do, always in sight.
        VisualElement pinnedNote;
        void init_C07() { pinnedNote = Q("#pinned-note"); }
        void renderPinned()
        {
            if (pinnedNote == null) return;
            var q = string.IsNullOrEmpty(stats.pinned) ? null : LandsOfLore.QUESTS.FirstOrDefault(x => x.id == stats.pinned);
            var states = engine != null && playing ? LandsOfLore.questStates(engine) : new Dictionary<string, string>();
            bool show = q != null && St(states, q.id) == "active";
            pinnedNote.SetHidden(!show);
            if (show) { pinnedNote.SetText($"★ {q.title}"); pinnedNote.SetTitle(q.detail ?? ""); }
        }
        void renderQuestJournal()
        {
            var box = Q("#journal-quests");
            if (box == null || engine == null) return;
            // the stylesheet's .journal-quests rules (a quest's description on its own line under the title) are
            // written for this box, but the page never gives it the class: the title and its text ran together
            if (!box.ClassListContains("journal-quests")) { box.AddToClassList("journal-quests"); CssLayout.Touch(box); }
            var states = LandsOfLore.questStates(engine);
            // JS builds this as markup: <h5>title</h5><div class="q[ done]"><b>[✔ ]title</b><small>detail</small></div>...
            List<VisualElement> section(string title, List<Quest> list, bool done)
            {
                var nodes = new List<VisualElement>();
                if (list.Count == 0) return nodes;
                nodes.Add(Dom.El("h5", null, title));
                foreach (var q in list)
                {
                    var div = Dom.El("div", "q" + (done ? " done" : ""));
                    div.Append(Dom.El("b", null, (done ? "✔ " : "") + q.title), Dom.El("small", null, q.detail));
                    nodes.Add(div);
                }
                return nodes;
            }
            var active = LandsOfLore.QUESTS.Where(q => St(states, q.id) == "active").ToList();
            var doneList = LandsOfLore.QUESTS.Where(q => St(states, q.id) == "done").ToList();
            var all = section("Active", active, false).Concat(section("Completed", doneList, true)).ToList();
            if (all.Count == 0) all.Add(Dom.El("p", "panel-note", "Nothing yet."));
            box.ReplaceChildren(all);
        }

        // --- statistics, achievements and bestiary (browser-wide, like the journal) ---
        const string STATS_KEY = "lol.stats";
        const string ACH_KEY = "lol.achievements";
        List<string> achUnlocked = new List<string>();
        WebStats stats = WebStats.Read(null);
        void init_C08()
        {
            try { achUnlocked = JsonSerializer.Deserialize<List<string>>(localStorage.getItem(ACH_KEY) ?? "null") ?? new List<string>(); } catch (Exception) { /* ignore */ }
            stats = WebStats.Read(localStorage.getItem(STATS_KEY));
        }
        bool statsDirty = false;
        void statsTouched() { statsDirty = true; }
        // extra counters for achievements (missing keys read as 0)
        void announce(IEnumerable<string> fresh) { foreach (var name in fresh ?? Enumerable.Empty<string>()) toast($"Achievement: {name}", "fx-toast-ach"); }
        void bump(string key, double n = 1)
        {
            if (engine == null || engine.meta == null) { stats[key] = stats[key] + n; statsTouched(); return; }
            announce(engine.metaBump(key, n));
            statsTouched();
        }
        void peak(string key, double v)
        {
            if (engine == null || engine.meta == null) { if (v > stats[key]) { stats[key] = v; statsTouched(); } return; }
            announce(engine.metaPeak(key, v));
            statsTouched();
        }
        /// <summary>The page's keys of a bestiary entry (entry.thumb / .drops / .loot), found by the entry's name.</summary>
        BeastNotes beastNotesOf(BestiaryEntry entry)
        {
            var book = engine != null && engine.meta != null ? engine.meta.bestiary : stats.bestiary;
            return stats.notesOf(book.FirstOrDefault(kv => kv.Value == entry).Key);
        }
        // A creature met before portraits were kept (or on a level whose sprites are not loaded now): its sprite
        // file is named after it (BOAR.SHP) in the pack of a level it was seen on; the front frame, once, in the
        // current palette (the level's own is not loaded - the colours can be a little off).
        readonly HashSet<string> beastThumbTried = new HashSet<string>();

        string backfillBeastThumb(string name, BestiaryEntry b)
        {
            if (engine == null || b == null || string.IsNullOrEmpty(name) || !beastThumbTried.Add(name)) return null;
            string file = name.ToUpperInvariant() + ".SHP";
            var levels = (b.levels ?? new List<int>()).Concat(new[] { b.level }).Where(l => l > 0).Distinct();
            // the packs of the levels it was seen on first, then every other pack (some creatures' sprites live in
            // a shared or neighbouring level's pack)
            var packs = levels.Select(l => Path.Combine(dataFolder ?? "", $"L{l:D2}.PAK")).ToList();
            try { packs.AddRange(Directory.GetFiles(dataFolder ?? "", "*.PAK").Where(f => !packs.Contains(f, StringComparer.OrdinalIgnoreCase))); } catch (Exception) { /* no folder */ }
            foreach (string pak in packs)
            {
                if (!File.Exists(pak)) continue;
                try
                {
                    var archive = new PakArchive(File.ReadAllBytes(pak));
                    if (!archive.has(file)) continue;
                    var shape = LolShapes.decodeShapeFile(Cps.decodeBitmapData(archive.get(file)).data).FirstOrDefault(sh => sh != null && sh.width > 0);
                    if (shape == null) continue;
                    var c = (CanvasEl)Dom.El("canvas"); c.width = shape.width; c.height = shape.height;
                    drawIconInto(c, shape, engine.uiPalette(), true);
                    string thumb = c.toDataURL("image/png");
                    stats.notesOf(name).thumb = thumb;
                    statsDirty = true;
                    return thumb;
                }
                catch (Exception error) { log($"Bestiary portrait for {name}: {error.Message}"); }
            }
            return null;
        }

        BestiaryEntry bestiaryMeet(Monster m)
        {
            var entry = engine.metaBestiaryMeet(m);
            var notes = beastNotesOf(entry);
            if (string.IsNullOrEmpty(notes.thumb))
            { // portrait: the monster's first (front) frame in the level palette
                try
                {
                    var shape = engine.monsterShapes[m.properties.shapeIndex << 4];
                    if (shape != null && shape.width != 0) { var c = (CanvasEl)Dom.El("canvas"); c.width = shape.width; c.height = shape.height; drawIconInto(c, shape, engine.uiPalette(), true); notes.thumb = c.toDataURL("image/png"); }
                }
                catch (Exception) { /* no portrait */ }
            }
            statsDirty = true;
            return entry;
        }
        void init_C09()
        {
            timers.setInterval(() =>
            {
                // document.hidden: the Unity window has no hidden-tab state; it counts as visible
                if (playing && engine != null) { stats.seconds += 5; statsDirty = true; checkAchievements(); }
                if (statsDirty)
                {
                    statsDirty = false;
                    try
                    {
                        localStorage.setItem(STATS_KEY, stats.ToJson().ToJsonString());
                        if (engine != null && engine.meta != null) localStorage.setItem(ACH_KEY, JsonSerializer.Serialize(engine.meta.unlocked));
                    }
                    catch (Exception) { /* ignore */ }
                }
            }, 5000);
        }
        // [name, description, test, icon]; icons are keys of ACH_ICONS
        static readonly Dictionary<string, string> ACH_ICONS = new Dictionary<string, string>
        {
            ["skull"] = "M12 2a8 8 0 00-8 8c0 3 2 5 3 6v3h10v-3c1-1 3-3 3-6a8 8 0 00-8-8zM9 10a2 2 0 110 4 2 2 0 010-4zm6 0a2 2 0 110 4 2 2 0 010-4zM9 19h6v3H9z",
            ["sword"] = "M4 20l4-4M6 22l-4-4 2-2 4 4zM8 16L19 5l1-3-3 1L6 14z",
            ["shield"] = "M12 2l8 3v6c0 5-3.5 9-8 11-4.5-2-8-6-8-11V5z",
            ["flame"] = "M12 2c1 4 5 6 5 11a5 5 0 01-10 0c0-2 1-3 2-5 0 2 1 3 2 3 1-2 0-6 1-9z",
            ["bolt"] = "M13 2L4 14h6l-1 8 9-12h-6z",
            ["heart"] = "M12 21s-8-5-8-11a4 4 0 018-2 4 4 0 018 2c0 6-8 11-8 11z",
            ["book"] = "M4 4h7a2 2 0 012 2v14a2 2 0 00-2-2H4zM20 4h-7a2 2 0 00-2 2v14a2 2 0 012-2h7z",
            ["map"] = "M3 6l6-2 6 2 6-2v14l-6 2-6-2-6 2zM9 4v14M15 6v14",
            ["boot"] = "M5 3h6v8l6 3a3 3 0 011 3v4H5z",
            ["coin"] = "M12 3a9 9 0 100 18 9 9 0 000-18zm0 4v10m-3-7h4a2 2 0 010 4h-2a2 2 0 000 4h4",
            ["bag"] = "M6 8h12l1 13H5zM9 8V6a3 3 0 016 0v2",
            ["scroll"] = "M6 3h12v14a2 2 0 01-2 2H6a2 2 0 01-2-2V7a2 2 0 012-2M8 8h8M8 12h8M8 16h5",
            ["eye"] = "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zm10-3a3 3 0 100 6 3 3 0 000-6z",
            ["crown"] = "M3 18h18l1-11-5 4-5-7-5 7-5-4z",
            ["clock"] = "M12 3a9 9 0 100 18 9 9 0 000-18zm0 4v5l3 3",
            ["camera"] = "M4 8h4l2-3h4l2 3h4v11H4zm8 3a3 3 0 100 6 3 3 0 000-6z",
            ["star"] = "M12 2l3 7 7 .6-5.3 4.6 1.7 7L12 17.5 5.6 21.2l1.7-7L2 9.6 9 9z",
            ["talk"] = "M4 4h16v11H9l-5 4z",
            ["snow"] = "M12 2v20M4 7l16 10M4 17l16-10",
            ["moon"] = "M20 14A8 8 0 1110 4a6 6 0 0010 10z",
            ["hand"] = "M7 11V5a1.5 1.5 0 013 0v5m0-6a1.5 1.5 0 013 0v6m0-5a1.5 1.5 0 013 0v6m0-3a1.5 1.5 0 013 0v5a7 7 0 01-7 7h-1a6 6 0 01-5-2.7L3 13a1.6 1.6 0 012.6-1.8L7 13",
            ["save"] = "M4 4h12l4 4v12H4zM8 4v5h7V4M7 20v-6h10v6",
            ["trophy"] = "M7 3h10v5a5 5 0 01-10 0zM5 5h2v3a3 3 0 01-2-3zm12 0h2a3 3 0 01-2 3zM10 13h4v4h3v3H7v-3h3z",
            ["wand"] = "M4 20l10-10m2-6l1 3 3 1-3 1-1 3-1-3-3-1 3-1zM6 4l.5 1.5L8 6l-1.5.5L6 8l-.5-1.5L4 6l1.5-.5z",
            ["key"] = "M15 3a6 6 0 00-5.6 8.2L2 18v3h3v-2h2v-2h2l2.6-2.6A6 6 0 1015 3zm1 4a2 2 0 110 4 2 2 0 010-4z",
            ["note"] = "M5 3h14v18l-3-2-2 2-2-2-2 2-2-2-3 2zM8 8h8M8 12h8",
            ["acid"] = "M12 2c3 5 6 8 6 12a6 6 0 01-12 0c0-4 3-7 6-12zM9 15a3 3 0 003 3",
            ["axe"] = "M4 20l9-9M13 11l-3-3 4-4a5 5 0 017 0l1 1-4 4a5 5 0 01-2 1z",
            ["target"] = "M12 3a9 9 0 100 18 9 9 0 000-18zm0 4a5 5 0 100 10 5 5 0 000-10zm0 4a1 1 0 100 2 1 1 0 000-2zM12 1v4M12 19v4M1 12h4M19 12h4",
            ["hammer"] = "M4 20l7-7M11 13l-2-2 5-5 2 2zM14 6l2-2 4 4-2 2M12 8l4 4",
            ["burst"] = "M12 3l1.5 4.5L18 6l-3 3.5L19 12l-4 1.5L16 18l-4-2-4 2 1-4.5L5 12l4-2.5L6 6l4.5 1.5z",
            ["hat"] = "M4 20h16l-2-3H6zM7 17l3-11h4l3 11M9 10h6",
            ["cross"] = "M10 3h4v7h7v4h-7v7h-4v-7H3v-4h7z",
            ["sun"] = "M12 7a5 5 0 100 10 5 5 0 000-10zM12 1v3M12 20v3M1 12h3M20 12h3M4.2 4.2l2.1 2.1M17.7 17.7l2.1 2.1M4.2 19.8l2.1-2.1M17.7 6.3l2.1-2.1",
            ["compass"] = "M12 3a9 9 0 100 18 9 9 0 000-18zm4-4l-6 8-3-3zM16 8l-3 6-4 2 2-4z",
            ["gem"] = "M6 3h12l4 6-10 12L2 9zM2 9h20M9 3l3 6 3-6M12 9l-4 12M12 9l4 12",
            ["bed"] = "M3 18V7M3 12h18v6M21 12V9a2 2 0 00-2-2h-8v5M5 10a2 2 0 104 0 2 2 0 00-4 0z",
            ["hourglass"] = "M6 2h12M6 22h12M7 2c0 5 5 7 5 10s-5 5-5 10M17 2c0 5-5 7-5 10s5 5 5 10",
            ["chest"] = "M3 8a3 3 0 013-3h12a3 3 0 013 3v12H3zM3 12h18M10 12v3h4v-3",
            ["flag"] = "M5 22V3M5 4h13l-3 4 3 4H5",
        };
        List<Character> heroes() => engine != null ? engine.characters.Where(c => (c.flags & 1) != 0).ToList() : new List<Character>();
        // The achievements live in the engine now (src/game/meta.mjs), so every build earns the same ones.
        // Here they are only drawn: [name, description, unlocked, icon], in the engine's own order.
        List<AchievementState> achievementList() => engine != null ? engine.metaAchievements() : new List<AchievementState>();
        void checkAchievements()
        {
            if (engine == null || !playing) return;
            foreach (var name in engine.metaCheckAchievements()) toast($"Achievement: {name}", "fx-toast-ach");
        }
        SvgEl achIcon(string key, int size = 22)
        {
            var svg = (SvgEl)Dom.El("svg");
            svg.SetAttr("viewBox", "0 0 24 24"); svg.SetAttr("width", size.ToString(CultureInfo.InvariantCulture)); svg.SetAttr("height", size.ToString(CultureInfo.InvariantCulture)); svg.AddToClassList("ach-icon");
            var path = SvgEl.Node("path");
            path.SetAttr("d", key != null && ACH_ICONS.TryGetValue(key, out var d) ? d : ACH_ICONS["star"]); path.SetAttr("fill", "none"); path.SetAttr("stroke", "currentColor"); path.SetAttr("stroke-width", "1.8"); path.SetAttr("stroke-linecap", "round"); path.SetAttr("stroke-linejoin", "round");
            svg.Append(path);
            return svg;
        }

        // Journal tabs
        void init_C10()
        {
            foreach (var tab in QAll("#journal-overlay .tab")) tab.On("click", () =>
            {
                foreach (var t in QAll("#journal-overlay .tab")) t.ClassToggle("active", t == tab);
                tab.Dataset().TryGetValue("tab", out string want);
                foreach (var page in QAll("#journal-overlay .tab-page")) { page.Dataset().TryGetValue("page", out string p); page.SetHidden(p != want); }
                journalTabShown(want);
            });
        }
        // Share card: a PNG summary of the run (Statistics tab).
        void shareCard()
        {
            if (engine == null) return;
            var c = (CanvasEl)Dom.El("canvas");
            c.width = 800; c.height = 420;
            var ctx = c.getContext("2d");
            var g = ctx.createLinearGradient(0, 0, 0, 420); g.addColorStop(0, "#1a1f18"); g.addColorStop(1, "#080b0c");
            ctx.fillStyle = g; ctx.fillRect(0, 0, 800, 420);
            ctx.strokeStyle = "#cab66e"; ctx.lineWidth = 3; ctx.strokeRect(8, 8, 784, 404);
            ctx.imageSmoothingEnabled = false;
            try { ctx.drawImage((CanvasEl)Q("#screen"), 20, 20, 352, 240); } catch (Exception) { /* ignore */ }
            ctx.fillStyle = "#f0d792"; ctx.font = "bold 30px Georgia, serif"; ctx.fillText("Open Lands", 392, 52);
            ctx.font = "16px Georgia, serif"; ctx.fillStyle = "#cab66e"; ctx.fillText("The Throne of Chaos", 392, 76);
            ctx.fillStyle = "#e3ce92"; ctx.font = "15px sans-serif";
            var heroes = engine.characters.Where(h => (h.flags & 1) != 0).Select(h => $"{h.name} (F{h.skillLevels[0]}/R{h.skillLevels[1]}/M{h.skillLevels[2]})");
            var rz = engine.randomizer;
            var lines = new List<string>
            {
                $"Party: {string.Join(", ", heroes)}",
                $"Location: {engine.levelName()} (level {engine.currentLevel}){(rz != null ? $" · randomizer seed \"{rz.seed}\"{(rz.ngplus != 0 ? $" NG+{rz.ngplus}" : "")}" : "")}",
                $"Time played: {N(Math.Floor(stats.seconds / 3600))}h {N(Math.Floor((stats.seconds % 3600) / 60))}m",
                $"Monsters slain: {N(stats.kills)} · damage dealt {N(stats.damageDealt)} · taken {N(stats.damageTaken)}",
                $"Achievements: {achievementList().Count(a => a.unlocked)} / {achievementList().Count}",
                $"Levels visited: {visited.Count} / 29",
            };
            for (int i = 0; i < lines.Count; i++) ctx.fillText(lines[i], 392, 110 + i * 26);
            ctx.fillStyle = "#8a8375"; ctx.font = "12px sans-serif"; ctx.fillText(DateTime.Now.ToShortDateString(), 392, 300);
            var q = LandsOfLore.questStates(engine); var active = LandsOfLore.QUESTS.Where(x => St(q, x.id) == "active").Select(x => x.title).ToList();
            ctx.fillStyle = "#ffd66b"; ctx.font = "14px sans-serif"; ctx.fillText($"Now: {(active.Count != 0 ? active[0] : "the story is done")}", 20, 300);
            ctx.fillStyle = "#b9ad8f"; ctx.font = "13px sans-serif";
            var got = achievementList().Where(a => a.unlocked).Take(6).ToList();
            for (int i = 0; i < got.Count; i++) ctx.fillText($"✔ {got[i].name}", 20 + (i % 3) * 250, 340 + (i / 3) * 22);
            // The browser downloads the PNG (an <a download> clicked): into Downloads, as it saves it.
            string url = c.toDataURL("image/png");
            try { FileDialog.Download($"lands-of-lore-card-{N(now())}.png", Convert.FromBase64String(url.Substring(url.IndexOf(',') + 1))); } catch (Exception) { /* nowhere to write */ }
            toast("Share card saved");
        }
        // img.src = url (Dom/DomImages.cs decodes and caches)
        static void imgSrc(VisualElement img, string url) => img.SetAttr("src", url);
        // Bestiary tab: portrait tiles, click one for its card.
        string beastSel = null;
        string beastFilter = "";
        // Unity build: picking a tile changes only the details card (and which tile is lit). Rebuilding the whole
        // page for it showed a frame of the new page half laid out: a tile flashing in the card's corner.
        VisualElement swapPending;   // a card being measured to take the shown one's place

        void swapCard(VisualElement box, Action<VisualElement, VisualElement> render, VisualElement picked)
        {
            // a click before the last swap finished: that card never shows, the one on screen is replaced
            swapPending?.RemoveFromHierarchy();
            swapPending = null;
            var oldCard = box.Q(className: "itemdb-card");
            var scratch = Dom.El("div");
            render(box, scratch);
            var newCard = scratch.Q(className: "itemdb-card");
            if (oldCard == null || newCard == null || oldCard.parent == null) { box.ReplaceChildren(scratch.Children().ToArray()); return; }
            var parent = oldCard.parent;
            foreach (var t in box.QAll(".itemdb-tile")) t.ClassToggle("on", t == picked);
            // its table is measured a layout pass later: the new card is laid out, invisible, over the old one, and
            // takes its place once whole (the card used to go blank for a few frames)
            var was = oldCard.layout;
            parent.Add(newCard);
            newCard.style.position = Position.Absolute;
            newCard.style.left = was.x; newCard.style.top = was.y; newCard.style.width = was.width;
            newCard.style.opacity = 0;
            CssLayout.Touch(newCard);
            swapPending = newCard;
            newCard.schedule.Execute(() =>
            {
                if (newCard.parent == null || swapPending != newCard) return;
                swapPending = null;
                newCard.style.position = StyleKeyword.Null;
                newCard.style.left = newCard.style.top = newCard.style.width = StyleKeyword.Null;
                newCard.style.opacity = StyleKeyword.Null;
                if (oldCard.parent == parent) { parent.Insert(parent.IndexOf(oldCard), newCard); oldCard.RemoveFromHierarchy(); }
            }).StartingIn(70);
        }

        void renderBestiary(VisualElement box) => renderBestiary(box, null);

        // into: where the page is built (the box itself, or a scratch element swapCard takes the card from)
        void renderBestiary(VisualElement box, VisualElement into)
        {
            into = into ?? box;
            // only slain kinds are listed: the entry is earned with the first kill
            var all = stats.bestiary.Where(kv => kv.Value.kills > 0).OrderBy(kv => kv.Value.level).ThenBy(kv => kv.Key, StringComparer.CurrentCulture).ToList();
            if (all.Count == 0) { into.ReplaceChildren(mkEl("p", "panel-note", "Nothing slain yet. A creature enters the bestiary with its first kill.")); return; }
            // The search reads a name, a family ("undead", "burning") or a reagent ("bone dust"), because
            // those are the three things you actually look a creature up by.
            string haystack(string name)
            {
                var family = LandsOfLore.familyOf(name);
                return string.Join(" ", new[] { name, family.name, family.id }.Concat(LandsOfLore.dropsFor(name).Select(d => d.name))).ToLowerInvariant();
            }
            var beasts = beastFilter.Length != 0 ? all.Where(kv => haystack(kv.Key).Contains(beastFilter)).ToList() : all;
            if (!beasts.Any(kv => kv.Key == beastSel)) beastSel = beasts.Count != 0 ? beasts[0].Key : null;
            VisualElement pic(string name, int size)
            {
                var img = Dom.El("img"); img.SetClassName("beast-pic"); img.SetStyle("height", size + "px"); img.SetAttr("alt", "");
                string thumb = stats.notesOf(name).thumb;
                if (string.IsNullOrEmpty(thumb)) thumb = backfillBeastThumb(name, all.FirstOrDefault(kv => kv.Key == name).Value);
                if (!string.IsNullOrEmpty(thumb)) imgSrc(img, thumb); else img.SetHidden(true);
                return img;
            }
            var tools = mkEl("div", "itemdb-tools");
            var search = (DomInput)Dom.El("input");
            search.SetAttr("type", "search"); search.SetAttr("placeholder", "Search by name, family or drop"); search.value = beastFilter;
            search.SetAttr("aria-label", "Search the bestiary");
            search.On("input", () =>
            {
                beastFilter = search.value.Trim().ToLowerInvariant();
                renderBestiary(box);
                var again = box.Q("input[type=search]") as DomInput;
                if (again != null) { again.FocusInput(); /* setSelectionRange: the field keeps its own caret */ }
            });
            tools.Append(search, mkEl("span", "itemdb-count", $"{beasts.Count} of {all.Count}"));
            var grid = mkEl("div", "itemdb-grid beast-grid");
            foreach (var kv in beasts)
            {
                string name = kv.Key;
                var tile = mkEl("button", "itemdb-tile" + (name == beastSel ? " on" : "")); tile.SetAttr("type", "button"); tile.SetTitle(name);
                tile.Append(pic(name, 48), mkEl("span", "", name));
                tile.On("click", () => { beastSel = name; swapCard(box, renderBestiary, tile); });
                grid.Append(tile);
            }
            if (beastSel == null) { var body0 = mkEl("div", "itemdb"); body0.Append(grid, mkEl("p", "panel-note", "Nothing matches that.")); into.ReplaceChildren(tools, body0); return; }
            var b = stats.bestiary[beastSel];
            var bn = stats.notesOf(beastSel);
            var card = mkEl("div", "itemdb-card");
            var head = mkEl("div", "itemdb-head"); head.Append(pic(beastSel, 120), mkEl("h3", "", beastSel));
            // stats as a small table
            var table = mkEl("table", "beast-stats");
            void stat(string k, string v, string cls = null) { var tr = mkEl("tr"); tr.Append(mkEl("th", "", k), mkEl("td", cls ?? "", v)); table.Append(tr); }
            stat("Hit points", N(b.hpMax));
            stat("Might", N(b.might));
            stat("Hit chance", N(b.hitChance) + "%");
            stat("Evade", N(b.evade));
            stat("Armour", N(b.protection)); // JS: only when defined; the engine's entry always has it
            if (!string.IsNullOrEmpty(b.danger)) stat("Danger", b.danger, "danger-" + b.danger);
            var track = mkEl("button", "ibtn hunt-track", stats.hunt == beastSel ? "Stop hunting" : "Hunt this");
            track.SetAttr("type", "button");
            track.On("click", () => { stats.hunt = stats.hunt == beastSel ? "" : beastSel; statsTouched(); renderHunt(); renderBestiary(box); });
            head.Append(track);
            var rowsEl = mkEl("dl", "itemdb-rows");
            void row(string k, string v) { if (!string.IsNullOrEmpty(v)) rowsEl.Append(mkEl("dt", "", k), mkEl("dd", "", v)); }
            var seenOn = (b.levels != null && b.levels.Count != 0 ? b.levels : new List<int> { b.level }).Where(l => l != 0);
            row("Found in", string.Join(", ", seenOn.Select(l => engine != null ? engine.levelName(l) : "Level " + l)));
            row("Slain", N(b.kills));
            if (settings.craft)
            {
                var seen = bn.drops ?? new List<string>();
                var family = LandsOfLore.familyOf(beastSel);
                var kin = stats.bestiary.Keys.Where(n => n != beastSel && LandsOfLore.familyOf(n).id == family.id).ToList();
                row("Family", $"{family.name}{(kin.Count != 0 ? $" · also {string.Join(", ", kin)}" : "")}");
                row("Leaves", string.Join(", ", LandsOfLore.dropsFor(beastSel).Select(d => $"{d.name} ({N(d.chance)}% per kill){(seen.Contains(d.key) ? " ✓" : "")}")));
            }
            if (bn.loot != null && bn.loot.Count != 0) row("Drops", string.Join(", ", bn.loot));
            var traits = b.traits ?? new[] { b.ranged ? "ranged attacks" : null, b.poison ? "poisons" : null, b.steals ? "thief" : null }.Where(t => t != null).ToList();
            row("Traits", traits.Count != 0 ? string.Join(", ", traits) : "none known");
            // damage taken per class, as a table (100% = normal; 0% = immune; negative = heals it)
            var dmg = mkEl("table", "beast-stats beast-dmg");
            var cap = mkEl("caption", "", "Damage taken"); dmg.Append(cap);
            foreach (var d in b.damageTaken ?? new List<DamageTakenEntry>())
            {
                var tr = mkEl("tr");
                string cls = d.pct < 0 ? "heals" : d.pct == 0 ? "immune" : d.pct < 75 ? "resist" : d.pct > 125 ? "weak" : "";
                var th = mkEl("th", "", d.kind); th.SetTitle(d.hint ?? ""); tr.Append(th, mkEl("td", cls, d.pct < 0 ? "heals it" : d.pct == 0 ? "immune" : d.pct + "%"));
                dmg.Append(tr);
            }
            card.Append(head, table, rowsEl);
            if (b.damageTaken != null) card.Append(dmg); else card.Append(mkEl("p", "panel-note", "Damage table: meet it again to record it."));
            var body = mkEl("div", "itemdb"); body.Append(grid, card);
            into.ReplaceChildren(tools, body);
        }
        void renderStats()
        {
            var box = Q("#journal-stats");
            var beastBox = Q("#journal-bestiary");
            var achBox = Q("#journal-achievements");
            if (box == null) return;
            // Unity build: a board instead of the table (Web.Stats.cs)
            renderStatsBoard(box);
            if (achBox != null)
            {
                var all = achievementList();
                int done = all.Count(a => a.unlocked);
                var grid = mkEl("div", "ach-grid");
                foreach (var a in all)
                {
                    var card = mkEl("div", "ach-card" + (a.unlocked ? " done" : ""));
                    card.Append(achIcon(a.icon, 26), mkEl("b", "", a.name), mkEl("span", "", a.description));
                    card.SetTitle(a.unlocked ? "Unlocked" : "Locked");
                    grid.Append(card);
                }
                achBox.ReplaceChildren(mkEl("p", "ach-count", $"{done} of {all.Count} unlocked"), grid);
            }
            if (beastBox != null) renderBestiary(beastBox);
        }
        // 320x200 playfield coordinates -> pixels inside .stage (only the scene window is shown when compact).
        (double, double) sceneToStage(double x, double y)
        {
            var c = Q("#screen");
            var rect = compactActive ? SCENE : new[] { 0, 0, 320, 200 };
            // offsetLeft/Top, clientWidth/Height: the canvas' box in its parent (.stage)
            return (c.layout.x + ((x - rect[0]) / rect[2]) * c.layout.width, c.layout.y + ((y - rect[1]) / rect[3]) * c.layout.height);
        }
        void floatText(double x, double y, string text, string cls)
        {
            if (fxLayer == null) return;
            var (sx, sy) = sceneToStage(x, y);
            var el = Dom.El("span");
            el.SetClassName($"fx-float {cls}");
            el.SetText(text);
            el.SetStyle("left", Px(sx));
            el.SetStyle("top", Px(sy));
            fxLayer.Append(el);
            el.On("animationend", () => el.RemoveFromHierarchy());
        }
        readonly Dictionary<int, VisualElement> monsterBars = new Dictionary<int, VisualElement>();
        // Weakness icons next to a monster's health bar, once its kind is in the bestiary (slain before).
        // Damage classes: 0 chopping, 1 blades, 2 blunt, 3 ice, 4 fire, 5 lightning, 6 acid, 7 magic.
        static readonly string[] WEAK_ICONS = { "axe", "sword", "hammer", "snow", "flame", "bolt", "acid", "wand" };
        VisualElement weakIcons(MonsterInfo info)
        {
            stats.bestiary.TryGetValue(info.name, out var entry);
            if (entry == null || entry.kills == 0 || info.damageTaken == null) return null;
            var weak = info.damageTaken.Select((d, i) => (d, i)).Where(p => p.d.pct > 125).ToList();
            var heals = info.damageTaken.Where(d => d.pct < 0).ToList();
            if (weak.Count == 0 && heals.Count == 0) return null;
            var box = Dom.El("span");
            box.SetClassName("mon-weak");
            foreach (var (d, i) in weak) { var ic = achIcon(i < WEAK_ICONS.Length ? WEAK_ICONS[i] : null, 14); ic.AddToClassList("weak"); ic.SetAttr("title", $"Weak to {d.hint}: {d.pct}% damage"); box.Append(ic); }
            foreach (var d in heals) { int i = info.damageTaken.IndexOf(d); var ic = achIcon(i < WEAK_ICONS.Length ? WEAK_ICONS[i] : null, 14); ic.AddToClassList("heals"); ic.SetAttr("title", $"Healed by {d.hint}!"); box.Append(ic); }
            return box;
        }
        // Warn once per monster when something much stronger than the party comes within a few blocks.
        readonly HashSet<string> dangerWarned = new HashSet<string>();
        void checkDanger()
        {
            if (engine == null || !playing || !compactActive) return;
            foreach (var m in engine.monsters)
            {
                if (m.properties == null || m.hitPoints <= 0 || m.mode >= 13 || m.mode == 1 || m.block == 0) continue; // mode 1: standing guards
                string key = $"{engine.currentLevel}:{m.id}:{m.type}";
                if (dangerWarned.Contains(key) || engine.getBlockDistance(engine.currentBlock, m.block) > 3) continue;
                var info = engine.monsterInfo(m);
                if (info.danger != "high") continue;
                dangerWarned.Add(key);
                checkpoint($"Before the {info.name.ToLowerInvariant()} on level {engine.currentLevel}");
                toast($"Danger: a {info.name.ToLowerInvariant()} is close by", "danger");
                gameUi.message($"A {info.name.ToLowerInvariant()} is nearby: {info.hpMax} HP, might {info.might}. Be careful.", "warn");
            }
        }
        // Party line-up threat: the hero nearest to each adjacent monster is the one it will hit.
        double threatWarnedAt = 0;
        void updateThreat()
        {
            if (engine == null || !playing) return;
            var targets = compactActive ? engine.uiThreatTargets() : new List<ThreatTarget>();
            var targeted = new HashSet<int>(targets.Select(t => t.target));
            int ci = 0;
            foreach (var card in gameUi.cards) { if (card != null) card.root.ClassToggle("threatened", targeted.Contains(ci)); ci++; }
            foreach (int c in targeted)
            {
                var ch = engine.characters[c];
                if (ch.hitPointsCur * 3 < ch.hitPointsMax && now() - threatWarnedAt > 20000)
                {
                    threatWarnedAt = now();
                    toast($"{ch.name} is wounded and stands nearest to the monster: swap the line-up", "danger");
                }
            }
        }
        void updateMonsterBars()
        {
            if (fxLayer == null) return;
            if ((questTick & 7) == 0) updateThreat();
            if ((questTick & 31) == 0) checkDanger();
            var seen = new HashSet<int>();
            if (engine != null && compactActive && engine.updateFlags == 0 && engine.needSceneRestore == 0 && engine.partyAwake && Q("#death-overlay").IsHidden())
            {
                foreach (var m in engine.monsters)
                {
                    if (m == null || m.properties == null || m.drawSerial != engine.sceneSerial || m.mode >= 13 || m.hitPoints <= 0) continue;
                    if (!monsterBars.TryGetValue(m.id, out var bar))
                    {
                        bar = Dom.El("div");
                        bar.SetClassName("mon-bar");
                        bar.Append(Dom.El("i"));
                        fxLayer.Append(bar);
                        monsterBars[m.id] = bar;
                    }
                    var (sx, sy) = sceneToStage(m.drawX, m.drawY);
                    var (sx2, _) = sceneToStage(m.drawX + (m.drawW >> 1), m.drawY);
                    double w = Math.Max(20, (sx2 - sx) * 1.2);
                    bar.SetStyle("left", Px(sx));
                    bar.SetStyle("top", Px(Math.Max(0, sy - 9)));
                    bar.SetStyle("width", Px(w));
                    int full = m.pitMaxHp != 0 ? m.pitMaxHp : m.properties.hitPoints;   // a pit monster: as the floor sized it
                    bar[0].SetStyle("width", $"{Math.Min(100, Js.Round((100.0 * m.hitPoints) / Math.Max(1, full)))}%");
                    // the floor's master says so over its bar
                    if (m.dungeonBoss != 0 && bar.Q(".mon-master") == null) bar.Append(Dom.El("span", "mon-master", "Floor master"));
                    // attack wind-up: an orange bar under the health bar fills up, then flashes red with a sword
                    double? threat = engine.monsterThreat(m);
                    var wind = bar.Q(".mon-wind");
                    if (threat == null) { if (wind != null) wind.RemoveFromHierarchy(); bar.RemoveFromClassList("striking"); }
                    else
                    {
                        if (wind == null)
                        {
                            wind = Dom.El("div");
                            wind.SetClassName("mon-wind");
                            // JS innerHTML: <b></b><span class="mon-wind-label"><svg viewBox="0 0 24 24"><use href="#i-sword"/></svg><em></em></span>
                            var label = Dom.El("span", "mon-wind-label", null, true);
                            label.Append(Dom.Icon("#i-sword"), Dom.El("em"));
                            wind.Append(Dom.El("b"), label);
                            bar.Append(wind);
                        }
                        int pct = Js.Round(threat.Value * 100);
                        wind.Q("b").SetStyle("width", $"{pct}%");
                        bool ready = threat.Value >= 0.95;
                        bar.ClassToggle("striking", ready);
                        wind.Q("em").SetText(ready ? "ATTACKING" : $"attack in {100 - pct}%");
                    }
                    string hpText = m.hitPoints.ToString(CultureInfo.InvariantCulture);
                    if (!bar.Dataset().TryGetValue("hp", out string oldHp) || oldHp != hpText)
                    {
                        bar.Dataset()["hp"] = hpText;
                        var info = engine.monsterInfo(m);
                        bar.SetClassName($"mon-bar {info.danger}");
                        bar.SetTitle($"{info.name}: {info.hp}/{info.hpMax} HP · might {info.might} · hits {info.hitChance}% · evade {info.evade} · armour {info.protection}{(info.traits.Count != 0 ? $" · {string.Join(" · ", info.traits)}" : "")}{(info.weak.Count != 0 ? $" · weak to {string.Join(", ", info.weak)}" : "")} · danger {info.danger}");
                        if (!bar.Dataset().TryGetValue("met", out string met) || met != "1") { bar.Dataset()["met"] = "1"; bestiaryMeet(m); }
                        var old = bar.Q(".mon-weak"); if (old != null) old.RemoveFromHierarchy();
                        var icons = weakIcons(info); if (icons != null) bar.Append(icons);
                    }
                    seen.Add(m.id);
                }
            }
            foreach (var kv in monsterBars.ToList()) if (!seen.Contains(kv.Key)) { kv.Value.RemoveFromHierarchy(); monsterBars.Remove(kv.Key); }
        }
    }
}
