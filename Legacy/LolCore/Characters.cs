// The party: the characters, their stats, and the arithmetic of hitting things.
//
// Transliterated from src/game/party.mjs (addCharacter, battleHitSkillTest, calcInflictableDamage,
// calcInflictableDamagePerItem, calculateProtection, inflictDamage, getNearestPartyMemberFromPos)
// and the character table in static-data.
//
// The numbers here decide every fight in the game, and they are all integer maths with deliberate
// truncation - a stray floating-point divide would change damage by one and nothing would look
// wrong until a monster took a hit too many.
namespace LolCore;

public sealed class Character
{
    public int Flags;
    public string Name = "";
    public int RaceClassSex;
    public int Id;
    public int ScreamSfx;
    public int ItemProtection;
    public int HitPointsCur, HitPointsMax;
    public int MagicPointsCur, MagicPointsMax;
    public int TotalMightModifier, TotalProtectionModifier;
    public int Might, Protection;
    public int DamageSuffered, WeaponHit;
    public readonly int[] ItemsMight = new int[8];
    public readonly int[] ProtectionAgainstItems = new int[8];
    public readonly int[] Items = new int[11];
    public readonly int[] SkillLevels = new int[3];
    public readonly int[] SkillModifiers = new int[3];
    public readonly int[] ExperiencePts = new int[3];
    public int[] DefaultModifiers = new int[9];
    /// <summary>The five timed effects a character can be under (a potion's skill bonus is
    /// events 9, 10 and 11), how long each has left, and what it added - so it can be taken back.</summary>
    public readonly int[] CharacterUpdateEvents = new int[5];
    public readonly int[] CharacterUpdateDelay = new int[5];
    public readonly int[] PotionSkillBonus = new int[3];
    /// <summary>Which face the portrait wears, and the one an event asked for.</summary>
    public int CurFaceFrame, TempFaceFrame;

    /// <summary>How long until this face blinks again (party.mjs nextAnimUpdateCountdown).</summary>
    public int NextAnimUpdateCountdown;
    /// <summary>field_41: which of the two shield shapes the portrait's lower panel draws.</summary>
    public int Field41;

    /// <summary>How long this hero's current swing takes, so a host can show how far along it is.</summary>
    public int AttackCooldownTotal;

    public bool Active => (Flags & 1) != 0;
}

public sealed partial class MonsterBoard
{
    private static readonly int[] Modifiers4 = { 256, 256, 320 };

    public Character[] Characters = { new(), new(), new(), new() };

    public int AddCharacter(int id)
    {
        int at = PartyRoster.AddCharacter(id, Characters, Items);
        if (at >= 0 && at < Characters.Length) Characters[at].NextAnimUpdateCountdown = _rng.RollDice(1, 12) + 6;
        return at;
    }
}

/// <summary>Character creation: no level involved, so it stands on its own.</summary>
public static class PartyRoster
{
    /// <summary>addCharacter: the chosen character joins the party, kit and all.</summary>
    public static int AddCharacter(int id, Character[] Characters, ItemBoard Items)
    {
        var numbers = StaticData.Table("CharacterDefNumbers");
        var names = StaticData.Names("CharacterDefNames");
        int stride = StaticData.Table("CharacterDefStride")[0];
        // CharDefsMan/Woman/Kieran/Akshel, in the order the definitions are listed
        string[] modifierTables = { "CharDefsMan", "CharDefsMan", "CharDefsMan", "CharDefsWoman", "CharDefsMan", "CharDefsMan", "CharDefsWoman", "CharDefsKieran", "CharDefsAkshel" };

        int count = Characters.Count(c => c.Active);
        if (count >= 3) return -1;
        int defIndex = -1;
        for (int i = 0; i * stride < numbers.Length; i += 1) if (numbers[i * stride + 2] == id) { defIndex = i; break; }
        if (defIndex < 0) return -1;

        int at = defIndex * stride;
        var c = new Character
        {
            Flags = numbers[at], RaceClassSex = numbers[at + 1], Id = numbers[at + 2], ScreamSfx = numbers[at + 3],
            ItemProtection = numbers[at + 4], HitPointsCur = numbers[at + 5], HitPointsMax = numbers[at + 6],
            MagicPointsCur = numbers[at + 7], MagicPointsMax = numbers[at + 8],
            TotalMightModifier = numbers[at + 9], TotalProtectionModifier = numbers[at + 10],
            Might = numbers[at + 11], Protection = numbers[at + 12],
            Name = defIndex < names.Length ? names[defIndex] : "",
            DefaultModifiers = StaticData.Table(modifierTables[defIndex]),
        };
        int p = at + 13;
        for (int i = 0; i < 8; i += 1) c.ItemsMight[i] = numbers[p + i];
        for (int i = 0; i < 8; i += 1) c.ProtectionAgainstItems[i] = numbers[p + 8 + i];
        for (int i = 0; i < 11; i += 1) c.Items[i] = numbers[p + 16 + i];
        for (int i = 0; i < 3; i += 1) c.SkillLevels[i] = numbers[p + 27 + i];
        for (int i = 0; i < 3; i += 1) c.SkillModifiers[i] = numbers[p + 30 + i];
        for (int i = 0; i < 3; i += 1) c.ExperiencePts[i] = numbers[p + 33 + i];

        Characters[count] = c;
        // the items are made by the caller, in the order the engine makes them
        for (int i = 0; i < 11; i += 1) if (c.Items[i] != 0) c.Items[i] = Items.Make(c.Items[i], 0, 0, 1);
        return count;
    }
}

