// Loading a saved game: the snapshot the JavaScript build writes, read back into the port.
//
// Transliterated from src/game/savegame.mjs loadState. The save is plain JSON with an explicit
// field list, which is the one piece of luck in this port - the shape is stable, and a field the
// port does not model yet is simply skipped rather than corrupting what it does.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LolCore;

public static class SaveGame
{
    /// <summary>
    /// Restores everything the port models: the party, the items, the flags, the state of every
    /// level visited, and finally the level the party was standing on.
    /// </summary>
    /// <summary>
    /// A number out of a save, whatever shape it arrives in. JavaScript has a single number type, so
    /// a saved game quite legitimately holds 87.5 where the port keeps a whole number, and asking for
    /// an Int32 throws rather than rounding - which stopped real saves from loading at all. Rounded
    /// the way the engine that wrote it rounds.
    /// </summary>
    private static int Num(JsonElement v, int fallback = 0)
    {
        if (v.ValueKind != JsonValueKind.Number) return fallback;
        if (v.TryGetInt32(out int whole)) return whole;
        if (!v.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d)) return fallback;
        d = JsMath.Round(d);
        if (d >= int.MaxValue) return int.MaxValue;
        if (d <= int.MinValue) return int.MinValue;
        return (int)d;
    }

    /// <summary>The same, for a named field of an object.</summary>
    private static int Num(JsonElement parent, string name, int fallback = 0) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) ? Num(v, fallback) : fallback;

    /// <summary>One monster as the browser build writes it.</summary>
    private static Monster ReadMonster(JsonElement m)
    {
        var one = new Monster
        {
                Id = Num(m, "id"),
                Block = Num(m, "block"),
                X = Num(m, "x"),
                Y = Num(m, "y"),
                Facing = Num(m, "facing"),
                Direction = Num(m, "direction"),
                Type = Num(m, "type"),
                Mode = Num(m, "mode"),
                Flags = Num(m, "flags"),
                HitPoints = Num(m, "hitPoints"),
                NumDistAttacks = Num(m, "numDistAttacks"),
                DistAttackTick = Num(m, "distAttackTick"),
                FlyingHeight = Num(m, "flyingHeight"),
                CurrentSubFrame = Num(m, "currentSubFrame"),
                ShiftStep = Num(m, "shiftStep"),
                FightCurTick = Num(m, "fightCurTick"),
                DamageReceived = Num(m, "damageReceived"),
                SpeedTick = Num(m, "speedTick"),
                DestX = Num(m, "destX"),
                DestY = Num(m, "destY"),
                DestDirection = Num(m, "destDirection"),
                AssignedItems = Num(m, "assignedItems"),
                CurDistWeapon = Num(m, "curDistWeapon"),
        };
        if (m.TryGetProperty("equipmentShapes", out var eq))
        {
            int at = 0;
            foreach (var v in eq.EnumerateArray()) { if (at >= one.EquipmentShapes.Length) break; one.EquipmentShapes[at] = Num(v); at += 1; }
        }
        return one;
    }

    /// <summary>The same, on the way out.</summary>
    private static JsonObject WriteMonster(Monster m) => new()
    {
        ["id"] = m.Id, ["block"] = m.Block, ["x"] = m.X, ["y"] = m.Y, ["facing"] = m.Facing,
        ["direction"] = m.Direction, ["type"] = m.Type, ["mode"] = m.Mode, ["flags"] = m.Flags,
        ["hitPoints"] = m.HitPoints, ["numDistAttacks"] = m.NumDistAttacks, ["distAttackTick"] = m.DistAttackTick,
        ["flyingHeight"] = m.FlyingHeight, ["currentSubFrame"] = m.CurrentSubFrame, ["shiftStep"] = m.ShiftStep,
        ["fightCurTick"] = m.FightCurTick, ["damageReceived"] = m.DamageReceived, ["speedTick"] = m.SpeedTick,
        ["destX"] = m.DestX, ["destY"] = m.DestY, ["destDirection"] = m.DestDirection,
        ["assignedItems"] = m.AssignedItems, ["curDistWeapon"] = m.CurDistWeapon,
        ["equipmentShapes"] = new JsonArray(m.EquipmentShapes.Select(v => (JsonNode)v).ToArray()),
        ["nextAssignedObject"] = 0, ["nextDrawObject"] = 0,
        ["properties"] = null,
    };

    public static void Load(LevelLoader loader, JsonElement save)
    {
        string[] modifierTables = { "CharDefsMan", "CharDefsWoman", "CharDefsKieran", "CharDefsMan", "CharDefsAkshel" };
        var characters = save.GetProperty("characters").EnumerateArray().ToArray();
        for (int i = 0; i < 4 && i < characters.Length; i += 1)
        {
            var src = characters[i];
            var c = new Character();
            int Int(string name, int fallback = 0) => Num(src, name, fallback);
            void Fill(string name, int[] into)
            {
                if (!src.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return;
                int at = 0;
                foreach (var item in v.EnumerateArray()) { if (at >= into.Length) break; into[at++] = Num(item); }
            }
            c.Flags = Int("flags");
            c.Name = src.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : "";
            c.RaceClassSex = Int("raceClassSex");
            c.Id = Int("id");
            c.ScreamSfx = Int("screamSfx");
            c.ItemProtection = Int("itemProtection");
            c.HitPointsCur = Int("hitPointsCur");
            c.HitPointsMax = Int("hitPointsMax");
            c.MagicPointsCur = Int("magicPointsCur");
            c.MagicPointsMax = Int("magicPointsMax");
            c.TotalMightModifier = Int("totalMightModifier");
            c.TotalProtectionModifier = Int("totalProtectionModifier");
            c.Might = Int("might");
            c.Protection = Int("protection");
            c.DamageSuffered = Int("damageSuffered");
            c.WeaponHit = Int("weaponHit");
            Fill("itemsMight", c.ItemsMight);
            Fill("protectionAgainstItems", c.ProtectionAgainstItems);
            Fill("items", c.Items);
            Fill("skillLevels", c.SkillLevels);
            Fill("skillModifiers", c.SkillModifiers);
            Fill("experiencePts", c.ExperiencePts);
            Fill("potionSkillBonus", c.PotionSkillBonus);
            Fill("characterUpdateEvents", c.CharacterUpdateEvents);
            Fill("characterUpdateDelay", c.CharacterUpdateDelay);
            c.CurFaceFrame = Int("curFaceFrame");
            c.TempFaceFrame = Int("tempFaceFrame");
            c.Field41 = Int("field_41");
            if ((c.Flags & 1) != 0)
            {
                // savegame.mjs: cdf[raceClassSex] - five tables, Ak'shel is 4. Masking with 3 gave him the man's.
                c.DefaultModifiers = StaticData.Table(modifierTables[Math.Max(0, Math.Min(c.RaceClassSex, modifierTables.Length - 1))]);
                // Transient combat state must not outlive the timer event that would clear it.
                c.Flags &= ~0x2000;
                bool hasAttackEvent = src.TryGetProperty("characterUpdateEvents", out var events)
                    && events.ValueKind == JsonValueKind.Array && events.EnumerateArray().Any(e => Num(e) == 1);
                if (!hasAttackEvent) { c.Flags &= ~4; c.WeaponHit = 0; }
            }
            loader.Characters[i] = c;
        }

        // Everything the party carry. Without these a loaded game came back with no money, no
        // spells and an empty pack - the save recorded them all along, the load simply threw
        // them away.
        int Value(string name, int fallback = 0) =>
            save.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Num(v) : fallback;
        void FillFrom(string name, int[] into)
        {
            if (!save.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return;
            int at = 0;
            foreach (var item in v.EnumerateArray()) { if (at >= into.Length) break; into[at++] = Num(item); }
        }

        loader.Items.Credits = Value("credits");
        loader.Items.InventoryCurItem = Value("inventoryCurItem");
        FillFrom("inventory", loader.Items.Inventory);
        FillFrom("availableSpells", loader.AvailableSpells);
        loader.Brightness = Value("brightness");
        loader.LampOilStatus = Value("lampOilStatus");
        loader.LampSwitchedOff = save.TryGetProperty("lampSwitchedOff", out var lamp) && lamp.ValueKind == JsonValueKind.True;
        loader.ScriptDirection = Value("scriptDirection", loader.ScriptDirection);
        if (loader.Gui != null)
        {
            loader.Gui.ItemInHand = Value("itemInHand");
            loader.Gui.SelectedCharacter = Math.Clamp(Value("selectedCharacter"), 0, 3);
            loader.Gui.SelectedSpell = Value("selectedSpell");
        }

        loader.PartyBlock = Num(save, "currentBlock");
        loader.PartyDirection = Num(save, "currentDirection");
        int level = Num(save, "currentLevel");

        int at2 = 0;
        foreach (var flag in save.GetProperty("flagsTable").EnumerateArray())
        {
            if (at2 >= loader.Flags.Length) break;
            loader.Flags[at2++] = (byte)Num(flag);
        }
        at2 = 0;
        foreach (var v in save.GetProperty("globalScriptVars").EnumerateArray())
        {
            if (at2 >= loader.GlobalScriptVars.Length) break;
            loader.GlobalScriptVars[at2++] = (short)Num(v);
        }
        at2 = 0;
        foreach (var v in save.GetProperty("globalScriptVars2").EnumerateArray())
        {
            if (at2 >= loader.GlobalScriptVars2.Length) break;
            loader.GlobalScriptVars2[at2++] = (short)Num(v);
        }

        int index = 0;
        foreach (var it in save.GetProperty("itemsInPlay").EnumerateArray())
        {
            if (index >= 400) break;
            var item = new Item
            {
                NextAssignedObject = Num(it, "nextAssignedObject"),
                NextDrawObject = Num(it, "nextDrawObject"),
                FlyingHeight = Num(it, "flyingHeight"),
                Block = Num(it, "block"),
                X = Num(it, "x"),
                Y = Num(it, "y"),
                Level = Num(it, "level"),
                ItemPropertyIndex = Num(it, "itemPropertyIndex"),
                ShpCurFrameFlg = Num(it, "shpCurFrame_flg"),
            };
            loader.Items.InPlay[index++] = item;
        }

        // Normalise the floor chains the way loadState does: only a chain head carries a level tag,
        // and a chain that points back at itself is cut.
        for (int i = 1; i < 400; i += 1)
        {
            var head = loader.Items.InPlay[i];
            if (head.Level < 1 || head.Level > 29) continue;
            var seen = new HashSet<int> { i };
            var previous = head;
            for (int id = head.NextAssignedObject; id != 0; id = loader.Items.InPlay[id].NextAssignedObject)
            {
                if (seen.Contains(id) || (id & 0x8000) != 0) { previous.NextAssignedObject = 0; break; }
                seen.Add(id);
                var it = loader.Items.InPlay[id];
                if (it.Level == head.Level) it.Level = -1;
                previous = it;
            }
        }

        foreach (var entry in save.GetProperty("levels").EnumerateObject())
        {
            int levelIndex = int.Parse(entry.Name);
            var walls = new byte[1024, 4];
            int block = 0;
            foreach (var row in entry.Value.GetProperty("walls").EnumerateArray())
            {
                int side = 0;
                foreach (var w in row.EnumerateArray()) { if (side < 4) walls[block, side++] = (byte)Num(w); }
                block += 1;
                if (block >= 1024) break;
            }
            var flags = new byte[1024];
            int f = 0;
            foreach (var v in entry.Value.GetProperty("flags").EnumerateArray()) { if (f < 1024) flags[f++] = (byte)Num(v); }
            var monsters = entry.Value.GetProperty("monsters").EnumerateArray().Select(ReadMonster).ToArray();
            loader.SetTempData(levelIndex + 1, walls, flags, monsters);
        }

        foreach (var entry in save.TryGetProperty("monsterSpawns", out var spawnField) && spawnField.ValueKind == JsonValueKind.Object
                                 ? spawnField.EnumerateObject()
                                 : default)
            if (int.TryParse(entry.Name, out int spawnLevel))
                loader.SetMonsterSpawns(spawnLevel, entry.Value.EnumerateArray().Select(ReadMonster).ToArray());

        loader.Load(level);
    }

    /// <summary>
    /// saveState, the other way round: the port writes the same JSON the browser build does, field
    /// for field, so a game saved here opens there. Fields the port does not model yet are written
    /// as the engine's own defaults rather than left out - loadState skips what it does not find,
    /// and a missing field would quietly become a zero.
    /// </summary>
    public static string Save(LevelLoader loader, Gui gui = null)
    {
        loader.GenerateTempData();
        loader.TagFloorItemsForSave();

        var save = new JsonObject
        {
            ["version"] = 1,
            ["currentBlock"] = loader.Party.Block,
            ["partyPosX"] = loader.Party.PosX,
            ["partyPosY"] = loader.Party.PosY,
            ["updateFlags"] = 0,
            ["scriptDirection"] = loader.ScriptDirection,
            ["selectedSpell"] = gui?.SelectedSpell ?? 0,
            ["sceneDefaultUpdate"] = 0,
            ["compassBroken"] = 0,
            ["drainMagic"] = 0,
            ["currentDirection"] = loader.Party.Direction,
            ["compassDirection"] = gui?.CompassDirection ?? -1,
            ["selectedCharacter"] = gui?.SelectedCharacter ?? 0,
            ["currentLevel"] = loader.Level,
            ["inventoryCurItem"] = loader.Items.InventoryCurItem,
            ["itemInHand"] = gui?.ItemInHand ?? 0,
            ["lastMouseRegion"] = -1,
            ["brightness"] = loader.Brightness,
            ["lampOilStatus"] = 0,
            ["lampEffect"] = -1,
            ["credits"] = loader.Items.Credits,
            ["hasTempDataFlags"] = loader.HasTempDataFlags,
            ["playTimer"] = 0,
            ["charSelection"] = 0,
            ["lampSwitchedOff"] = false,
        };

        JsonArray Numbers(IEnumerable<int> values)
        {
            var array = new JsonArray();
            foreach (int v in values) array.Add(v);
            return array;
        }

        var characters = new JsonArray();
        foreach (var c in loader.Characters)
        {
            characters.Add(new JsonObject
            {
                ["flags"] = c.Flags,
                ["name"] = c.Name ?? "",
                ["raceClassSex"] = c.RaceClassSex,
                ["id"] = c.Id,
                ["curFaceFrame"] = c.CurFaceFrame,
                ["tempFaceFrame"] = c.TempFaceFrame,
                ["screamSfx"] = c.ScreamSfx,
                ["itemProtection"] = c.ItemProtection,
                ["hitPointsCur"] = c.HitPointsCur,
                ["hitPointsMax"] = c.HitPointsMax,
                ["magicPointsCur"] = c.MagicPointsCur,
                ["magicPointsMax"] = c.MagicPointsMax,
                ["field_41"] = c.Field41,
                ["damageSuffered"] = c.DamageSuffered,
                ["weaponHit"] = c.WeaponHit,
                ["totalMightModifier"] = c.TotalMightModifier,
                ["totalProtectionModifier"] = c.TotalProtectionModifier,
                ["might"] = c.Might,
                ["protection"] = c.Protection,
                ["nextAnimUpdateCountdown"] = 0,
                ["itemsMight"] = Numbers(c.ItemsMight),
                ["protectionAgainstItems"] = Numbers(c.ProtectionAgainstItems),
                ["items"] = Numbers(c.Items),
                ["skillLevels"] = Numbers(c.SkillLevels),
                ["skillModifiers"] = Numbers(c.SkillModifiers),
                ["experiencePts"] = Numbers(c.ExperiencePts),
                ["characterUpdateEvents"] = Numbers(new int[5]),
                ["characterUpdateDelay"] = Numbers(new int[5]),
                ["potionSkillBonus"] = Numbers(new int[3]),
            });
        }
        save["characters"] = characters;
        save["inventory"] = Numbers(loader.Items.Inventory);
        // The engine's own table is 100 bytes; the port keeps a larger one, but a save is a save.
        save["flagsTable"] = Numbers(loader.Flags.Take(100).Select(b => (int)b));
        save["globalScriptVars"] = Numbers(loader.GlobalScriptVars.Select(v => (int)v));
        save["globalScriptVars2"] = Numbers(loader.GlobalScriptVars2.Select(v => (int)v));
        save["availableSpells"] = Numbers(loader.AvailableSpells);

        var items = new JsonArray();
        foreach (var it in loader.Items.InPlay)
        {
            items.Add(new JsonObject
            {
                ["nextAssignedObject"] = it.NextAssignedObject,
                ["nextDrawObject"] = it.NextDrawObject,
                ["flyingHeight"] = it.FlyingHeight,
                ["block"] = it.Block,
                ["x"] = it.X,
                ["y"] = it.Y,
                ["level"] = it.Level,
                ["itemPropertyIndex"] = it.ItemPropertyIndex,
                ["shpCurFrame_flg"] = it.ShpCurFrameFlg,
            });
        }
        save["itemsInPlay"] = items;

        var levels = new JsonObject();
        foreach (var (level, temp) in loader.TempData())
        {
            var walls = new JsonArray();
            for (int b = 0; b < 1024; b += 1)
            {
                var row = new JsonArray();
                for (int i = 0; i < 4; i += 1) row.Add((int)temp.Walls[b, i]);
                walls.Add(row);
            }
            var monsters = new JsonArray();
            foreach (var m in temp.Monsters) monsters.Add(WriteMonster(m));
            levels[(level - 1).ToString()] = new JsonObject
            {
                ["walls"] = walls,
                ["flags"] = Numbers(temp.Flags.Select(b => (int)b)),
                ["monsters"] = monsters,
                ["flyingObjects"] = new JsonArray(),
                ["monsterDifficulty"] = loader.Board.Difficulty,
            };
        }
        save["levels"] = levels;
        // The set each level's script first made, so a later session can still put its monsters back.
        var spawns = new JsonObject();
        foreach (var (level, set) in loader.MonsterSpawns)
        {
            var list = new JsonArray();
            foreach (var m in set) list.Add(WriteMonster(m));
            spawns[level.ToString()] = list;
        }
        save["monsterSpawns"] = spawns;
        save["questDone"] = new JsonArray();
        save["dungeon"] = null;

        return save.ToJsonString();
    }
}
