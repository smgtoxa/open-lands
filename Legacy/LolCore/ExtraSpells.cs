// The spells the port adds on top of the game's own.
//
// Transliterated from src/game/extra-spells.mjs. Nothing in the original data knows about them:
// they are appended to the spell table at start-up, carry their own names (the language files
// cannot be extended), and are built only out of engine primitives that already exist - damage to
// a block, healing, an item on the floor, a step behind a monster.
//
// The briar sprite the Wall of Thorns leaves behind is drawn here rather than loaded, against the
// level's own palette, because the original art has no briars.
namespace LolCore;

public sealed class ExtraSpell
{
    public string Id = "", Name = "";
    public int[] Mp = Array.Empty<int>();
    public int Flags;
    public int[] Damage, Heal, Seconds;
    public double[] Share, Bonus;
}

public sealed partial class Gui
{
    /// <summary>The added spells, in the order they take slots after the game's own.</summary>
    public static readonly ExtraSpell[] ExtraSpells =
    {
        new()
        {
            Id = "drain", Name = "Drain", Mp = new[] { 12, 22, 38, 60 }, Flags = 0x100,
            Damage = new[] { 8, 14, 24, 38 }, Heal = new[] { 10, 18, 28, 40 },
            Share = new[] { 0.35, 0.35, 0.4, 0.4 },
        },
        new()
        {
            Id = "thorns", Name = "Wall of Thorns", Mp = new[] { 15, 28, 45, 70 }, Flags = 0x100,
            Damage = new[] { 4, 7, 11, 17 }, Seconds = new[] { 10, 14, 18, 24 },
        },
        new()
        {
            Id = "viper", Name = "Viper", Mp = new[] { 14, 25, 40, 64 }, Flags = 0x100,
            Damage = new[] { 9, 16, 26, 42 },
        },
        new()
        {
            Id = "backstab", Name = "Backstab", Mp = new[] { 20, 30, 45, 65 }, Flags = 0x100,
            Bonus = new[] { 1.0, 1.25, 1.5, 2.0 },
        },
    };

    /// <summary>Where the added spells start in the spell table.</summary>
    public int ExtraSpellBase = -1;

    /// <summary>Briars burning down on a block: how long they last and how hard they bite.</summary>
    public sealed class ThornPatch
    {
        public int Block, Level, Ticks, Damage, CharNum, Wait, Item;
    }

    public readonly List<ThornPatch> ThornBlocks = new();

    /// <summary>uiRegisterExtraSpells: the added spells take the slots after the game's own.</summary>
    public Dictionary<string, int> RegisterExtraSpells()
    {
        if (ExtraSpellBase < 0) ExtraSpellBase = SpellTable.ExtraBase;
        var ids = new Dictionary<string, int>();
        for (int i = 0; i < ExtraSpells.Length; i += 1)
        {
            int spell = ExtraSpellBase + i;
            ids[ExtraSpells[i].Id] = spell;
            // The added spell takes its row in the table: no name code (the language files cannot be
            // extended, so the name is its own), its magic cost, and no cost in blood.
            SpellTable.Write(spell, 0, ExtraSpells[i].Mp, null, ExtraSpells[i].Flags);
        }
        // Anything the party already knows must resolve too - a save from a build with different
        // added spells would otherwise leave a hole for the first reader of the table to fall into.
        SpellTable.EnsureResolvable(AvailableSpells);
        return ids;
    }

    public ExtraSpell ExtraSpellAt(int spell)
    {
        if (ExtraSpellBase < 0 || spell < ExtraSpellBase) return null;
        int at = spell - ExtraSpellBase;
        return at < ExtraSpells.Length ? ExtraSpells[at] : null;
    }

    /// <summary>uiLearnSpell: the first free slot of the scroll, as the game's own scrolls do.</summary>
    public int LearnSpell(int spell)
    {
        for (int i = 0; i < AvailableSpells.Length; i += 1) if (AvailableSpells[i] == spell) return -1;
        for (int i = 0; i < AvailableSpells.Length; i += 1)
        {
            if (AvailableSpells[i] != -1) continue;
            AvailableSpells[i] = spell;
            return i;
        }
        return -1;
    }

    public bool KnowsSpell(int spell) => AvailableSpells.Contains(spell);

