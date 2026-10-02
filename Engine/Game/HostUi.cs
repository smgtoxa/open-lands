// src/game/host-ui.mjs (HostUiMixin): bridge between the engine and a host-drawn interface. The
// engine keeps drawing the original 320x200 playfield (so scripts, clicks and the faithful mode keep
// working); it additionally reports messages, dialogue text and dialogue choices through `this.ui`,
// and exposes actions the host interface needs (equipment slots, character selection, using items).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>host-ui autoWalkTo: { steps, hp }</summary>
    public sealed class AutoWalk
    {
        public List<int> steps;
        public int hp;
    }

    /// <summary>host-ui uiLostLoot / uiRecoverLostLoot / uiItemsOnBlock: { item, name } (+ fresh)</summary>
    public sealed class LostLoot
    {
        public int item;
        public string name;
        public bool fresh;
    }

    /// <summary>host-ui uiEffectTimers: { name, seconds }</summary>
    public sealed class EffectTimer
    {
        public string name;
        public int seconds;
    }

    /// <summary>host-ui merchantCache: { script, blocks }</summary>
    public sealed class MerchantCache
    {
        public EmcScript script;
        public List<int> blocks;
    }

    /// <summary>host-ui uiMerchantRect / uiGreetMerchant: { x, y } (scene coordinates)</summary>
    public sealed class ScenePoint
    {
        public int x, y;
    }

    /// <summary>host-ui uiWares: { x, y, type, name, price }</summary>
    public sealed class Ware
    {
        public int x, y, type;
        public string name;
        public int price;
    }

    /// <summary>host-ui uiLevelExits: { block, level, toBlock } (toBlock null when unknown)</summary>
    public sealed class LevelExit
    {
        public int block, level;
        public int? toBlock;
    }

    /// <summary>host-ui exitCache: { script, exits }</summary>
    public sealed class ExitCache
    {
        public EmcScript script;
        public List<LevelExit> exits;
    }

    /// <summary>host-ui uiShopItems: { item, block, price, name, info }</summary>
    public sealed class ShopItem
    {
        public int item, block, price;
        public string name;
        public ItemInfo info;
    }

    /// <summary>host-ui uiChestAhead: { block, wall, locked }</summary>
    public sealed class ChestInfo
    {
        public int block, wall;
        public bool locked;
    }

    /// <summary>host-ui uiItemsForSlot: { inv, item }</summary>
    public sealed class InventoryRef
    {
        public int inv, item;
    }

    /// <summary>host-ui uiFloorItems: { item, name, block, ahead }</summary>
    public sealed class FloorItem
    {
        public int item;
        public string name;
        public int block;
        public bool ahead;
    }

    /// <summary>host-ui monsterInfo damageTaken[]: { kind, hint, pct }</summary>
    public sealed class DamageTakenEntry
    {
        public string kind, hint;
        public int pct;
    }

    /// <summary>host-ui monsterInfo</summary>
    public sealed class MonsterInfo
    {
        public string name;
        public int hp, hpMax, might, hitChance, evade, protection;
        public string danger;
        public bool ranged, poison, steals;
        public List<string> traits, weak, resist, heals;
        public List<DamageTakenEntry> damageTaken;
    }

    /// <summary>host-ui uiThreatTargets: { monster, target }</summary>
    public sealed class ThreatTarget
    {
        public int monster, target;
    }

    /// <summary>host-ui uiPuzzleHint: { block, dist, name, door }</summary>
    public sealed class PuzzleHint
    {
        public int block, dist;
        public string name;
        public int door;
    }

    /// <summary>host-ui itemInfo / itemInfoForProperty (the latter leaves skill and condition null)</summary>
    public sealed class ItemInfo
    {
        public string name;
        public int might, protection;
        public List<string> slots;
        public bool usable;
        public string skill;
        public int prop;
        public string condition;
    }

    /// <summary>host-ui uiRepairCost: { wear, price }</summary>
    public sealed class RepairCost
    {
        public int wear, price;
    }

    /// <summary>host-ui uiRepairAll: { error } or { price }</summary>
    public sealed class RepairResult
    {
        public string error;
        public int price;
    }

    /// <summary>host-ui uiLevelView: the read-only level the map draws. The JS returns the engine itself for
    /// the current level; C# needs one type, so that case is this record filled from the engine.</summary>
    public sealed class LevelView
    {
        public LevelBlock[] levelBlockProperties;
        public byte[] wllAutomapData;
        public Monster[] monsters;
        public int currentLevel, currentBlock, currentDirection;
        public Func<int, BlockObject> findObject;
        public Func<List<int>> uiFrontier;
        public List<DefaultLegendData> defaultLegendData;
        public Func<int, string> getLangString;
        /// <summary>JS `(l = level) => ...`: pass the level explicitly</summary>
        public Func<int, string> levelName;
    }

    /// <summary>host-ui uiCharacterInfo skills.*: { level, points, next }</summary>
    public sealed class SkillExp
    {
        public int level, points, next;
    }

    /// <summary>host-ui uiCharacterInfo skills</summary>
    public sealed class CharacterSkills
    {
        public SkillExp fighter, rogue, mage;
    }

    /// <summary>host-ui uiCharacterInfo status</summary>
    public sealed class CharacterStatus
    {
        public bool poisoned, paralyzed, attacking, asleep, busy;
    }

    /// <summary>host-ui uiCharacterInfo weapon: { name, condition, left }</summary>
    public sealed class WeaponState
    {
        public string name, condition;
        public double left;
    }

    /// <summary>host-ui uiCharacterInfo equipment[]: { slot, item, name, label }</summary>
    public sealed class EquipmentSlot
    {
        public int slot, item;
        public string name, label;
    }

    /// <summary>host-ui uiCharacterInfo: everything the host needs to draw a character card.</summary>
    public sealed class CharacterInfo
    {
        public string name;
        public int id;
        public bool selected;
        public int hp, hpMax, mp, mpMax, might, protection, damageSuffered, weaponHit;
        public CharacterSkills skills;
        public CharacterStatus status;
        public double? cooldown;
        public WeaponState weapon;
        public List<EquipmentSlot> equipment;
    }

    public sealed partial class LandsOfLore
    {
        public AutoWalk autoWalk;
        public bool fastForward;
        public int uiDialogueCount;
        public MerchantCache merchantCache;
        public ExitCache exitCache;
        public int lastAskedType;
        public int greetedMerchant;
        public bool itemCastPending;
        public int? extraBase;

        // JS `arr[i]` that may be out of range (undefined): null instead of throwing.
        Item HostUi_itemAt(int i) => i >= 0 && i < itemsInPlay.Length ? itemsInPlay[i] : null;
        ItemProperty HostUi_propAt(int i) => i >= 0 && i < itemProperties.Count ? itemProperties[i] : null;

        // `{ ...m }` (a shallow copy: equipmentShapes stays shared unless the caller replaces it)
        static Monster HostUi_cloneMonster(Monster m) => m.Spread();

        // `{ ...it }`
        static Item HostUi_cloneItem(Item it) => it.Spread();

        public void initHostUi()
        {
            // Host implements any of: message(text, kind), dialogue(text), choices(labels | null),
            // waiting(bool) (a "more" page break or a single OK is pending).
            ui = null;
            autoWalk = null;
            fastForward = false; // set by skipCutscene(): TIM timelines, speech and "more" pages run through
        }

        // Skip the running cutscene/dialogue up to the next real choice. Returns false when nothing runs.
        public bool skipCutscene()
        {
            if (tim.currentTim == null && (updateFlags & 3) == 0 && needSceneRestore == 0) return false;
            if (dialogueNumButtons > 1) return false; // a question is being asked
            fastForward = true;
            snd_stopSpeech(true);
            return true;
        }

        public void uiEmit(string name, params object[] args)
        {
            if (name == "dialogue" || name == "choices") uiDialogueCount = uiDialogueCount + 1;
            if (ui != null)
            {
                try { ui.Invoke(name, args); }
                catch (QuitException) { throw; }
                catch (Exception error) { log($"ui.{name}: {error.Message}"); }
            }
        }

        // Answer the current dialogue with choice `index` (0-based) as if the button had been clicked.
        public void uiChoose(int index)
        {
            if (index >= 0 && index < dialogueNumButtons) dialogueHighlightedButton = index;
            events.Add(new InputEvent { type = "key", key = "Enter" });
        }

        // Put the item in hand into the first free inventory slot (the original: click the inventory strip).
        // Drop the item of inventory slot `slot` (or the hand item when slot is -1) on the floor at the
        // party's feet, the same way the original drops what is in hand.
        public async Task<int> uiDropToFloor(int slot = -1)
        {
            if ((updateFlags & 1) != 0 || needSceneRestore != 0 || sysTimerPaused) return 0;
            int held = itemInHand;
            if (slot >= 0 && inventory[slot] == 0) return 0;
            int item = slot >= 0 ? inventory[slot] : itemInHand;
            if (item == 0) return 0;
            string name = itemName(item);
            if (slot >= 0)
            {
                inventory[slot] = 0;
                if (held != 0) await setHandItem(0); // park the held item: the slot takes it back below
                await setHandItem(item);
            }
            await clickedSceneDropItem(new Button { arg = 3 }); // ahead, like the original Drop button
            if (itemInHand != 0) await clickedSceneDropItem(new Button { arg = 1 }); // blocked ahead: at the party's feet
            if (itemInHand != 0)
            { // nowhere to put it: back where it came from
                if (slot >= 0) { inventory[slot] = (ushort)itemInHand; await setHandItem(held != 0 ? held : 0); }
                gui_drawInventory();
                uiEmit("message", "There is no room to drop that here.", "system");
                return 0;
            }
            if (held != 0 && slot >= 0) await setHandItem(held);
            gui_drawInventory();
            uiEmit("message", $"{name} dropped.", "system");
            return 1;
        }

        // Respawn the level's monsters: the level's own init script (LEVEL<N>.INI, function 1) is the
        // code that places them when the level is entered for the first time, so this clears the living
        // ones and runs it again. Items, walls, doors and quest flags are untouched.
        public async Task<int> uiRespawnMonsters()
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || levelLoading) return 0;
            // A pit floor is generated over a borrowed level: putting that level's own monsters back would
            // take the floor's master with it, and an objective asking for the master would read as done.
            if (uiInDungeon()) return 0;
            Monster[] spawn = monsterSpawns != null && monsterSpawns.TryGetValue(currentLevel, out var sp) ? sp : null;
            int before = monsters.Count(m => m.properties != null && m.hitPoints > 0 && m.mode < 13);
            // Only the monsters the party killed come back; every other slot is the story's and is left
            // exactly as it is. The level's scripts place, move, spawn and remove monsters once, behind
            // flags that never clear, so a slot reset to the first visit breaks what they did:
            // - townsfolk (peaceful, mode 1 - Gladstone's guards): put back on their first posts, one stood
            //   in the corridor for good, another spawned on the party's block and was deleted, and the
            //   castle, finding its first guard dead, read as hostile and shut the throne room;
            // - the living (a monster a script spawned - the Dungeons' Xeob and Knowle Key carriers - is not
            //   in the first-visit set and was wiped with its key);
            // - the removed (suspendMonster: mode 14 with hit points left - killed ones have none).
            var kept = new Monster[30];
            for (int i = 0; i < 30; i += 1)
            {
                var live = monsters[i];
                bool npc = (live != null && live.mode == 1 && live.hitPoints > 0) || (spawn != null && spawn[i] != null && spawn[i].mode == 1);
                bool story = live != null && live.hitPoints > 0 && live.mode != 16 && (live.block != 0 || live.mode == 14);
                if (npc || story) kept[i] = live;
            }
            for (int i = 0; i < 30; i += 1)
            { // take the living ones off their blocks first
                var m = monsters[i];
                if (m != null && m.block != 0 && kept[i] == null) placeMonster(m, 0, 0);
            }
            if (spawn != null && spawn.Any(m => m.hitPoints > 0))
            {
                for (int i = 0; i < 30; i += 1)
                { // the level's original set, untouched by anything else
                    if (kept[i] != null) continue;
                    var src = spawn[i];
                    var m = HostUi_cloneMonster(src);
                    m.equipmentShapes = src.equipmentShapes.ToArray();
                    m.nextAssignedObject = 0;
                    m.nextDrawObject = 0;
                    var mp = monsterProperties.ElementAtOrDefault(m.type);
                    if (mp != null) m.properties = mp;
                    monsters[i] = m;
                    // what it carried on the first visit is not its any more: those item records were dropped
                    // when it died and may be in the party's hands now (a respawned monster that still named
                    // them took Ak'shel's mace out of his hand when it fell). It comes back empty-handed.
                    m.assignedItems = 0;
                    if (m.block != 0 && m.mode != 14 && m.hitPoints > 0) assignObjectToBlock(levelBlockProperties[m.block], m.id | 0x8000);
                }
            }
            else
            {
                if (await uiRespawnFromScript() == 0) return 0;
                for (int i = 0; i < 30; i += 1)
                { // the script made the kept slots anew: the ones there were take their places back
                    if (kept[i] == null) continue;
                    var fresh = monsters[i];
                    if (fresh != null && fresh.block != 0 && fresh.mode != 14 && fresh.hitPoints > 0) removeAssignedObjectFromBlock(levelBlockProperties[fresh.block], fresh.id | 0x8000);
                    monsters[i] = kept[i];
                    if (kept[i].block != 0 && kept[i].mode != 14 && kept[i].hitPoints > 0) assignObjectToBlock(levelBlockProperties[kept[i].block], kept[i].id | 0x8000);
                }
            }
            deleteMonstersFromBlock(currentBlock); // never on top of the party
            generateTempData(); // the fresh set is what a later visit restores
            sceneUpdateRequired = true;
            int after = monsters.Count(m => m.properties != null && m.hitPoints > 0 && m.mode < 13);
            uiEmit("message", $"Monsters respawned: {after} on this level (was {before}).", "system");
            return after;
        }

        // No snapshot (the level was already cleared when this build first saw it): run the level's own
        // spawn script again, with the map, the walls, the doors and the items put back exactly as they
        // were - only the monsters it creates are kept.
        public async Task<int> uiRespawnFromScript()
        {
            var walls = levelBlockProperties.Select(b => b.walls.ToArray()).ToArray();
            var flags = levelBlockProperties.Select(b => b.flags).ToArray();
            int itemCount = itemsInPlay.Length;
            var items = itemsInPlay.Select(it => it != null ? HostUi_cloneItem(it) : it).ToArray();
            var gameFlags = flagsTable.ToArray();
            for (int i = 0; i < 30; i += 1) monsters[i] = makeEmptyMonster(i);
            try
            {
                await runInitScript($"LEVEL{currentLevel}.INI", 1);
            }
            catch (QuitException) { throw; }
            catch (Exception)
            {
                uiEmit("message", "This level has no spawn script.", "system");
                return 0;
            }
            // undo everything the script touched except the monsters
            Js.Set(flagsTable, gameFlags);
            // (JS: itemsInPlay.length = itemCount - the array has a fixed length here)
            for (int i = 0; i < itemCount; i += 1) if (items[i] != null) itemsInPlay[i] = items[i];
            for (int i = 0; i < 1024; i += 1)
            {
                var b = levelBlockProperties[i];
                Js.Set(b.walls, walls[i]);
                b.flags = flags[i];
                b.assignedObjects = 0;
                b.drawObjects = 0;
            }
            addLevelItems(); // the level's items back on their blocks, exactly as after a level load
            for (int i = 0; i < 30; i += 1)
            { // and the freshly spawned monsters
                var m = monsters[i];
                if (m != null && m.block != 0 && m.mode != 14 && m.hitPoints > 0) assignObjectToBlock(levelBlockProperties[m.block], m.id | 0x8000);
            }
            monsterSpawns[currentLevel] = monsters.Select(m =>
            {
                var c = HostUi_cloneMonster(m);
                c.equipmentShapes = m.equipmentShapes.ToArray();
                c.nextAssignedObject = 0;
                c.nextDrawObject = 0;
                return c;
            }).ToArray();
            return 1;
        }

        // Loot that a fixed bug had dropped on block 0 (a monster's items were placed after the corpse
        // had been moved away). Lists what is stranded there, and moves it to the party's feet.
        public List<LostLoot> uiLostLoot()
        {
            var @out = new List<LostLoot>();
            // only what is really lying on block 0 (its object list), never inventory or equipment
            int cur = levelBlockProperties[0].assignedObjects;
            int guard = 0;
            while (cur != 0 && guard++ < 64)
            {
                int next = findObject(cur).nextAssignedObject;
                if ((cur & 0x8000) == 0 && HostUi_itemAt(cur) != null && itemsInPlay[cur].itemPropertyIndex != 0) @out.Add(new LostLoot { item = cur, name = itemName(cur) });
                cur = next;
            }
            return @out;
        }

        public async Task<int> uiRecoverLostLoot()
        {
            int phantoms = uiCleanPhantomItems();
            var lost = uiLostLoot();
            foreach (int prop in uiMissingQuestItems())
            { // quest items the save lost entirely
                int item = makeItem(prop, 0, 0);
                if (item != -1) lost.Add(new LostLoot { item = item, name = itemName(item), fresh = true });
            }
            if (lost.Count == 0 && phantoms != 0) { uiEmit("message", $"Cleared {phantoms} phantom item{(phantoms == 1 ? "" : "s")} left by the old bug.", "system"); return 0; }
            if (lost.Count == 0) { uiEmit("message", "No stranded loot on this level.", "system"); return 0; }
            var (x, y) = calcCoordinates(currentBlock, 0x40, 0x40);
            foreach (var l in lost)
            {
                if (!l.fresh) { removeAssignedObjectFromBlock(levelBlockProperties[0], l.item); removeDrawObjectFromBlock(levelBlockProperties[0], l.item); }
                await setItemPosition(l.item, x, y, 0, 1);
            }
            levelBlockProperties[currentBlock].direction = 5; // rebuild the draw order
            sceneUpdateRequired = true;
            uiEmit("message", $"Recovered {lost.Count} item{(lost.Count == 1 ? "" : "s")}: {string.Join(", ", lost.Select(l => l.name))}.", "system");
            return lost.Count;
        }

        // Entries in a block's object lists that point at an empty item slot ("Illegal Item"): a leftover
        // of the old monster-drop bug. Unlink them so the floor and the minimap stop showing phantoms.
        public int uiCleanPhantomItems()
        {
            int removed = 0;
            for (int b = 0; b < 1024; b += 1)
            {
                var l = levelBlockProperties[b];
                // JS: for (const key of ["assignedObjects", "drawObjects"]), nextKey the matching link
                for (int k = 0; k < 2; k += 1)
                {
                    bool assigned = k == 0;
                    int prev = 0; int cur = assigned ? l.assignedObjects : l.drawObjects; int guard = 0;
                    while (cur != 0 && guard++ < 64)
                    {
                        var obj = findObject(cur);
                        int next = assigned ? obj.nextAssignedObject : obj.nextDrawObject;
                        bool phantom = (cur & 0x8000) == 0 && (HostUi_itemAt(cur) == null || itemsInPlay[cur].itemPropertyIndex == 0 || (itemsInPlay[cur].shpCurFrame_flg & 0x8000) != 0);
                        if (phantom)
                        {
                            if (prev != 0) { var p = findObject(prev); if (assigned) p.nextAssignedObject = next; else p.nextDrawObject = next; }
                            else if (assigned) l.assignedObjects = next; else l.drawObjects = next;
                            if (assigned) obj.nextAssignedObject = 0; else obj.nextDrawObject = 0;
                            if (assigned) { itemsInPlay[cur].block = 0; removed += 1; }
                        }
                        else prev = cur;
                        cur = next;
                    }
                }
            }
            if (removed != 0) sceneUpdateRequired = true;
            return removed;
        }

        // Quest items a monster of this level should be carrying, but that the save has lost (the old
        // drop bug emptied the slot). Gives them back at the party's feet. The list is what the level's
        // own spawn scripts hand to monsters - see the giveItemToMonster calls in LEVEL<N>.INI/.INF.
        public List<int> uiMissingQuestItems()
        {
            var CARRIED = new Dictionary<int, int[]> { { 16, new[] { 273, 273 } }, { 11, new[] { 259 } }, { 17, new[] { 220, 125, 124, 124 } } }; // mines 4: two Bloodstones; swamp: ceremonial mask; upper Opinwood: ruby, vials
            var want = (CARRIED.TryGetValue(currentLevel, out var carried) ? carried : new int[0]).ToList();
            // Vaelan's Cube: block 517's script drops it at block 611 once flag 230 is set. If the flag is
            // set and the cube is nowhere, the save lost it - the script never hands it out twice.
            if (currentLevel == 16 && queryGameFlag(230) != 0) want.Add(264);
            if (want.Count == 0) return new List<int>();
            bool has(int prop)
            {
                bool check(int i) => i != 0 && HostUi_itemAt(i) != null && itemsInPlay[i].itemPropertyIndex == prop && (itemsInPlay[i].shpCurFrame_flg & 0x8000) == 0;
                if (inventory.Any(i => check(i)) || check(itemInHand)) return true;
                if (characters.Any(c => (c.flags & 1) != 0 && c.items.Any(i => check(i)))) return true;
                for (int i = 1; i < itemsInPlay.Length; i += 1) { var it = itemsInPlay[i]; if (it != null && it.itemPropertyIndex == prop && (it.shpCurFrame_flg & 0x8000) == 0 && (it.block != 0 || it.level == currentLevel)) return true; }
                return monsters.Any(m => { int a = m.assignedItems; int g = 0; while (a != 0 && g++ < 16) { if (check(a)) return true; a = itemsInPlay[a].nextAssignedObject; } return false; });
            }
            var @out = new List<int>();
            var seen = new Dictionary<int, int>();
            foreach (int prop in want)
            {
                seen[prop] = (seen.TryGetValue(prop, out int s) ? s : 0) + 1;
                if (seen[prop] == 1 && has(prop)) continue; // one of that kind is enough to say it is not lost
                if (seen[prop] > 1) continue;
                @out.Add(prop);
            }
            return @out;
        }

        public int uiStashHand()
        {
            if (itemInHand == 0) return 0;
            int slot = Array.IndexOf(inventory, (ushort)0);
            if (slot < 0) { uiEmit("message", "Your inventory is full.", "system"); return 0; }
            queueAsync(() => inventorySlotClick(slot));
            return 1;
        }

        // Grow or shrink the bag (multiples of 8 slots). Shrinking never drops items: it stops at the
        // last used slot.
        public void setInventorySize(int n)
        {
            int used = 0;
            for (int i = 0; i < inventory.Length; i += 1) if (inventory[i] != 0) used = i + 1;
            n = Math.Max(48, (int)Math.Ceiling(Math.Max(n, used) / 8.0) * 8);
            if (n == inventory.Length) return;
            var next = new ushort[n];
            Js.Set(next, Js.Slice(inventory, 0, Math.Min(n, inventory.Length)));
            inventory = next;
            if (inventoryCurItem >= n) inventoryCurItem = 0;
        }

        // Read-only view of another visited level for the map: its remembered walls/flags (the same
        // data the original automap pages through), its monsters and floor items. null if unknown.
        public async Task<LevelView> uiLevelView(int level)
        {
            if (level == currentLevel)
            {
                // JS: `return this` - the engine's own live state, as the same record type
                return new LevelView
                {
                    levelBlockProperties = levelBlockProperties, wllAutomapData = this.wllAutomapData, monsters = this.monsters, currentLevel = currentLevel,
                    currentBlock = currentBlock, currentDirection = currentDirection, findObject = findObject, uiFrontier = uiFrontier,
                    defaultLegendData = defaultLegendData, getLangString = getLangString, levelName = l => levelName(l),
                };
            }
            var t = lvlTempData != null && level - 1 >= 0 && level - 1 < lvlTempData.Length ? lvlTempData[level - 1] : null;
            if (t == null || (hasTempDataFlags & (1 << (level - 1))) == 0) return null;
            await res.loadPak($"L{level.ToString().PadLeft(2, '0')}.PAK");
            var file = res.get($"LEVEL{level}.WLL");
            var wllAutomapData = new byte[256];
            for (int i = 0, n = Js.FloorDiv(file.Length - 2, 12); i < n; i += 1) wllAutomapData[file[2 + i * 12] | (file[2 + i * 12 + 1] << 8)] = file[2 + i * 12 + 10];
            var blocks = t.walls.Select((walls, i) => new LevelBlock { walls = walls, flags = t.flags[i], assignedObjects = 0 }).ToArray();
            var objects = new Dictionary<int, int>(); // id -> { nextAssignedObject }
            void place(int id, int block) { objects[id] = blocks[block].assignedObjects; blocks[block].assignedObjects = id; }
            var monsters = t.monsters.Select(m => HostUi_cloneMonster(m)).ToArray();
            foreach (var m in monsters) if (m.block != 0 && m.mode != 14 && m.mode < 13 && m.hitPoints > 0) place(m.id | 0x8000, m.block);
            for (int i = 0; i < itemsInPlay.Length; i += 1) { var it = itemsInPlay[i]; if (i != 0 && it.level == level && it.block != 0 && (it.shpCurFrame_flg & 0x8000) == 0 && it.block < blocks.Length) place(i, it.block); }
            return new LevelView
            {
                levelBlockProperties = blocks, wllAutomapData = wllAutomapData, monsters = monsters, currentLevel = level, currentBlock = -1, currentDirection = 0,
                findObject = id =>
                {
                    if ((id & 0x8000) != 0)
                    {
                        var m = HostUi_cloneMonster(monsters[id & 0x7fff]);
                        if (objects.TryGetValue(id, out int na)) m.nextAssignedObject = na;
                        return m;
                    }
                    return new Item { nextAssignedObject = objects.TryGetValue(id, out int nx) ? nx : 0 };
                },
                uiFrontier = () => new List<int>(), defaultLegendData = defaultLegendData, getLangString = id => getLangString(id), levelName = l => levelName(l),
            };
        }

        // Timed effects for the host: seconds left on the frozen swamp (character update event 8, run
        // by timer 3 every 15 ticks). null when nothing is running.
        public List<EffectTimer> uiEffectTimers()
        {
            var @out = new List<EffectTimer>();
            if ((flagsTable[52] & 0x04) != 0)
            {
                int left = 0;
                foreach (var c in characters) if ((c.flags & 1) != 0) for (int i = 0; i < 5; i += 1) if (c.characterUpdateEvents[i] == 8) left = Math.Max(left, c.characterUpdateDelay[i]);
                if (left == 0) flagsTable[52] &= unchecked((byte)~0x04); // no thaw event pending (e.g. a save made mid-freeze): the effect is over
                else @out.Add(new EffectTimer { name = "Swamp frozen", seconds = Math.Max(1, Js.Round((left * 15 * tickLength) / 1000)) });
            }
            // potion buffs: events 9/10/11 are the fighter/rogue/mage potions running out
            string[] SKILL = { "Strength", "Agility", "Arcane" };
            foreach (var c in characters)
            {
                if ((c.flags & 1) == 0) continue;
                for (int i = 0; i < 5; i += 1)
                {
                    int type = c.characterUpdateEvents[i];
                    if (type < 9 || type > 11) continue;
                    @out.Add(new EffectTimer { name = $"{c.name}: {SKILL[type - 9]}", seconds = Math.Max(1, Js.Round((c.characterUpdateDelay[i] * 15 * tickLength) / 1000)) });
                }
            }
            return @out;
        }

        // ---- shops ----
        // A merchant is a block whose level-script function calls getItemPrice (the shopkeepers' scripts
        // price what you pick up from their counter and what you show them). Scanned once per level.
        public List<int> uiMerchantBlocks()
        {
            if (scriptData == null) return new List<int>();
            if (merchantCache != null && merchantCache.script == scriptData) return merchantCache.blocks;
            const int PRICE = 189; // OPCODE_NAMES.indexOf("getItemPrice")
            var ordr = scriptData.ordr; var data = scriptData.data;
            (int op, int param, int ip) decode(int ip)
            { // -> [op, param, nextIp]
                int code = (short)data[ip++];
                int op = (code >> 8) & 0x1f; int param = 0;
                if ((code & 0x8000) != 0) { op = 0; param = code & 0x7fff; } else if ((code & 0x4000) != 0) param = (sbyte)code; else if ((code & 0x2000) != 0) param = (short)data[ip++];
                return (op, param, ip);
            }
            // Does the code from ip (to end, or to its return) price something, directly or through the
            // subroutines it calls ("pushRet/call 1" followed by "jmp target")? Shops share helpers.
            var memo = new Dictionary<string, bool>();
            bool pricesFrom(int start, int end, int depth, bool helper = false)
            {
                string key = start + ":" + end + ":" + helper;
                if (memo.TryGetValue(key, out bool m)) return m;
                memo[key] = false;
                bool prevCall = false; bool found = false;
                for (int ip = start; ip < end && !found;)
                {
                    var (op, param, next) = decode(ip);
                    if (op == 14 && (param & 0xff) == PRICE) found = true;
                    else if (op == 0 && prevCall && depth < 4 && param > 0 && param < data.Length) found = pricesFrom(param, data.Length, depth + 1, true);
                    else if (helper && op == 8 && param == 1 && ip > start) break; // helper's return
                    prevCall = op == 2 && param == 1;
                    ip = next;
                }
                memo[key] = found;
                return found;
            }
            var starts = ordr.Select((o, i) => (o: (int)o, i)).Where(x => x.o != 0xffff).OrderBy(x => x.o).ToList();
            var blocks = new List<int>();
            for (int n = 0; n < starts.Count; n += 1)
            {
                var (start, func) = starts[n];
                int end = n + 1 < starts.Count ? starts[n + 1].o : data.Length;
                if (func < 1024 && pricesFrom(start + 1, end, 0)) blocks.Add(func);
            }
            merchantCache = new MerchantCache { script = scriptData, blocks = blocks };
            return blocks;
        }

        // Where to "click" the shopkeeper: the first mouse rectangle in the block's script that is
        // followed by a look at the item in hand (the exit button's rectangle is skipped). Scene
        // coordinates, or null when the script does not care where the click lands.
        public ScenePoint uiMerchantRect(int block)
        {
            if (scriptData == null) return null;
            var ordr = scriptData.ordr; var data = scriptData.data;
            if (block < 0 || block >= ordr.Length || ordr[block] == 0xffff) return null;
            int start = ordr[block];
            var starts = ordr.Select(o => (int)o).Where(o => o != 0xffff && o > start).OrderBy(o => o).ToList();
            int end = starts.Count > 0 ? starts[0] : data.Length;
            const int RECT = 33, HAND = 83; // checkRectForMousePointer, getItemInHand
            var pushes = new List<int>(); (int x, int y, int at)? rect = null;
            for (int ip = start + 1, n = 0; ip < end; n += 1)
            {
                int code = (short)data[ip++];
                int op = (code >> 8) & 0x1f; int param = 0;
                if ((code & 0x8000) != 0) { op = 0; param = code & 0x7fff; } else if ((code & 0x4000) != 0) param = (sbyte)code; else if ((code & 0x2000) != 0) param = (short)data[ip++];
                if (op == 3 || op == 4) { pushes.Add(param); if (pushes.Count > 4) pushes.RemoveAt(0); continue; }
                if (op == 14 && (param & 0xff) == RECT && pushes.Count == 4)
                {
                    int y2 = pushes[0], x2 = pushes[1], y1 = pushes[2], x1 = pushes[3]; // pushed last-argument-first
                    if (!(x1 == 250 && y1 == 104)) rect = ((x1 + x2) >> 1, (y1 + y2) >> 1, n);
                }
                else if (op == 14 && (param & 0xff) == HAND && rect != null && n - rect.Value.at < 12) return new ScenePoint { x = rect.Value.x, y = rect.Value.y };
                pushes.Clear();
            }
            return null;
        }

        // Wares a shopkeeper offers by pointing at them in the scene window: every mouse rectangle in
        // the block's script that leads to a subroutine call whose first argument is an item type.
        // Returns [{ x, y, type, name }] (scene coordinates of the rectangle's centre).
        public List<Ware> uiWares(int block)
        {
            if (scriptData == null) return new List<Ware>();
            var ordr = scriptData.ordr; var data = scriptData.data;
            if (block < 0 || block >= ordr.Length || ordr[block] == 0xffff) return new List<Ware>();
            int start = ordr[block];
            var starts = ordr.Select(o => (int)o).Where(o => o != 0xffff && o > start).OrderBy(o => o).ToList();
            int end = starts.Count > 0 ? starts[0] : data.Length;
            const int RECT = 33;
            var @out = new List<Ware>(); var pushes = new List<int>(); var args = new List<int>(); (int x, int y, int at)? rect = null;
            int? arg(int i) => i >= 0 && i < args.Count ? args[i] : (int?)null;
            for (int ip = start + 1, n = 0; ip < end; n += 1)
            {
                int code = (short)data[ip++];
                int op = (code >> 8) & 0x1f; int param = 0;
                if ((code & 0x8000) != 0) { op = 0; param = code & 0x7fff; } else if ((code & 0x4000) != 0) param = (sbyte)code; else if ((code & 0x2000) != 0) param = (short)data[ip++];
                if (op == 3 || op == 4) { pushes.Add(param); if (pushes.Count > 4) pushes.RemoveAt(0); args.Add(param); continue; }
                if (op == 14 && (param & 0xff) == RECT && pushes.Count == 4)
                {
                    int y2 = pushes[0], x2 = pushes[1], y1 = pushes[2], x1 = pushes[3];
                    rect = x1 == 250 && y1 == 104 ? null : ((x1 + x2) >> 1, (y1 + y2) >> 1, n);
                    args.Clear();
                }
                else if (op == 2 && param == 1 && rect != null && n - rect.Value.at < 16)
                {
                    // the offer helpers take (flag, itemType, price, ...): the item is the second argument,
                    // i.e. the push before the last one (the last is the "already bought" game flag)
                    var cand = new[] { arg(args.Count - 2), arg(args.Count - 1) }.Where(t => t > 0 && HostUi_propAt(t.Value) != null && itemProperties[t.Value].nameStringId != 0 && !string.IsNullOrEmpty(getLangString(itemProperties[t.Value].nameStringId))).Select(t => t.Value).ToList();
                    int? price = arg(args.Count - 3);
                    if (cand.Count > 0 && !(price > 0 && price < 5)) { @out.Add(new Ware { x = rect.Value.x, y = rect.Value.y, type = cand[0], name = getLangString(itemProperties[cand[0]].nameStringId), price = price > 0 && price < 5000 ? price.Value : 0 }); rect = null; }
                }
                pushes.Clear();
            }
            return @out;
        }

        // Ask the merchant about a ware: the click lands on it in the scene window and the script
        // names the price (YES/NO follows, like the original).
        public async Task<int> uiAsk(int merchant, Ware ware)
        {
            // no scene guards: inside a shop window updateFlags/needSceneRestore are set by design
            if (itemInHand != 0) { uiEmit("message", "Empty your hand first.", "system"); return 0; }
            lastAskedType = ware.type;
            await uiClickScene(ware.x, ware.y); // exactly what a click on it in the scene window does
            return 1;
        }

        sealed class HostUi_ExitPush { public int? v, bp; }
        sealed class HostUi_ExitScan { public int? level, block, argLevel, argBlock; }

        // Level exits: blocks whose script (or a subroutine it calls) loads another level. The
        // destination is read from the pushes right before the loadNewLevel call when it is in the
        // block's own code (level = last push, block = the one before). Cached per level.
        public List<LevelExit> uiLevelExits()
        {
            if (scriptData == null) return new List<LevelExit>();
            if (exitCache != null && exitCache.script == scriptData) return exitCache.exits;
            const int LOAD = 60; // OPCODE_NAMES.indexOf("loadNewLevel")
            var ordr = scriptData.ordr; var data = scriptData.data;
            (int op, int param, int ip) decode(int ip) { int code = (short)data[ip++]; int op = (code >> 8) & 0x1f; int param = 0; if ((code & 0x8000) != 0) { op = 0; param = code & 0x7fff; } else if ((code & 0x4000) != 0) param = (sbyte)code; else if ((code & 0x2000) != 0) param = (short)data[ip++]; return (op, param, ip); }
            var memo = new Dictionary<string, HostUi_ExitScan>();
            // (unused in the JS too) Math.min(...) of the ORDR entries
            double firstFunc = ordr.Where(o => o != 0xffff).Select(o => (double)o).DefaultIfEmpty(double.PositiveInfinity).Min();
            // Subroutines are not in ORDR: they sit back to back below the block functions, so one ends
            // where the next call target begins. Bounding them by the next ORDR entry instead would let a
            // scan run on into the next subroutine and credit a block with an exit it never takes.
            var entries = new List<int>();
            for (int ip = 0, wasCall = 0; ip < data.Length;)
            {
                var (op, param, next) = decode(ip);
                if (op == 0 && wasCall != 0 && param > 0 && param < data.Length) entries.Add(param);
                wasCall = op == 2 && param == 1 ? 1 : 0;
                ip = next;
            }
            foreach (var o in ordr) if (o != 0xffff) entries.Add(o + 1);
            entries.Sort();
            int helperEnd(int at) { int i = entries.FindIndex(o => o > at); return i < 0 ? data.Length : entries[i]; }
            // Scan code for a loadNewLevel. Returns null, or { level, block } with numbers when the
            // arguments are literal pushes, or argument slots ({ argLevel, argBlock }: k-th value from the
            // end of the caller's pushes) when a subroutine takes them as parameters.
            HostUi_ExitScan scan(int start, int end, int depth, bool follow = true)
            {
                string key = start + ":" + end + ":" + follow;
                if (memo.TryGetValue(key, out var memoed)) return memoed;
                memo[key] = null;
                var pushes = new List<HostUi_ExitPush>(); bool prevCall = false; HostUi_ExitScan found = null; var callPushes = new List<HostUi_ExitPush>();
                for (int ip = start; ip < end && found == null;)
                {
                    var (op, param, next) = decode(ip);
                    if (op == 3 || op == 4) pushes.Add(new HostUi_ExitPush { v = param });
                    else if (op == 7) pushes.Add(new HostUi_ExitPush { bp = param });
                    else if (op == 5 || op == 6) pushes.Add(new HostUi_ExitPush());
                    else
                    {
                        if (op == 14 && (param & 0xff) == LOAD)
                        {
                            var lv = pushes.Count >= 1 ? pushes[pushes.Count - 1] : new HostUi_ExitPush(); var bl = pushes.Count >= 2 ? pushes[pushes.Count - 2] : new HostUi_ExitPush();
                            found = new HostUi_ExitScan { level = lv.v, block = bl.v, argLevel = lv.bp, argBlock = bl.bp };
                        }
                        else if (follow && op == 0 && prevCall && depth < 4 && param > 0 && param < data.Length)
                        {
                            var r = scan(param, helperEnd(param), depth + 1);
                            if (r != null)
                            {
                                var cp = callPushes;
                                HostUi_ExitPush pick(int? k) => k != null && k != 0 && cp.Count >= k ? cp[cp.Count - k.Value] : new HostUi_ExitPush();
                                found = new HostUi_ExitScan
                                {
                                    level = r.level != null ? r.level : pick(r.argLevel).v, block = r.block != null ? r.block : pick(r.argBlock).v,
                                    argLevel = r.argLevel != null && r.argLevel != 0 ? pick(r.argLevel).bp : null, argBlock = r.argBlock != null && r.argBlock != 0 ? pick(r.argBlock).bp : null,
                                };
                            }
                        }
                        if (op == 2 && param == 1) callPushes = pushes; // arguments of the call that follows
                        pushes = new List<HostUi_ExitPush>();
                    }
                    prevCall = op == 2 && param == 1;
                    ip = next;
                }
                memo[key] = found;
                return found;
            }
            var starts = ordr.Select((o, i) => (o: (int)o, i)).Where(x => x.o != 0xffff).OrderBy(x => x.o).ToList();
            var exits = new List<LevelExit>();
            for (int n = 0; n < starts.Count; n += 1)
            {
                var (start, func) = starts[n];
                int end = n + 1 < starts.Count ? starts[n + 1].o : data.Length;
                if (func >= 1024) continue;
                // the block's own loadNewLevel first; a subroutine's only when it has none
                var r = scan(start + 1, end, 0, false) ?? scan(start + 1, end, 0, true);
                // only resolved destinations to another level: unresolved helpers are mostly traps and deaths
                if (r != null && r.level > 0 && r.level <= 29 && r.level != currentLevel) exits.Add(new LevelExit { block = func, level = r.level.Value, toBlock = r.block > 0 ? r.block : null });
            }
            exitCache = new ExitCache { script = scriptData, exits = exits };
            return exits;
        }

        // The merchant the party faces (block ahead), or -1.
        public int uiMerchantAhead()
        {
            if (currentBlock == 0) return -1;
            var blocks = uiMerchantBlocks();
            int ahead = calcNewBlockPosition(currentBlock, currentDirection);
            if (blocks.Contains(ahead)) return ahead;
            if (blocks.Contains(currentBlock)) return currentBlock;
            for (int d = 0; d < 4; d += 1) { int nb = calcNewBlockPosition(currentBlock, d); if (blocks.Contains(nb)) return nb; }
            return -1;
        }

        // What the price ladder makes of an item's base price (getItemPrice opcode).
        public int uiPriceOf(int item)
        {
            int @base = itemProperties[itemsInPlay[item].itemPropertyIndex].unkB;
            for (int i = 0; i < 46; i += 1) if (@static.ItemPrices[i] >= @base) return @static.ItemPrices[i];
            return 0;
        }

        // Items on the merchant's counter: the merchant block and its neighbours (not the party's block).
        public List<ShopItem> uiShopItems(int merchant)
        {
            var @out = new List<ShopItem>();
            var blocks = new[] { merchant, 0, 1, 2, 3 }.Select((d, i) => i == 0 ? merchant : calcNewBlockPosition(merchant, d)).Where(b => b != currentBlock);
            foreach (int b in blocks.Distinct())
            {
                int o = levelBlockProperties[b].assignedObjects; int guard = 0;
                while (o != 0 && (o & 0x8000) == 0 && guard++ < 64)
                {
                    var it = HostUi_itemAt(o);
                    if (it != null && it.itemPropertyIndex != 0) @out.Add(new ShopItem { item = o, block = b, price = uiPriceOf(o), name = itemName(o), info = itemInfo(o) });
                    o = it != null ? it.nextAssignedObject : 0;
                }
            }
            return @out;
        }

        // Buy: taking the item off the counter is what makes the merchant name the price (level script,
        // event 0x100), so this is the original flow with the item chosen from a list.
        public async Task<int> uiBuy(int item, int block)
        {
            if (itemInHand != 0) { uiEmit("message", "Empty your hand first.", "system"); return 0; }
            if (itemsInPlay[item].block != block) return 0;
            await removeLevelItem(item, block); // the merchant script asks the price here
            if (itemsInPlay[item].block != 0) { sceneUpdateRequired = true; return 0; } // declined: the script put it back
            await setHandItem(item);
            sceneUpdateRequired = true;
            return 1;
        }

        // Greet the merchant if their dialogue script is not loaded yet (the first click on them);
        // the host fast-forwards the greeting.
        public async Task uiGreetMerchant(int merchant)
        {
            int key = currentLevel * 1024 + merchant;
            if (greetedMerchant == key || itemInHand != 0 || needSceneRestore != 0) return;
            greetedMerchant = key;
            var at = uiMerchantRect(merchant) ?? new ScenePoint { x = 160, y = 60 };
            await uiClickScene(at.x, at.y);
        }

        // A left click in the scene at 320x200 coordinates, through the engine's own input queue (so
        // the button list of the moment - playfield, shop window, dialogue - decides what it means).
        public Task uiClickScene(int x, int y)
        {
            if (needSceneRestore != 0)
            { // a shop window is open: its own button list handles the click
                pushMouse(x, y, 1);
                events.Add(new InputEvent { type = "mouseup", x = x, y = y, button = 1 });
                return Task.CompletedTask;
            }
            // plain scene: the same thing a click on the wall shape ahead ends in
            mouseX = x; mouseY = y;
            return runLevelScript(calcNewBlockPosition(currentBlock, currentDirection), 0x40);
        }

        // Sell: show the merchant the item (the original: click the shopkeeper with it in hand).
        public async Task<int> uiSell(int invSlot, int merchant)
        {
            if (itemInHand != 0 || inventory[invSlot] == 0) return 0;
            var at = uiMerchantRect(merchant);
            // No rectangle means his script never asks for a click with something in hand, so there is
            // nowhere to offer it. Clicking a guessed point does not reach him - it reaches whatever the
            // artist drew there - so put the item in the hand and let the player click him themselves,
            // which is the original's own way of selling.
            if (at == null)
            {
                await inventorySlotClick(invSlot);
                return itemInHand != 0 ? 2 : 0;
            }
            await inventorySlotClick(invSlot);
            if (itemInHand == 0) return 0;
            await uiClickScene(at.x, at.y); // the original: click the shopkeeper with the item in hand
            // declined or not wanted: the item stays in hand (Stash puts it back)
            return 1;
        }

        // The palette the host should paint with: what is on screen (after fades, the ice swap...),
        // never the working palette 0 that the swamp spell swaps around. Falls back to palette 0 while
        // the screen is faded to black so icons do not disappear.
        public byte[] uiPalette()
        {
            // Unity build: the level's own colours (palette 0), never the screen's: a lantern's dark, a fade or a spell's
            // flash on the screen palette left the icons dim, white or bleached until they were drawn again
            return screen.getPalette(0);
        }

        // Is a spell really waiting for its hero? Only the cast that raised the prompt counts, so a
        // leftover flag can never swallow plain clicks on the party cards.
        public bool uiTargetPending()
        {
            return awaitingSpellTarget && spellTargetSpell != null && spellTargetSpell == activeSpell;
        }

        public void uiClearTarget()
        {
            awaitingSpellTarget = false;
            spellTargetSpell = null;
            uiEmit("target", false);
        }

        // The pending spell (Heal) goes on hero c.
        public void uiCastOn(int c)
        {
            if (!uiTargetPending()) { uiClearTarget(); return; }
            if ((characters[c].flags & 1) != 0) queueAsync(() => clickedSpellTargetCharacter(new Button { arg = c }));
            else uiClearTarget();
        }

        // Give the spell up: the mana comes back (the original's "cast on the scene" outcome).
        public void uiCancelTarget()
        {
            if (!awaitingSpellTarget) return;
            if (uiTargetPending()) queueAsync(() => clickedSpellTargetScene());
            else { uiClearTarget(); gui_enableDefaultPlayfieldButtons(); }
        }

        // A chest in the wall ahead (automap symbol 18): { locked } or null.
        public ChestInfo uiChestAhead()
        {
            if (currentBlock == 0) return null;
            int ahead = calcNewBlockPosition(currentBlock, currentDirection);
            int wall = levelBlockProperties[ahead].walls[currentDirection ^ 2];
            if (wall == 0 || (wllAutomapData[wall] & 0x1f) != 18) return null;
            return new ChestInfo { block = ahead, wall = wall, locked = wall == 56 || wall == 59 }; // the chest helpers' locked wall types
        }

        // Search the chest ahead: the same click as on its lid (with an empty hand it hands over what is
        // inside; with lockpicks or a key in hand it works the lock).
        public async Task<int> uiSearchChest()
        {
            var chest = uiChestAhead();
            if (chest == null) return 0;
            mouseX = 200; mouseY = 90;
            await runLevelScript(chest.block, 0x40);
            return 1;
        }

        // Rotate the marching order: the first hero goes to the back.
        public bool uiRotateParty()
        {
            var active = new[] { 0, 1, 2, 3 }.Where(i => (characters[i].flags & 1) != 0).ToList();
            if (active.Count < 2) return false;
            for (int i = 0; i + 1 < active.Count; i += 1) if (!uiSwapParty(active[i], active[i + 1])) return false;
            return true;
        }

        // Select hero c. Nothing but an absent hero can refuse: a click on a party card always lands,
        // whatever the engine is doing (a pending spell target is answered instead, see uiTargetPending).
        public void uiSelectCharacter(int c)
        {
            if ((characters[c].flags & 1) == 0) return;
            selectionPinned = c;
            // "Cast a spell on who?" - but only while the spell is really pending; a leftover flag must
            // never swallow plain clicks on the party cards.
            if (uiTargetPending()) { uiCastOn(c); return; }
            if (awaitingSpellTarget) uiClearTarget(); // stale flag: fall through and select
            selectionPinned = c; // the player's choice holds until this hero acts
            if (c == selectedCharacter) return;
            gui_highlightPortraitFrame(c);
        }

        // Swap the hand item with equipment slot `slot` of character `c` (clickedCharInventorySlot rules).
        public async Task<int> uiEquipSlot(int c, int slot)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || (characters[c].flags & 1) == 0) return 0;
            uiSelectCharacter(c);
            return await clickedCharInventorySlot(new Button { arg = slot });
        }

        // Use the hand item on character `c` (potions, herbs, scrolls, ...).
        public async Task<int> uiUseHandOn(int c)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || (characters[c].flags & 1) == 0) return 0;
            // Equipment (weapons, armour, rings...) is not "used": put it into the matching slot instead.
            if (itemInHand != 0)
            {
                var prop = itemProperties[itemsInPlay[itemInHand].itemPropertyIndex];
                if ((prop.flags & 1) == 0 && prop.type != 0)
                {
                    int @base = @static.CharInvIndex[characters[c].raceClassSex] * 22;
                    var slots = new List<int>();
                    for (int i = 0; i < 11; i += 1) if ((prop.type & (1 << i)) != 0 && @static.CharInvDefs[@base + i * 2] != 0xff) slots.Add(i);
                    int ei = slots.FindIndex(i => characters[c].items[i] == 0);
                    int? slot = ei < 0 ? (slots.Count > 0 ? slots[0] : (int?)null) : slots[ei];
                    if (slot != null) return await uiEquipSlot(c, slot.Value);
                }
            }
            return await clickedPortraitEtcRight(new Button { arg = c });
        }

        // Inventory items that fit equipment slot `slot` of character `c` (for the slot picker).
        public List<InventoryRef> uiItemsForSlot(int c, int slot)
        {
            var @out = new List<InventoryRef>();
            for (int i = 0; i < inventory.Length; i += 1)
            {
                int item = inventory[i];
                if (item != 0 && uiSlotsFor(item, c).Contains(slot)) @out.Add(new InventoryRef { inv = i, item = item });
            }
            return @out;
        }

        // Take the worn item out of `slot` into a free inventory slot (into the hand when the bag is full).
        public async Task<int> uiUnequip(int c, int slot)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || weaponsDisabled || itemInHand != 0) return 0;
            var ch = characters[c];
            if ((ch.flags & 1) == 0 || ch.items[slot] == 0) return 0;
            await uiEquipSlot(c, slot); // empty hand + click = take the item into the hand
            if (itemInHand != 0)
            {
                int free = Array.IndexOf(inventory, (ushort)0);
                if (free >= 0) { inventory[free] = (ushort)itemInHand; await setHandItem(0); gui_drawInventory(); }
            }
            return 1;
        }

        // Drag-and-drop from the inventory: put inventory item `invSlot` into equipment slot `slot` of
        // character `c` (or, with slot -1, wherever it fits / use it). Whatever was equipped goes back
        // into the inventory slot, so the hand stays as it was.
        public async Task<int> uiDropInventoryOn(int invSlot, int c, int slot = -1)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || weaponsDisabled) return 0;
            int item = inventory[invSlot];
            if (item == 0 || itemInHand != 0 || (characters[c].flags & 1) == 0) return 0;
            if (slot >= 0)
            {
                if (!uiSlotsFor(item, c).Contains(slot))
                {
                    txt.printMessage(0, $"{itemName(item)} does not fit there.");
                    return 0;
                }
            }
            inventory[invSlot] = 0;
            await setHandItem(item);
            if (slot >= 0) await uiEquipSlot(c, slot);
            else await uiUseHandOn(c);
            if (itemInHand != 0)
            { // the swapped-out (or unused) item goes back where the dragged one was
                inventory[invSlot] = (ushort)itemInHand;
                await setHandItem(0);
            }
            gui_drawInventory();
            return 1;
        }

        // Use the item in inventory slot `slot` on character `c` without leaving it in the hand.
        public async Task<int> uiUseInventorySlot(int slot, int c)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || weaponsDisabled) return 0;
            if (inventory[slot] == 0 || itemInHand != 0) return 0;
            int item = inventory[slot];
            var extra = uiExtraItem(item);
            if (extra != null)
            { // the port's own potions: no item script, the effect is applied here
                string said = uiUseExtraItem(extra, c);
                if (string.IsNullOrEmpty(said)) { uiEmit("message", $"{characters[c].name} has no use for that right now.", "system"); return 0; }
                inventory[slot] = 0;
                deleteItem(item);
                gui_drawInventory();
                uiEmit("message", said, "combat");
                return 1;
            }
            inventory[slot] = 0;
            await setHandItem(item);
            await clickedPortraitEtcRight(new Button { arg = c });
            if (c == selectionPinned) selectionPinned = -1;
            if (itemInHand == item)
            { // not consumed: put it back
                inventory[slot] = (ushort)item;
                await setHandItem(0);
            }
            gui_drawInventory();
            return 1;
        }

        // The game never names monsters; the sprite file (GUARD.SHP, ORC.SHP...) is the best label there is.
        public string monsterName(Monster m)
        {
            string file = m.properties != null && monsterShapeNames != null ? monsterShapeNames.ElementAtOrDefault(m.properties.shapeIndex) : "";
            string @base = Regex.Replace(Regex.Replace(string.IsNullOrEmpty(file) ? "monster" : file, @"\.SHP$", "", RegexOptions.IgnoreCase), @"\d+$", "").ToLowerInvariant();
            return (@base.Length > 0 ? @base.Substring(0, 1).ToUpperInvariant() : "") + (@base.Length > 1 ? @base.Substring(1) : "");
        }

        // Everything the host shows about a monster (health bar tooltip, bestiary).
        public MonsterInfo monsterInfo(Monster m)
        {
            if (m == null || m.properties == null) return null;
            var p = m.properties;
            int partyHp = characters.Sum(c => (c.flags & 1) != 0 ? c.hitPointsMax : 0);
            if (partyHp == 0) partyHp = 1;
            double ratio = (double)p.hitPoints / partyHp;
            int might = p.itemsMight.Max(v => (int)v);
            string[] ATTACK = { "", "steals equipment", "poisons", "destroys an item", "steals an item" };
            string[] DEFENSE = { "", "grabs your weapon", "breaks your weapon", "flees from magic", "healed by fire" };
            var traits = new List<string>();
            if (p.numDistAttacks > 0) traits.Add("ranged attacks");
            if (!string.IsNullOrEmpty(ATTACK.ElementAtOrDefault(p.attackSkillType))) traits.Add($"{ATTACK[p.attackSkillType]} ({p.attackSkillChance}%)");
            if (!string.IsNullOrEmpty(DEFENSE.ElementAtOrDefault(p.defenseSkillType))) traits.Add($"{DEFENSE[p.defenseSkillType]} ({p.defenseSkillChance}%)");
            // Damage classes (itemsMight/protectionAgainstItems index): weapons set 0-2 from their item
            // scripts, the spells use 3 Freeze, 4 Fireball, 5 Spark/Lightning, 6 Caustic fog, 7 Hand of
            // Fate/Mist of Doom. Protection 256 = normal damage.
            string[] DAMAGE = { "chopping (axes, halberds, sabres)", "blades (swords, daggers)", "blunt (maces, mauls)", "ice (Freeze)", "fire (Fireball)", "lightning (Spark, Lightning)", "acid (Caustic fog)", "magic (Hand of Fate, Mist of Doom)" };
            var resist = new List<string>(); var weak = new List<string>(); var heals = new List<string>();
            // weak/resistant relative to the creature's own typical value (many take 50% of everything)
            var vals = p.protectionAgainstItems.Select(v => (int)(short)v).ToList();
            var sorted = vals.Where(d => d >= 0).OrderBy(d => d).ToList(); int @base = sorted.Count > 0 ? sorted[sorted.Count >> 1] : 0; if (@base == 0) @base = 256;
            for (int i = 0; i < vals.Count; i += 1) { int d = vals[i]; int pct = Js.Round((d * 100.0) / @base); if (d < 0) heals.Add(DAMAGE[i]); else if (d < @base * 0.75) resist.Add($"{DAMAGE[i]} ({pct}% damage)"); else if (d > @base * 1.25) weak.Add($"{DAMAGE[i]} ({pct}% damage)"); }
            if (heals.Count > 0) traits.Add($"healed by {string.Join(", ", heals)}");
            return new MonsterInfo
            {
                name = monsterName(m), hp = m.hitPoints, hpMax = p.hitPoints, might = might, hitChance = Math.Min(100, Js.Round((p.fightingStats[0] * 100.0) / 256)), evade = p.fightingStats[3],
                protection = p.itemProtection, danger = ratio > 1.5 ? "high" : ratio > 0.6 ? "medium" : "low",
                ranged = p.numDistAttacks > 0, poison = p.attackSkillType == 2, steals = p.attackSkillType == 1 || p.attackSkillType == 4, traits = traits, weak = weak, resist = resist, heals = heals,
                damageTaken = vals.Select((d, i) => new DamageTakenEntry { kind = Regex.Replace(DAMAGE[i], @" \(.*", ""), hint = DAMAGE[i], pct = Js.Round((d * 100.0) / 256) }).ToList(), // per damage class, 100 = normal
            };
        }

        // Everything really lying on one block (the map tooltip and the bestiary's drop list read this).
        public List<LostLoot> uiItemsOnBlock(int block)
        {
            var @out = new List<LostLoot>();
            if (block < 0 || block >= levelBlockProperties.Length) return @out;
            var l = levelBlockProperties[block];
            int cur = l.assignedObjects;
            int guard = 0;
            while (cur != 0 && guard++ < 64)
            {
                var obj = findObject(cur);
                if ((cur & 0x8000) == 0 && HostUi_itemAt(cur) != null && itemsInPlay[cur].itemPropertyIndex != 0 && (itemsInPlay[cur].shpCurFrame_flg & 0x8000) == 0)
                {
                    @out.Add(new LostLoot { item = cur, name = itemName(cur) });
                }
                cur = obj.nextAssignedObject;
            }
            return @out;
        }

        // Items lying on the party block and on the block ahead (the two the original lets you pick from).
        public List<FloorItem> uiFloorItems()
        {
            var @out = new List<FloorItem>();
            int ahead = calcNewBlockPosition(currentBlock, currentDirection);
            var blocks = new List<(int block, bool isAhead)> { (currentBlock, false) };
            if (!testWallFlag(ahead, currentDirection, 1) && !testWallFlag(currentBlock, currentDirection, 1)) blocks.Add((ahead, true));
            foreach (var (block, isAhead) in blocks)
            {
                var l = levelBlockProperties[block];
                int cur = l.drawObjects != 0 ? l.drawObjects : l.assignedObjects; // draw order is rebuilt on the next frame
                int guard = 0;
                while (cur != 0 && guard++ < 64)
                {
                    if ((cur & 0x8000) == 0 && HostUi_itemAt(cur) != null && itemsInPlay[cur].itemPropertyIndex != 0) @out.Add(new FloorItem { item = cur, name = itemName(cur), block = block, ahead = isAhead });
                    var obj = findObject(cur);
                    cur = l.drawObjects != 0 ? obj.nextDrawObject : obj.nextAssignedObject;
                }
            }
            return @out;
        }

        // Take a floor item straight into the inventory (into the hand when the inventory is full).
        public async Task<int> uiTakeFloorItem(int item, int block)
        {
            onCall?.Invoke("uiTakeFloorItem", new object[] { item, block });   // the host's wrapper (main.mjs wrapCount)
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || weaponsDisabled || itemInHand != 0) return 0;
            if (!uiFloorItems().Any(f => f.item == item && f.block == block)) return 0;
            await removeLevelItem(item, block);
            await setHandItem(item); // runs the item pick-up script and prints "taken"
            if (itemInHand == item)
            {
                int slot = Array.IndexOf(inventory, (ushort)0);
                if (slot >= 0)
                {
                    inventory[slot] = (ushort)item;
                    await setHandItem(0);
                    gui_drawInventory();
                }
            }
            sceneUpdateRequired = true;
            return 1;
        }

        // Level name from the automap's string table ("Gladstone Keep", "Caves Level 2"...).
        public string levelName() => levelName(currentLevel);

        public string levelName(int level)
        {
            string name = level >= 0 && level < @static.MapStringId.Length ? getLangString(@static.MapStringId[level]) : null;
            return !string.IsNullOrEmpty(name) ? name : $"Level {level}";
        }

        // Attack cooldown of a monster: 0..1 (1 = about to strike), or null when it is not fighting.
        public double? monsterThreat(Monster m)
        {
            if (m == null || m.properties == null || m.hitPoints <= 0) return null;
            if (m.mode == 5 && m.attackWait > 0) return 1 - Math.Max(0, Math.Min(1, (double)m.fightCurTick / m.attackWait));
            if (m.mode == 7 || m.mode == 8 || m.mode == 9) return 1;
            return null;
        }

        // The creature the party is facing: on the block ahead, or on their own if it is on top of them.
        public Monster uiMonsterAhead()
        {
            if (currentBlock == 0) return null;
            int[] blocks = { calcNewBlockPosition(currentBlock, currentDirection), currentBlock };
            foreach (int b in blocks)
            {
                int o = levelBlockProperties[b].assignedObjects;
                int guard = 0;
                while (o != 0 && guard++ < 32)
                {
                    if ((o & 0x8000) != 0)
                    {
                        var m = monsters.ElementAtOrDefault(o & 0x7fff);
                        if (m != null && m.properties != null && m.hitPoints > 0 && m.mode < 13) return m;
                    }
                    o = findObject(o).nextAssignedObject;
                }
            }
            return null;
        }

        // How far through their attack cooldown a hero is: 0 just swung, 1 ready again, null when they
        // are not on cooldown at all. Event type 1 is the weapon cooldown the attack button sets.
        public double? uiAttackCooldown(int c)
        {
            var ch = characters.ElementAtOrDefault(c);
            if (ch == null || (ch.flags & 1) == 0) return null;
            for (int i = 0; i < 5; i += 1)
            {
                if (ch.characterUpdateEvents[i] != 1) continue;
                int left = ch.characterUpdateDelay[i];
                if (left <= 0) return null;
                int total = Math.Max(left, ch.attackCooldownTotal != 0 ? ch.attackCooldownTotal : left);
                return Math.Max(0, Math.Min(1, 1 - (double)left / total));
            }
            return null;
        }

        // A live monster within reach of the party: the host blocks fast travel (and anything else that
        // would walk out of a fight) while this is true.
        public bool uiInCombat()
        {
            if (currentBlock == 0) return false;
            return monsters.Any(m => m.properties != null && m.hitPoints > 0 && m.mode < 13 && m.block != 0 && getBlockDistance(currentBlock, m.block) <= 1);
        }

        // Which hero a monster on `block` would hit (the nearest to it), and the party's line-up left to right.
        public List<ThreatTarget> uiThreatTargets()
        {
            var @out = new List<ThreatTarget>();
            foreach (var m in monsters)
            {
                if (m.properties == null || m.hitPoints <= 0 || m.mode >= 13 || m.block == 0) continue;
                if (getBlockDistance(currentBlock, m.block) > 1 || monsterThreat(m) == null) continue; // only monsters in a fight
                int c = getNearestPartyMemberFromPos(m.x, m.y);
                if (c != 0xffff) @out.Add(new ThreatTarget { monster = m.id, target = c });
            }
            return @out;
        }

        // Swap two party slots (the line-up decides who stands nearest to a monster).
        public bool uiSwapParty(int a, int b)
        {
            if (a == b || (characters[a].flags & 1) == 0 || (characters[b].flags & 1) == 0) return false;
            if ((updateFlags & 3) != 0 || weaponsDisabled || needSceneRestore != 0 || sysTimerPaused) return false;
            (characters[a], characters[b]) = (characters[b], characters[a]);
            (characterFaceShapes[a], characterFaceShapes[b]) = (characterFaceShapes[b], characterFaceShapes[a]);
            if (selectedCharacter == a) selectedCharacter = b; else if (selectedCharacter == b) selectedCharacter = a;
            calcCharPortraitXpos();
            gui_drawAllCharPortraitsWithStats();
            return true;
        }

        // What an item is for, from its name (the scripts do not say; these are the game's conventions).
        public string itemUse(string name, bool usable, bool wearable)
        {
            string n = name.ToLowerInvariant();
            bool t(string re) => Regex.IsMatch(n, re);
            if (t("bezel cup")) return "Drink from it: heals a hero fully (a few sips)";
            if (t("crucible of faith")) return "The Elixir's vessel: place it on the Altar de Blanca in the White Tower, then add the ingredients";
            if (t("bloodstone|swamp vial|honey|earth vial")) return "Elixir ingredient: use it on the Altar de Blanca in the White Tower (with the Crucible in place)";
            if (t("empty flask|vial")) return "Holds one of the Elixir's ingredients";
            if (t("ruby of truth|shard")) return "Needed against the Nether Mask";
            if (t("writ")) return "Shows you are on the King's business";
            if (t("atlas|magic map")) return "Draws the map as you explore";
            if (t(@"key\b")) return "Opens a lock: try locked doors and chests on this level";
            if (t("lockpick")) return "Use on a locked door or chest (rogue skill)";
            if (t("oil flask")) return "Refills the lantern";
            if (t(@"lantern|lamp\b")) return "Lights dark places (Compass & lantern panel)";
            if (t("compass")) return "Shows the facing direction";
            if (t("scroll")) return "Read it: use on a hero to learn or cast";
            if (t("salve|aloe|ginseng|potion|herb|elixir|antidote|bread|food")) return "Use on a hero (heals or cures)";
            if (t("rock|dart|star|shuriken|quarrel|arrow")) return "Throw it: click in the scene with it in hand";
            if (t("figurine|cube|orb|mask|crystal|emblem|medallion|gem|idol|skull|bone|amulet|talisman")) return "Quest item: keep it, use it where the story asks" + (wearable ? " (can also be held as a weapon)" : "");
            if (usable && wearable) return "Use it in the scene or on a hero; can also be equipped";
            if (usable) return "Use on a hero";
            if (wearable) return "Equip on a hero";
            return "";
        }

        // Puzzle hint (opt-in): the nearest closed door in the explored area and the nearest switch-like
        // symbol (button, lever, plate, secret wall, teleporter) to try for it. Heuristic, not script-based.
        public PuzzleHint uiPuzzleHint()
        {
            var symbols = new List<(int block, int type, int dist)>();
            int door = -1; int doorDist = 99;
            int dist(int b) => getBlockDistance(currentBlock, b);
            for (int b = 0; b < 1024; b += 1)
            {
                if ((levelBlockProperties[b].flags & 7) != 7) continue;
                for (int side = 0; side < 4; side += 1)
                {
                    int nb = calcNewBlockPosition(b, side);
                    int wall = levelBlockProperties[nb].walls[side ^ 2];
                    if (wall == 0) continue;
                    int type = wllAutomapData[wall] & 0x1f;
                    if (type == 13 && (wllWallFlags[wall] & 1) != 0 && dist(b) < doorDist) { door = b; doorDist = dist(b); }
                    if (new[] { 10, 15, 22, 17 }.Contains(type)) symbols.Add((b, type, dist(b)));
                }
            }
            if (symbols.Count == 0) return null;
            var names = new Dictionary<int, string> { { 10, "Plate" }, { 15, "Button/lever" }, { 22, "Secret wall" }, { 17, "Teleporter" } };
            symbols = symbols.OrderBy(s => s.dist).ToList(); // stable, like Array.prototype.sort
            var s0 = symbols[0];
            return new PuzzleHint { block = s0.block, dist = s0.dist, name = names.TryGetValue(s0.type, out var nm) ? nm : "switch", door = door };
        }

        // The Green Skull is a thrown poison bomb, so the host offers it as a spell. These two helpers
        // are what that row needs: where the skull is, and throwing one straight ahead.
        public int uiInventorySlotOfProperty(int prop)
        {
            for (int i = 0; i < inventory.Length; i += 1)
            {
                int item = inventory[i];
                if (item != 0 && HostUi_itemAt(item) != null && itemsInPlay[item].itemPropertyIndex == prop) return i;
            }
            return -1;
        }

        public int uiCountOfProperty(int prop)
        {
            int n = 0;
            foreach (int item in inventory) if (item != 0 && HostUi_itemAt(item) != null && itemsInPlay[item].itemPropertyIndex == prop) n += 1;
            return n;
        }

        // Repair for a save made while the port was wrongly destroying the skull on every cast. It hands
        // one back only when the party has none anywhere, so it cannot be used to stack them up.
        public int uiRestoreGreenSkull()
        {
            const int GREEN_SKULL = 266;
            bool has(int item) => item != 0 && HostUi_itemAt(item) != null && itemsInPlay[item].itemPropertyIndex == GREEN_SKULL;
            if (inventory.Any(i => has(i)) || has(itemInHand)) return 0;
            if (characters.Any(c => (c.flags & 1) != 0 && c.items.Any(i => has(i)))) return 0;
            int slot = Array.IndexOf(inventory, (ushort)0);
            if (slot < 0) { uiEmit("message", "Your inventory is full.", "system"); return 0; }
            int item = makeItem(GREEN_SKULL, 0, 0);
            if (item == -1) return 0;
            inventory[slot] = (ushort)item;
            gui_drawInventory();
            uiEmit("message", "The Green Skull is back in your pack.", "system");
            return 1;
        }

        // The Green Skull does not get thrown: its item script casts the spell its `might` names
        // (ITEM.INF function 29 - getItemPara 11, then checkMagic and castSpell). The hero casts it and
        // pays for it, exactly as the script does; the skull itself is not used up. The only change is
        // the power, fixed at maximum instead of the script's lowest - so it costs the level-4 mana.
        public int uiCastFromItem(int slot, int c)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || weaponsDisabled) return 0;
            int item = inventory[slot];
            if (item == 0 || itemInHand != 0 || itemCastPending) return 0;
            int spell = itemProperties[itemsInPlay[item].itemPropertyIndex].might;
            var sp = @static.SpellProperties.ElementAtOrDefault(spell);
            if (sp == null) return 0;
            var ch = characters.ElementAtOrDefault(c);
            if (ch == null || (ch.flags & 1) == 0) return 0;
            if (sp.mpRequired[3] > ch.magicPointsCur || sp.hpRequired[3] >= ch.hitPointsCur)
            {
                uiEmit("message", $"{ch.name} has not the magic to break the skull.", "system");
                return 0;
            }
            itemCastPending = true; // one cast at a time: clicking fast used to queue up free ones
            queueAsync(async () =>
            {
                try { await castSpell(c, spell, 3); } finally { itemCastPending = false; }
            });
            return 1;
        }

        // Items the port adds on top of the game's own (the crafted potions). itemProperties is a plain
        // array built at start-up, so extra entries can be appended after it; they carry their own name
        // and effect because no ITEM.INF script knows about them. Registration is deterministic, so a
        // saved potion still finds its property when the game is loaded again.
        public Dictionary<string, int> uiRegisterExtraItems(IList<ExtraItemDef> defs)
        {
            if (itemProperties.Count == 0) return null;
            if (extraItems != null && extraBase == itemProperties.Count - defs.Count)
            {
                return defs.Select((d, i) => (d.id, i)).ToDictionary(x => x.id, x => extraBase.Value + x.i);
            }
            extraBase = itemProperties.Count;
            extraItems = new Dictionary<int, ExtraItemDef>();
            var @out = new Dictionary<string, int>();
            for (int i = 0; i < defs.Count; i += 1)
            {
                var def = defs[i];
                var icon = HostUi_propAt(def.icon);
                int prop = extraBase.Value + i;
                var entry = new ItemProperty
                {
                    nameStringId = 0, shpIndex = icon != null ? icon.shpIndex : 0, flags = def.kind == "errand" ? 0 : 1, type = 0,
                    itemScriptFunc = 0xff, might = 0, skill = 0, protection = 0, unkB = def.price,
                };
                // JS: itemProperties[prop] = ... (prop is always the next index)
                if (prop < itemProperties.Count) itemProperties[prop] = entry; else itemProperties.Add(entry);
                extraItems[prop] = def;
                @out[def.id] = prop;
            }
            return @out;
        }

        public ExtraItemDef uiExtraItem(int item)
        {
            var it = item != 0 ? HostUi_itemAt(item) : null;
            return it != null && extraItems != null && extraItems.TryGetValue(it.itemPropertyIndex, out var def) ? def : null;
        }

        // Drinking one of the port's own potions. Returns the message, or "" when it would do nothing.
        public string uiUseExtraItem(ExtraItemDef def, int c)
        {
            var ch = characters.ElementAtOrDefault(c);
            if (ch == null || (ch.flags & 1) == 0) return "";
            if (def.effect == "heal")
            {
                if (ch.hitPointsCur >= ch.hitPointsMax) return "";
                increaseCharacterHitpoints(c, def.amount, false);
                gui_drawCharPortraitWithStats(c);
                return $"{ch.name} drinks the {def.name.ToLowerInvariant()}.";
            }
            if (def.effect == "mana")
            {
                if (ch.magicPointsCur >= ch.magicPointsMax) return "";
                ch.magicPointsCur = Math.Min(ch.magicPointsMax, ch.magicPointsCur + def.amount);
                gui_drawCharPortraitWithStats(c);
                return $"{ch.name} drinks the {def.name.ToLowerInvariant()}.";
            }
            if (def.effect == "cure")
            {
                if ((ch.flags & 0x80) == 0) return "";
                removeCharacterEffects(ch, 4, 4); // clears the poisoned flag, as the Salve script does
                gui_drawCharPortraitWithStats(c);
                return $"{ch.name} is cured of the poison.";
            }
            if (def.effect == "skill")
            { // doubles one skill for a while, through the engine's own modifier
                int skill = def.skill;
                if (ch.potionSkillBonus[skill] != 0) return "";
                int bonus = Math.Min(255, (int)ch.skillLevels[skill]);
                if (bonus == 0) return "";
                ch.potionSkillBonus[skill] = (byte)bonus;
                ch.skillModifiers[skill] = unchecked((sbyte)(ch.skillModifiers[skill] + bonus));
                setCharacterUpdateEvent(c, 9 + skill, Js.Round((def.seconds * 1000) / (15 * tickLength)), 1);
                gui_drawCharPortraitWithStats(c);
                return $"{ch.name} burns with {def.name.ToLowerInvariant()}: {new[] { "fighter", "rogue", "mage" }[skill]} skill doubled for {def.seconds} seconds.";
            }
            return "";
        }

        // What kind of thing an item is, for sorting and filtering the bag. Derived from what the item
        // does rather than from slot bits, so it needs no table of what each equipment slot means.
        public string uiItemKind(int item)
        {
            var extra = uiExtraItem(item);
            if (extra != null && !string.IsNullOrEmpty(extra.kind)) return extra.kind; // the port's own trophies keep their own filter
            var info = itemInfo(item);
            if (info == null) return "other";
            if (info.usable) return "usable";
            if (info.slots.Count == 0) return "other";
            return info.might >= info.protection ? "weapon" : "armour";
        }

        // Reorders the bag. `mode`: "type" (kind, then name), "name", "value" or "might"; empty slots end
        // up last either way. Returns newSlotOf[oldSlot] so the host can follow its hotbar pins.
        public int[] uiSortInventory(string mode = "type")
        {
            var inv = inventory;
            var KIND = new Dictionary<string, int> { { "weapon", 0 }, { "armour", 1 }, { "usable", 2 }, { "errand", 3 }, { "other", 4 } };
            var filled = new List<int>();
            for (int i = 0; i < inv.Length; i += 1) if (inv[i] != 0) filled.Add(i);
            string name(int i) => (itemName(inv[i]) ?? "").ToLowerInvariant();
            int infoMight(int i) => itemInfo(inv[i])?.might ?? 0;
            int cmpName(int a, int b) => string.Compare(name(a), name(b), CultureInfo.InvariantCulture, CompareOptions.None); // localeCompare (browser default locale ~ invariant)
            int byType(int a, int b)
            {
                // KIND[x] - KIND[y] is NaN (falsy) for a kind outside the table: then the name decides
                if (KIND.TryGetValue(uiItemKind(inv[a]), out int ka) && KIND.TryGetValue(uiItemKind(inv[b]), out int kb) && ka - kb != 0) return ka - kb;
                return cmpName(a, b);
            }
            var by = new Dictionary<string, Comparison<int>>
            {
                { "type", byType },
                { "name", cmpName },
                { "value", (a, b) => { int d = uiPriceOf(inv[b]) - uiPriceOf(inv[a]); return d != 0 ? d : cmpName(a, b); } },
                { "might", (a, b) => { int d = infoMight(b) - infoMight(a); return d != 0 ? d : cmpName(a, b); } },
            };
            var cmp = by.TryGetValue(mode ?? "", out var f) ? f : by["type"];
            var order = filled.OrderBy(x => x, Comparer<int>.Create(cmp)).ToList(); // stable, like Array.prototype.sort
            var newSlotOf = Enumerable.Repeat(-1, inv.Length).ToArray();
            var sorted = new int[inv.Length];
            for (int index = 0; index < order.Count; index += 1) { int old = order[index]; sorted[index] = inv[old]; newSlotOf[old] = index; }
            for (int i = 0; i < inv.Length; i += 1) inv[i] = (ushort)sorted[i];
            gui_drawInventory();
            return newSlotOf;
        }

        // Item stats for tooltips: name, might/protection bonus, the equipment slots it fits, usability.
        public ItemInfo itemInfo(int item)
        {
            if (item == 0 || HostUi_itemAt(item) == null) return null;
            var prop = HostUi_propAt(itemsInPlay[item].itemPropertyIndex);
            if (prop == null) return null; // an item whose property is not in the table (a save from another build)
            var slots = new List<string>();
            for (int i = 0; i < 11; i += 1)
            {
                if ((prop.type & (1 << i)) == 0) continue;
                string ls = getLangString(@static.InventoryDesc[i]);
                string label = Regex.Replace(!string.IsNullOrEmpty(ls) ? ls : $"slot {i}", @"\.$", "");
                if (!slots.Contains(label)) slots.Add(label);
            }
            return new ItemInfo { name = itemName(item), might = prop.might, protection = prop.protection, slots = slots, usable = (prop.flags & 1) != 0, skill = new[] { "fighter", "rogue", "mage" }.ElementAtOrDefault(prop.skill), prop = itemsInPlay[item].itemPropertyIndex, condition = weaponWear ? uiConditionName(item) : "" };
        }

        // ---- weapon wear (Settings -> weapon wear) ----
        // A weapon collects a point of wear per swing and loses up to half its damage on the way. The
        // count lives on the item, so it travels with the save and disappears with the item.
        public int uiWearWeapon(int c, int damage)
        {
            var ch = characters.ElementAtOrDefault(c);
            int item = ch != null ? ch.items[0] : 0;
            var it = item != 0 ? HostUi_itemAt(item) : null;
            if (it == null) return damage;
            var prop = HostUi_propAt(it.itemPropertyIndex);
            if (prop == null || prop.might == 0 || (prop.flags & 1) != 0) return damage; // only real weapons wear
            it.wear = it.wear + 1;
            return Math.Max(1, Js.Round(damage * (0.5 + 0.5 * uiCondition(item))));
        }

        // 1 = as new, 0 = as blunt as it gets.
        public double uiCondition(int item)
        {
            var it = HostUi_itemAt(item);
            if (it == null) return 1;
            var prop = HostUi_propAt(it.itemPropertyIndex);
            if (prop == null || prop.might == 0 || (prop.flags & 1) != 0) return 1;
            return Math.Max(0, 1 - it.wear / 300.0);
        }

        public string uiConditionName(int item)
        {
            double c = uiCondition(item);
            if (c > 0.85) return "";
            return c > 0.6 ? "worn" : c > 0.3 ? "chipped" : "battered";
        }

        // What the imp charges to grind every carried and worn weapon back to new, and the repair itself.
        public RepairCost uiRepairCost()
        {
            int wear = 0;
            void add(int item) { var it = item != 0 ? HostUi_itemAt(item) : null; if (it != null && it.wear != 0) wear += Math.Min(300, it.wear); }
            foreach (int item in inventory) add(item);
            foreach (var ch in characters) if ((ch.flags & 1) != 0) foreach (int item in ch.items) add(item);
            return new RepairCost { wear = wear, price = (int)Math.Ceiling(wear / 4.0) };
        }

        public RepairResult uiRepairAll()
        {
            var cost = uiRepairCost(); int wear = cost.wear, price = cost.price;
            if (wear == 0) return new RepairResult { error = "Nothing needs grinding." };
            if (credits < price) return new RepairResult { error = $"The imp wants {price} crowns." };
            void clear(int item) { var it = item != 0 ? HostUi_itemAt(item) : null; if (it != null) it.wear = 0; }
            foreach (int item in inventory) clear(item);
            foreach (var ch in characters) if ((ch.flags & 1) != 0) foreach (int item in ch.items) clear(item);
            queueAsync(() => takeCredits(price, 1));
            return new RepairResult { price = price };
        }

        public string itemTooltip(int item) => itemTooltip(item, selectedCharacter);

        public string itemTooltip(int item, int c)
        {
            var info = itemInfo(item);
            if (info == null) return "";
            var parts = new List<string>();
            if (info.might != 0 && !info.usable) parts.Add($"might {(info.might > 0 ? "+" : "")}{info.might}");
            if (info.protection != 0 && !info.usable) parts.Add($"protection +{info.protection}");
            if (info.slots.Count > 0) parts.Add($"fits: {string.Join(", ", info.slots)}");
            if (info.usable) parts.Add("usable");
            if (!string.IsNullOrEmpty(info.condition)) parts.Add(info.condition);
            string compare = "";
            if (!info.usable && info.slots.Count > 0 && c >= 0 && characters.ElementAtOrDefault(c) != null && (characters[c].flags & 1) != 0)
            {
                var fits = uiSlotsFor(item, c);
                int? slot = fits.Count > 0 ? fits[0] : (int?)null;
                int worn = slot != null ? characters[c].items[slot.Value] : 0;
                if (worn != 0 && worn != item)
                {
                    var w = itemInfo(worn);
                    var diff = new List<string>();
                    if (info.might != w.might) diff.Add($"{(info.might - w.might > 0 ? "+" : "")}{info.might - w.might} might");
                    if (info.protection != w.protection) diff.Add($"{(info.protection - w.protection > 0 ? "+" : "")}{info.protection - w.protection} protection");
                    compare = $"\n{(diff.Count > 0 ? string.Join(", ", diff) : "same")} vs. {w.name} ({characters[c].name})";
                }
                else if (slot != null && worn == 0) compare = $"\n{characters[c].name}: slot empty";
            }
            // what it is for (the Items tab's "Use" line)
            string use = itemUse(info.name, info.usable, info.slots.Count > 0);
            return (parts.Count > 0 ? $"{info.name}\n{string.Join(" · ", parts)}" : info.name) + (string.IsNullOrEmpty(use) ? "" : $"\n{use}") + compare;
        }

        // Equipment slots of character `c` that `item` fits.
        public List<int> uiSlotsForProperty(int propIndex, int c)
        {
            var prop = HostUi_propAt(propIndex);
            if (prop == null || (characters[c].flags & 1) == 0) return new List<int>();
            int @base = @static.CharInvIndex[characters[c].raceClassSex] * 22;
            var slots = new List<int>();
            for (int i = 0; i < 11; i += 1) if ((prop.type & (1 << i)) != 0 && @static.CharInvDefs[@base + i * 2] != 0xff) slots.Add(i);
            return slots;
        }

        public ItemInfo itemInfoForProperty(int propIndex)
        {
            var p = HostUi_propAt(propIndex);
            if (p == null) return null;
            var slots = new List<string>(); for (int i = 0; i < 11; i += 1) if ((p.type & (1 << i)) != 0) { string ls = getLangString(@static.InventoryDesc[i]); string l = Regex.Replace(!string.IsNullOrEmpty(ls) ? ls : $"slot {i}", @"\.$", ""); if (!slots.Contains(l)) slots.Add(l); }
            var extra = extraItems != null && extraItems.TryGetValue(propIndex, out var e) ? e : null;
            string name = extra != null && !string.IsNullOrEmpty(extra.name) ? extra.name : getLangString(p.nameStringId);
            return new ItemInfo { name = !string.IsNullOrEmpty(name) ? name : "", might = p.might, protection = p.protection, slots = slots, usable = (p.flags & 1) != 0, prop = propIndex };
        }

        public List<int> uiSlotsFor(int item, int c)
        {
            var prop = itemProperties[itemsInPlay[item].itemPropertyIndex];
            int @base = @static.CharInvIndex[characters[c].raceClassSex] * 22;
            var slots = new List<int>();
            for (int i = 0; i < 11; i += 1) if ((prop.type & (1 << i)) != 0 && @static.CharInvDefs[@base + i * 2] != 0xff) slots.Add(i);
            return slots;
        }

        // Item records the party holds must have one owner. The respawn bug above left some with several: a
        // monster's first-visit loot list still naming a record the party picked up, and when that monster fell
        // the record moved to a new owner while the old one still named it (Ak'shel "held" a helm in his weapon
        // hand and could not attack). On load: a hero's slot keeps a record only if the record is alive, is in
        // no other place the party keeps things, and - where several slots name it - fits this one; monsters
        // anywhere stop naming records the party holds.
        public bool repairOnLoad;

        public int uiRepairEquipment()
        {
            var elsewhere = new HashSet<int>(inventory.Where(i => i != 0).Select(i => (int)i));
            if (itemInHand != 0) elsewhere.Add(itemInHand);
            var refs = new Dictionary<int, List<(int c, int slot)>>();
            for (int c = 0; c < characters.Length; c += 1)
            {
                if ((characters[c].flags & 1) == 0) continue;
                for (int slot = 0; slot < characters[c].items.Length; slot += 1)
                {
                    int it = characters[c].items[slot];
                    if (it == 0) continue;
                    if (!refs.TryGetValue(it, out var l)) refs[it] = l = new List<(int, int)>();
                    l.Add((c, slot));
                }
            }
            int fixedCount = 0;
            foreach (var kv in refs)
            {
                int it = kv.Key;
                var rec = it > 0 && it < itemsInPlay.Length ? itemsInPlay[it] : null;
                bool dead = rec == null || rec.itemPropertyIndex == 0 || (rec.shpCurFrame_flg & 0x8000) != 0 || rec.block != 0 || elsewhere.Contains(it);
                var keep = dead ? (-1, -1) : kv.Value.Count == 1 ? kv.Value[0] : kv.Value.FirstOrDefault(r => uiSlotsFor(it, r.c).Contains(r.slot));
                if (!dead && kv.Value.Count > 1 && keep == default && !uiSlotsFor(it, kv.Value[0].c).Contains(kv.Value[0].slot)) keep = kv.Value[0];
                foreach (var r in kv.Value)
                {
                    if (r == keep) continue;
                    characters[r.c].items[r.slot] = 0;
                    fixedCount += 1;
                    log?.Invoke($"repaired: {characters[r.c].name} slot {r.slot} named item record {it}, owned elsewhere");
                }
            }
            // monsters (this level and the ones kept from other visits) giving up records the party holds
            var held = new HashSet<int>(refs.Keys.Concat(elsewhere));
            void unclaim(Monster m)
            {
                if (m == null || m.assignedItems == 0) return;
                int guard = 0;
                while (m.assignedItems != 0 && held.Contains(m.assignedItems) && guard++ < 64) m.assignedItems = itemsInPlay[m.assignedItems]?.nextAssignedObject ?? 0;
                for (int cur = m.assignedItems; cur != 0 && guard++ < 64; )
                {
                    int next = itemsInPlay[cur]?.nextAssignedObject ?? 0;
                    if (next != 0 && held.Contains(next)) { itemsInPlay[cur].nextAssignedObject = itemsInPlay[next]?.nextAssignedObject ?? 0; continue; }
                    cur = next;
                }
            }
            foreach (var m in monsters) unclaim(m);
            if (lvlTempData != null) foreach (var t in lvlTempData) if (t?.monsters != null) foreach (var m in t.monsters) unclaim(m);
            if (fixedCount > 0) { for (int c = 0; c < characters.Length; c += 1) if ((characters[c].flags & 1) != 0) recalcCharacterStats(c); uiEmit("message", "An equipment slot that named an item owned elsewhere was cleared (an old respawn bug).", "system"); }
            return fixedCount;
        }

        // Put the best inventory item (might + protection) into every slot where it beats what is worn.
        public async Task<int> uiEquipBest(int c)
        {
            if ((updateFlags & 3) != 0 || needSceneRestore != 0 || sysTimerPaused || weaponsDisabled || itemInHand != 0) return 0;
            var ch = characters[c];
            if ((ch.flags & 1) == 0) return 0;
            int score(int item) { var i = itemInfo(item); return i != null && !i.usable ? i.might + i.protection : -1; }
            int changed = 0;
            var used = new HashSet<int>();
            for (int slot = 0; slot < 11; slot += 1)
            {
                int best = -1; int bestScore = score(ch.items[slot]);
                for (int inv = 0; inv < inventory.Length; inv += 1)
                {
                    int item = inventory[inv];
                    if (item == 0 || used.Contains(inv) || !uiSlotsFor(item, c).Contains(slot)) continue;
                    int s = score(item);
                    if (s > bestScore) { bestScore = s; best = inv; }
                }
                if (best < 0) continue;
                used.Add(best);
                if (await uiDropInventoryOn(best, c, slot) != 0) changed += 1;
            }
            if (changed == 0) txt.printMessage(0, $"{ch.name} already wears the best gear.");
            return changed;
        }

        // ---- click-to-move ----
        // Breadth-first path over explored blocks (automap flags 7) using the engine passability test,
        // so closed doors, walls and monsters block the way exactly like a manual walk would.
        public List<int> findPath(int from, int to)
        {
            if (from == to) return new List<int>();
            var prev = new short[1024]; Js.Fill(prev, (short)-1);
            var via = new byte[1024];
            var queue = new Queue<int>(); queue.Enqueue(from);
            prev[from] = (short)from;
            while (queue.Count > 0)
            {
                int block = queue.Dequeue();
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int next = calcNewBlockPosition(block, dir);
                    if (prev[next] != -1 || ((levelBlockProperties[next].flags & 7) != 7 && next != to) || !checkBlockPassability(next, dir)) continue;
                    prev[next] = (short)block;
                    via[next] = (byte)dir;
                    if (next == to)
                    {
                        var path = new List<int>();
                        for (int b = to; b != from; b = prev[b]) path.Insert(0, via[b]);
                        return path;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        // Explored blocks with a walkable, still unexplored neighbour (the "frontier" the map shows).
        public List<int> uiFrontier()
        {
            var @out = new List<int>();
            for (int block = 0; block < 1024; block += 1)
            {
                if ((levelBlockProperties[block].flags & 7) != 7) continue;
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int next = calcNewBlockPosition(block, dir);
                    if ((levelBlockProperties[next].flags & 7) != 7 && checkBlockPassability(next, dir)) { @out.Add(block); break; }
                }
            }
            return @out;
        }

        // Nearest unexplored block reachable through explored ones (BFS by walking distance), or -1.
        public int nearestUnexplored()
        {
            var seen = new byte[1024];
            var queue = new Queue<int>(); queue.Enqueue(currentBlock);
            seen[currentBlock] = 1;
            while (queue.Count > 0)
            {
                int block = queue.Dequeue();
                for (int dir = 0; dir < 4; dir += 1)
                {
                    int next = calcNewBlockPosition(block, dir);
                    if (seen[next] != 0 || !checkBlockPassability(next, dir)) continue;
                    if ((levelBlockProperties[next].flags & 7) != 7) return next;
                    seen[next] = 1;
                    queue.Enqueue(next);
                }
            }
            return -1;
        }

        public bool autoWalkTo(int target)
        {
            var path = findPath(currentBlock, target);
            if (path == null || path.Count == 0) { autoWalk = null; return false; }
            autoWalk = new AutoWalk { steps = path, hp = characters.Sum(c => (c.flags & 1) != 0 ? c.hitPointsCur : 0) };
            return true;
        }

        // One turn or one step per main-loop iteration; anything unexpected (input, damage, a dialogue,
        // a blocked step) hands control back to the player.
        public async Task autoWalkStep()
        {
            var walk = autoWalk;
            if (walk == null) return;
            int hp = characters.Sum(c => (c.flags & 1) != 0 ? c.hitPointsCur : 0);
            if (events.Count > 0 || updateFlags != 0 || weaponsDisabled || needSceneRestore != 0 || sysTimerPaused || !partyAwake || hp < walk.hp)
            {
                autoWalk = null;
                return;
            }
            walk.hp = hp;
            int dir = walk.steps[0];
            var button = new Button { arg = 0, flags2 = 0x80 };
            if (dir != currentDirection)
            {
                if (((currentDirection - dir) & 3) == 1) await clickedTurnLeftArrow(button);
                else await clickedTurnRightArrow(button);
                return;
            }
            int before = currentBlock;
            int dialogues = uiDialogueCount;
            await clickedUpArrow(button);
            // Blocked, or a script talked to us on the way: stop here.
            if (currentBlock == before || uiDialogueCount != dialogues) { autoWalk = null; return; }
            walk.steps.RemoveAt(0);
            if (walk.steps.Count == 0) autoWalk = null;
        }

        // Everything the host needs to draw a character card.
        public CharacterInfo uiCharacterInfo(int c)
        {
            var ch = characters[c];
            if ((ch.flags & 1) == 0) return null;
            int busy = ch.flags & 0x314c;
            var equipment = new List<EquipmentSlot>();
            int @base = @static.CharInvIndex[ch.raceClassSex] * 22;
            for (int i = 0; i < 11; i += 1)
            {
                if (@static.CharInvDefs[@base + i * 2] == 0xff) continue; // slot not available for this race
                string label = getLangString(@static.InventoryDesc[i]);
                equipment.Add(new EquipmentSlot { slot = i, item = ch.items[i], name = itemName(ch.items[i]), label = !string.IsNullOrEmpty(label) ? label : $"Slot {i}" });
            }
            var exp = new[] { 0, 1, 2 }.Select(s =>
            {
                int level = ch.skillLevels[s] + ch.skillModifiers[s];
                int need = @static.ExpRequirements.ElementAtOrDefault(Math.Min(level, @static.ExpRequirements.Length - 1));
                return new SkillExp { level = level, points = ch.experiencePts[s], next = need };
            }).ToArray();
            return new CharacterInfo
            {
                name = ch.name, id = ch.id, selected = c == selectedCharacter,
                hp = ch.hitPointsCur, hpMax = ch.hitPointsMax, mp = ch.magicPointsCur, mpMax = ch.magicPointsMax,
                might = calculateCharacterStats(c, 0), protection = calculateCharacterStats(c, 1), damageSuffered = ch.damageSuffered, weaponHit = ch.weaponHit,
                skills = new CharacterSkills { fighter = exp[0], rogue = exp[1], mage = exp[2] },
                status = new CharacterStatus { poisoned = (ch.flags & 0x80) != 0, paralyzed = (ch.flags & 0x40) != 0, attacking = (ch.flags & 4) != 0, asleep = !partyAwake, busy = busy != 0 },
                cooldown = uiAttackCooldown(c),
                // the weapon's state, for the card badge (empty while weapon wear is off)
                weapon = weaponWear && ch.items[0] != 0 ? new WeaponState { name = itemName(ch.items[0]), condition = uiConditionName(ch.items[0]), left = uiCondition(ch.items[0]) } : null,
                equipment = equipment,
            };
        }
    }
}