public sealed partial class MonsterBoard
{
    public int CountActiveCharacters() => Characters.Count(c => c.Active);

    private int[] StatsOf(int id) => (id & 0x8000) != 0 ? Monsters[id & 0x7fff].Properties.FightingStats : Characters[id].DefaultModifiers;

    private int[] ItemsMightOf(int id) => (id & 0x8000) != 0 ? Monsters[id & 0x7fff].Properties.ItemsMight : Characters[id].ItemsMight;

    private int[] ProtectionAgainstItemsOf(int id)
        => (id & 0x8000) != 0 ? Monsters[id & 0x7fff].Properties.ProtectionAgainstItems : Characters[id].ProtectionAgainstItems;

    /// <summary>calculateProtection: how much of a blow the target soaks up.</summary>
    public int CalculateProtection(int index)
    {
        if ((index & 0x8000) != 0)
        {
            var m = Monsters[index & 0x7fff];
            return (m.Properties.ItemProtection * m.Properties.FightingStats[2]) >> 8;
        }
        var c = Characters[index];
        int value = c.ItemProtection + c.Protection;
        value = (value * c.DefaultModifiers[2]) >> 8;
        return (value * c.TotalProtectionModifier) >> 8;
    }

    /// <summary>battleHitSkillTest: 0 missed, 1 hit, 2 a clean hit that ignores protection.</summary>
    public int BattleHitSkillTest(int attacker, int target, int skill)
    {
        if (target == -1 || target == 0xffff) return 0;
        if (attacker == -1) return 1;
        if ((target & 0x8000) != 0 && Monsters[target & 0x7fff].Mode >= 13) return 0;
        int hitChanceModifier, sk;
        if ((attacker & 0x8000) != 0)
        {
            // as in the original: the modifiers come from the *target's* row here
            hitChanceModifier = Monsters[target & 0x7fff].Properties.FightingStats[0];
            sk = 100 - Monsters[target & 0x7fff].Properties.SkillLevel;
        }
        else
        {
            hitChanceModifier = Characters[attacker].DefaultModifiers[0];
            int m = Characters[attacker].SkillModifiers[skill];
            if (skill == 1) m *= 3;
            sk = 100 - (Characters[attacker].SkillLevels[skill] + m);
        }
        int evadeChanceModifier;
        if ((target & 0x8000) != 0)
        {
            evadeChanceModifier = Monsters[target & 0x7fff].Properties.FightingStats[3];
            evadeChanceModifier = (evadeChanceModifier * Modifiers4[Difficulty]) >> 8;
            Monsters[target & 0x7fff].Flags |= 0x10;
        }
        else evadeChanceModifier = Characters[target].DefaultModifiers[3];

        int r = _rng.RollDice(1, 100);
        if (r >= sk) return 2;
        // A zero hit chance is Infinity in the engine, and every roll is under Infinity: a miss.
        if (hitChanceModifier == 0) { OnMiss?.Invoke(attacker, target); return 0; }
        int v = (evadeChanceModifier << 8) / hitChanceModifier;
        if (r < v) { OnMiss?.Invoke(attacker, target); return 0; }
        return 1;
    }