    /// <summary>The hit points standing on a block, which is how these spells measure themselves.</summary>
    public int MonstersOnBlockHp(int block)
    {
        int hp = 0;
        int o = _loader.Map.AssignedObjects[block & 0x3ff];
        int guard = 0;
        while ((o & 0x8000) != 0 && guard++ < 32)
        {
            var m = _loader.Board.Monsters[o & 0x7fff];
            if (m.HitPoints > 0) hp += m.HitPoints;
            o = m.NextAssignedObject;
        }
        return hp;
    }

    /// <summary>
    /// castExtraSpell: which of the four. What it costs is not paid here - the game's own castSpell
    /// takes the mana before it calls the spell, and these hang off the same table.
    /// </summary>
    public bool CastExtraSpell(int spell, int charNum, int level)
    {
        var def = ExtraSpellAt(spell);
        if (def == null) return false;
        switch (def.Id)
        {
            case "drain": CastDrain(def, charNum, level); break;
            case "thorns": CastThorns(def, charNum, level); break;
            case "viper": CastViper(def, charNum, level); break;
            case "backstab": CastBackstab(def, charNum, level); break;
        }
        return true;
    }

    /// <summary>Drain: hurt what stands ahead, and give the caster part of what was actually taken.</summary>
    private void CastDrain(ExtraSpell def, int charNum, int level)
    {
        int ahead = Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction);
        int before = MonstersOnBlockHp(ahead);
        if (before == 0)
        {
            _loader.Text?.PrintMessage(2, "There is nothing there to drain.");
            return;
        }
        _loader.Board.InflictMagicalDamageForBlock(ahead, charNum, def.Damage[level], 0x80, _loader.Level);
        int taken = Math.Max(0, before - MonstersOnBlockHp(ahead));
        int back = Math.Min(def.Heal[level], JsMath.RoundToInt(taken * def.Share[level]));
        if (back > 0)
        {
            IncreaseCharacterHitpoints(charNum, back);
            DrawCharPortraitWithStats(charNum);
            _loader.Text?.PrintMessage(0, $"{Characters[charNum].Name} drains {back} health.");
        }
    }

    /// <summary>Viper: a serpent down the passage, striking the first thing within three blocks.</summary>
    private void CastViper(ExtraSpell def, int charNum, int level)
    {
        int block = _loader.Party.Block;
        for (int d = 0; d < 3; d += 1)
        {
            if ((_loader.Map.AssignedObjects[block] & 0x8000) != 0) break;
            int next = Party.CalcNewBlockPosition(block, _loader.Party.Direction);
            if ((_loader.Walls.WallFlags[_loader.Map.Walls[next, _loader.Party.Direction ^ 2]] & 7) != 0) break;
            block = next;
        }
        int before = MonstersOnBlockHp(block);
        _loader.Board.InflictMagicalDamageForBlock(block, charNum, def.Damage[level], 0x80, _loader.Level);
        int dealt = Math.Max(0, before - MonstersOnBlockHp(block));
        _loader.Text?.PrintMessage(0, dealt != 0 ? $"The serpent strikes for {dealt}." : "The serpent finds nothing to strike.");
    }

    /// <summary>
    /// Wall of Thorns: briars on the block ahead, which bite whatever stands in them until they
    /// wither. They are a real object on the block, so the scene draws them like anything else.
    /// </summary>
    private void CastThorns(ExtraSpell def, int charNum, int level)
    {
        int ahead = Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction);
        int ticks = JsMath.RoundToInt(def.Seconds[level] * 1000.0 / (15 * LevelLoader.TickLength));
        var existing = ThornBlocks.FirstOrDefault(t => t.Block == ahead && t.Level == _loader.Level);
        if (existing != null)
        {
            existing.Ticks = Math.Max(existing.Ticks, ticks);
            existing.Damage = def.Damage[level];
        }
        else
        {
            int item = 0;
            int prop = BriarProperty();
            if (prop >= 0)
            {
                item = _loader.Items.Make(prop, 0, 0, _loader.Level);
                if (item > 0)
                {
                    // Queued, not placed: the engine puts the briars down on its next drain, and a
                    // floor counted before that drain is a floor without them.
                    int placed = item;
                    _loader.Queue(() =>
                    {
                        var (x, y) = Party.CalcCoordinates(ahead, 0x40, 0x40);
                        _loader.Board.SetItemPosition(placed, x, y, 0, true);
                    });
                }
                else item = 0;
            }
            ThornBlocks.Add(new ThornPatch
            {
                Block = ahead, Level = _loader.Level, Ticks = ticks,
                Damage = def.Damage[level], CharNum = charNum, Wait = 0, Item = item,
            });
        }
        _loader.Text?.PrintMessage(0, "Briars burst from the floor.");
    }

    /// <summary>processThorns: the briars' own timer - a bite, a pause, and then they wither.</summary>
    public void ProcessThorns()
    {
        if (ThornBlocks.Count == 0) return;
        foreach (var t in ThornBlocks)
        {
            if (t.Level != _loader.Level) continue;
            t.Ticks -= 1;
            if (t.Wait > 0) { t.Wait -= 1; continue; }
            if (MonstersOnBlockHp(t.Block) != 0)
            {
                _loader.Board.InflictMagicalDamageForBlock(t.Block, t.CharNum, t.Damage, 0x80, _loader.Level);
                t.Wait = 12;   // about three seconds between bites
            }
        }
        foreach (var t in ThornBlocks)
        {
            if (t.Ticks > 0 || t.Item == 0) continue;
            _loader.Items.Delete(t.Item);
            _loader.Map.Direction[t.Block] = 5;
            t.Item = 0;
        }
        ThornBlocks.RemoveAll(t => t.Ticks <= 0);
    }

    /// <summary>Backstab: slip behind the creature ahead and strike it without a to-hit roll.</summary>
    private void CastBackstab(ExtraSpell def, int charNum, int level)
    {
        int dir = _loader.Party.Direction;
        int ahead = Party.CalcNewBlockPosition(_loader.Party.Block, dir);
        int target = 0;
        int o = _loader.Map.AssignedObjects[ahead];
        int guard = 0;
        while ((o & 0x8000) != 0 && guard++ < 32)
        {
            if (_loader.Board.Monsters[o & 0x7fff].HitPoints > 0) { target = o; break; }
            o = _loader.Board.Monsters[o & 0x7fff].NextAssignedObject;
        }
        if (target == 0)
        {
            _loader.Text?.PrintMessage(2, "There is nobody ahead to creep behind.");
            return;
        }
        int behind = Party.CalcNewBlockPosition(ahead, dir);
        if (behind == ahead || _loader.Party.TestWallFlag(behind, dir, 1))
        {
            _loader.Text?.PrintMessage(2, "There is no room behind it.");
            return;
        }
        _loader.Party.MoveTo(behind);
        _loader.Party.Direction = dir ^ 2;
        int might = JsMath.RoundToInt(_loader.Board.CalcInflictableDamage(charNum, target, 1) * def.Bonus[level]);
        int hpBefore = _loader.Board.Monsters[target & 0x7fff].HitPoints;
        _loader.Board.InflictDamage(target, Math.Max(2, might), charNum, 0, 0);
        int dealt = Math.Max(0, hpBefore - _loader.Board.Monsters[target & 0x7fff].HitPoints);
        _loader.Text?.PrintMessage(0, $"{Characters[charNum].Name} strikes from behind for {dealt}.");
        DrawCharPortraitWithStats(charNum);
    }

    /// <summary>The briar object: its own item property, and the sprite drawn for it.</summary>
    public int BriarProperty()
    {
        if (_briarProp >= 0 && _briarLevel == _loader.Level) return _briarProp;
        var shape = ExtraSpellArt.BriarShape(_screen.Palette(0));
        int shpIndex = GameShapes.Length;
        var shapes = GameShapes.ToList();
        shapes.Add(shape);
        GameShapes = shapes.ToArray();
        var icons = ItemIconShapes.ToList();
        while (icons.Count < shpIndex) icons.Add(null);
        icons.Add(shape);
        ItemIconShapes = icons.ToArray();
        int prop = _loader.Items.Properties.Length;
        var props = _loader.Items.Properties.ToList();
        props.Add(new ItemProperty
        {
            NameStringId = 0, ShpIndex = shpIndex, Flags = 0x44, Type = 0,
            ItemScriptFunc = 0xff, Might = 0, Skill = 0, Protection = 0, UnkB = 0,
        });
        _loader.Items.Properties = props.ToArray();
        _briarProp = prop;
        _briarLevel = _loader.Level;
        return prop;
    }

    private int _briarProp = -1, _briarLevel = -1;
}