    /// <summary>
    /// What a blow did, for a host that shows it: a miss, damage dealt, or a monster killed. The
    /// engine raises these from inflictDamage and the browser builds its combat log out of them.
    /// </summary>
    /// <summary>The hero's own cry when something lands on them.</summary>
    public Action<int> PlaySoundEffect;

    /// <summary>
    /// The lower half of setCharacterMagicOrHitPoints: the bar under the portrait, redrawn at the
    /// new value. Presentation, so the host owns it - (charNum, type, newVal, pointsMax).
    /// </summary>
    public Action<int, int, int, int> DrawPointsBar;

    /// <summary>
    /// setCharacterMagicOrHitPoints: the one place a hero's health or magic changes. It clamps to
    /// what they can hold, marks a hero brought to zero as down, and redraws the bar.
    /// </summary>
    public void SetCharacterMagicOrHitPoints(int charNum, int type, int points, int mode)
    {
        if (charNum > 2) return;
        var c = Characters[charNum];
        if (!c.Active) return;
        int pointsMax = type != 0 ? c.MagicPointsMax : c.HitPointsMax;
        int pointsCur = type != 0 ? c.MagicPointsCur : c.HitPointsCur;
        int newVal = mode == 2 ? pointsMax + points : mode != 0 ? pointsCur + points : points;
        newVal = Math.Max(0, Math.Min(pointsMax, newVal));
        if (type != 0) c.MagicPointsCur = newVal;
        else
        {
            c.HitPointsCur = newVal;
            if (c.HitPointsCur < 1) c.Flags |= 8;
        }
        if (((UpdateFlagsNow?.Invoke() ?? 0) & 2) != 0) return;
        DrawPointsBar?.Invoke(charNum, type, newVal, pointsMax);
    }

    public Action<int, int> OnMiss;                       // attacker, target
    public Action<int, int, int, bool> OnDamage;          // who, damage, attacker, target is a monster
    public Action<int, int> OnMonsterSlain;               // monster id, attacker

    public int CalcInflictableDamage(int attacker, int target, int hitType)
    {
        var might = ItemsMightOf(attacker);
        int res = 0;
        for (int i = 0; i < 8; i += 1) res += CalcInflictableDamagePerItem(attacker, target, might[i], i, hitType);
        return res;
    }

    public int CalcInflictableDamagePerItem(int attacker, int target, int itemMight, int index, int hitType)
    {
        int dmg = attacker == -1 ? 0x100 : StatsOf(attacker)[1];
        var st = ProtectionAgainstItemsOf(target);
        dmg = (dmg * itemMight) >> 8;
        if (dmg == 0) return 0;
        if ((attacker & 0x8000) == 0)
        {
            dmg = (dmg * Characters[attacker].TotalMightModifier) >> 8;
            if (dmg == 0) return 0;
        }
        int d = (short)((index & 0x80) != 0 ? st[7] : st[index]);
        int r = (dmg * Math.Abs(d)) >> 8;
        dmg = d < 0 ? -r : r;
        if (hitType == 2 || dmg == 0) return dmg == 1 ? 2 : dmg;
        dmg = (dmg * (256 - Math.Min((CalculateProtection(target) << 7) / dmg, 217))) >> 8;
        return dmg < 2 ? 2 : dmg;
    }

    /// <summary>inflictDamage, for the two things that can be hurt. Experience and the interface
    /// belong to later stages; the numbers and the state changes are here.</summary>
    public int InflictDamage(int target, int damage, int attacker, int skill, int flags)
    {
        if ((target & 0x8000) != 0)
        {
            var m = Monsters[target & 0x7fff];
            if (m.Mode >= 13) return 0;
            if (damage > 0)
            {
                m.HitPoints -= damage;
                m.DamageReceived = 0x8000 | damage;
                m.Flags |= 0x10;
                m.HitOffsX = _rng.RollDice(1, 24) - 12;
                m.HitOffsY = _rng.RollDice(1, 24) - 12;
                OnDamage?.Invoke(m.Id, damage, attacker, true);
                m.HitPoints = Math.Max(0, Math.Min(m.Properties.HitPoints, m.HitPoints));
                if ((attacker & 0x8000) == 0) ApplyMonsterDefenseSkill(m, attacker, flags, skill, damage);
                QueueEnvironmentalSoundEffect(m.Properties.Sounds[2], m.Block);
                SceneUpdateRequired = true;
                if (m.HitPoints <= 0)
                {
                    m.HitPoints = 0;
                    if ((attacker & 0x8000) == 0 && attacker != -1) IncreaseExperience(attacker, skill, m.Properties.HitPoints);
                    OnMonsterSlain?.Invoke(m.Id, attacker);
                    SetMonsterMode(m, 13);
                }
            }
            else
            {
                m.HitPoints -= damage;
                m.HitPoints = Math.Max(1, Math.Min(m.Properties.HitPoints, m.HitPoints));
            }
            AfterDamage(attacker, skill, damage);
            return damage;
        }

        if (target > 3)
        {
            int i = Array.FindIndex(Characters, c => c.Id == target);
            if (i < 0) return 0;
            target = i;
        }
        var ch = Characters[target];
        if (!ch.Active || (ch.Flags & 8) != 0) return 0;
        if ((ch.Flags & 0x1000) == 0) PlaySoundEffect?.Invoke(ch.ScreamSfx);
        ShowTemporaryFaceFrame?.Invoke(target, 6, 4, false);
        // The ring that turns fire aside takes three quarters of it.
        if (flags == 4 && (WearsItem?.Invoke(target, 229) ?? false)) damage >>= 2;
        OnDamage?.Invoke(target, damage, attacker, false);
        SetCharacterMagicOrHitPoints(target, 0, -damage, 1);
        if (ch.HitPointsCur <= 0)
        {
            // characterHitpointsZero: down, and every effect on them goes with it
            ch.HitPointsCur = 0;
            ch.Flags |= 8;
            RemoveCharacterEffects(ch, 1, 5);
            PartyDamageFlags = flags;
        }
        else
        {
            ch.DamageSuffered = damage;
            SetCharacterUpdateEvent(target, 2, 4, true);
        }
        RedrawPortrait?.Invoke(target);
        AfterDamage(attacker, skill, damage);
        return damage;
    }

    /// <summary>
    /// applyMonsterDefenseSkill: what a monster does back when it is hit - steal, shrug it off,
    /// turn and run, or drink the blow. The roll happens whatever the skill, so it is part of the
    /// dice stream; the item-stealing arms of it need the inventory and are counted instead.
    /// </summary>
    private void ApplyMonsterDefenseSkill(Monster monster, int attacker, int flags, int skill, int damage)
    {
        if (_rng.RollDice(1, 100) > monster.Properties.DefenseSkillChance) return;
        switch (monster.Properties.DefenseSkillType - 1)
        {
            case 0:
            case 1:
                if ((flags & 0x3f) == 2 || skill != 0) return;
                DefenseSkillsSkipped += 1;   // stealing or destroying a weapon: the inventory stage
                break;
            case 2:
                if ((flags & 0x80) == 0) return;
                monster.Flags |= 8;
                monster.Direction = CalcMonsterDirection(monster.X, monster.Y, Party.PosX, Party.PosY) ^ 4;
                SetMonsterMode(monster, 9);
                monster.FightCurTick = 30;
                break;
            case 3:
                if (flags != 3) return;
                monster.HitPoints = Math.Min(monster.Properties.HitPoints, monster.HitPoints + damage);
                break;
            case 4:
                if ((flags & 0x80) == 0) return;
                monster.HitPoints = Math.Min(monster.Properties.HitPoints, monster.HitPoints + damage);
                break;
            case 5:
                if ((flags & 0x84) == 0x84) monster.NumDistAttacks += 1;
                break;
        }
    }

    /// <summary>Defence skills the port counted but did not carry out.</summary>
    public int DefenseSkillsSkipped;

    /// <summary>The tail of inflictDamage: the attacker remembers the blow and learns from it.</summary>
    private void AfterDamage(int attacker, int skill, int damage)
    {
        if ((attacker & 0x8000) != 0 || attacker == -1) return;
        if (skill == 0) Characters[attacker].WeaponHit = damage;
        IncreaseExperience(attacker, skill, damage);
    }

    /// <summary>increaseExperience: skills go up, and with them hit points and magic.</summary>
    /// <summary>
    /// Said when a hero gains a level: (who, the string id). The loader puts the name into it and
    /// prints it on channel 0x8003, which is the channel the engine uses for it.
    /// </summary>
    public Action<int, int> SayLevelGained;

    public void IncreaseExperience(int charNum, int skill, int points)
    {
        if ((charNum & 0x8000) != 0) return;
        var c = Characters[charNum];
        if ((c.Flags & 8) != 0) return;
        var requirements = StaticData.Table("ExpRequirements");
        c.ExperiencePts[skill] += points;
        while (c.SkillLevels[skill] < requirements.Length && c.ExperiencePts[skill] >= requirements[c.SkillLevels[skill]])
        {
            c.SkillLevels[skill] += 1;
            c.Flags |= 0x200 << skill;
            int inc;
            if (skill == 0)
            {
                SayLevelGained?.Invoke(charNum, 0x4023);
                inc = _rng.RollDice(4, 6);
                c.HitPointsCur += inc;
                c.HitPointsMax += inc;
            }
            else if (skill == 1)
            {
                SayLevelGained?.Invoke(charNum, 0x4025);
                inc = _rng.RollDice(2, 6);
                c.HitPointsCur += inc;
                c.HitPointsMax += inc;
            }
            else if (skill == 2)
            {
                SayLevelGained?.Invoke(charNum, 0x4024);
                inc = (c.DefaultModifiers[6] * (_rng.RollDice(1, 8) + 17)) >> 8;
                c.MagicPointsCur += inc;
                c.MagicPointsMax += inc;
                inc = _rng.RollDice(1, 6);
                c.HitPointsCur += inc;
                c.HitPointsMax += inc;
            }
        }
    }

    /// <summary>removeCharacterItem: takes the first matching equipped item off, script and all.</summary>
    public int RemoveCharacterItem(int charNum, int itemFlags)
    {
        for (int i = 0; i < 11; i += 1)
        {
            int s = Characters[charNum].Items[i];
            if (((1 << i) & itemFlags) == 0 || s == 0) continue;
            Characters[charNum].Items[i] = 0;
            OnItemScript?.Invoke(charNum, s, 0x100);
            return s;
        }
        return 0;
    }

    /// <summary>Runs an item's script: (character, item, flags). Wired to the level loader.</summary>
    public Action<int, int, int> OnItemScript;

    /// <summary>getNearestMonsterFromCharacter: what a character's swing would land on.</summary>
    public int GetNearestMonsterFromCharacter(int charNum, int partyBlock, int direction)
    {
        int block = Party.CalcNewBlockPosition(partyBlock, direction);
        int id = 0xffff;
        int minDist = 0x7fff;
        var (cX, cY) = CalcCoordinatesForSingleCharacter(charNum);
        int o = _map.AssignedObjects[block];
        while ((o & 0x8000) != 0)
        {
            var m = Monsters[o & 0x7fff];
            if (m.Mode >= 13) { o = m.NextAssignedObject; continue; }
            int d = Math.Abs(cX - m.X) + Math.Abs(cY - m.Y);
            if (d < minDist) { minDist = d; id = o; }
            o = m.NextAssignedObject;
        }
        return id;
    }

    /// <summary>getNearestPartyMemberFromPos: who a monster standing there would swing at.</summary>
    public int GetNearestPartyMemberFromPos(int x, int y)
    {
        int id = 0xffff;
        int minDist = 0x7fff;
        for (int i = 0; i < 4; i += 1)
        {
            if (!Characters[i].Active || Characters[i].HitPointsCur <= 0) continue;
            var (cx, cy) = CalcCoordinatesForSingleCharacter(i);
            int d = Math.Abs(x - cx) + Math.Abs(y - cy);
            if (d < minDist) { minDist = d; id = i; }
        }
        return id;
    }

    /// <summary>calcCoordinatesForSingleCharacter: where in the block a party member stands.</summary>
    public (int X, int Y) CalcCoordinatesForSingleCharacter(int charNum)
    {
        int[] xOffsets = { 0x80, 0x00, 0x00, 0x40, 0xc0, 0x00, 0x40, 0x80, 0xc0 };
        int c = CountActiveCharacters();
        if (c == 0) return (0, 0);
        c = (c - 1) * 3 + charNum;
        var (x, y) = CalcCoordinatesAddDirectionOffset(xOffsets[c], 0x80, Party.Direction);
        x |= Party.PosX & 0xff00;
        y |= Party.PosY & 0xff00;
        return (x, y);
    }

    public static (int X, int Y) CalcCoordinatesAddDirectionOffset(int x, int y, int direction)
    {
        if (direction == 0) return (x, y);
        int tx = x, ty = y;
        if ((direction & 1) != 0) (tx, ty) = (ty, tx);
        if (direction != 1) ty = (ty - 256) * -1;
        if (direction != 3) tx = (tx - 256) * -1;
        return (tx & 0xffff, ty & 0xffff);
    }
}
