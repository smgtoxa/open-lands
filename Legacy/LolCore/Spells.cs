// Spells: what a cast costs, what it hits, and how hard.
//
// Transliterated from src/game/spells.mjs (castSpell, checkMagic, getSpellTargetBlock,
// inflictMagicalDamage, inflictMagicalDamageForBlock, processMagicSpark and the damage tables).
//
// A spell in this engine is two things wrapped together: a piece of arithmetic and an animation.
// The arithmetic is here; the animations, and the effects that are inseparable from them, are in
// Magic.cs. This dispatches to them through SpellProcs, which is spellProcs in spells.mjs.
namespace LolCore;

public sealed partial class MonsterBoard
{
    /// <summary>
    /// What each spell does, by spell number - spellProcs in spells.mjs. The loader fills it in from
    /// Magic.cs once the Gui exists; a spell with no entry is one the original has none for either.
    /// </summary>
    public Func<int, int, int>[] SpellProcs = new Func<int, int, int>[21];

    /// <summary>Spells cast that had no procedure at all, which should stay at zero.</summary>
    public int SpellsSkipped;

    /// <summary>activeSpell: what is being cast and by whom, for a spell that asks the player.</summary>
    public Action<int, int, int> OnSpellCast;

    private static int SpellField(int spell, int field) => SpellTable.Field(spell, field);

    private static int SpellMpRequired(int spell, int level) => SpellField(spell, 1 + level);

    private static int SpellHpRequired(int spell, int level) => SpellField(spell, 5 + level);

    private static int SpellFlags(int spell) => SpellField(spell, 9);

    /// <summary>checkMagic: 1 when the caster cannot pay for it.</summary>
    public int CheckMagic(int charNum, int spellNum, int spellLevel)
    {
        var c = Characters[charNum];
        if (SpellMpRequired(spellNum, spellLevel) > c.MagicPointsCur) return 1;
        if (SpellHpRequired(spellNum, spellLevel) >= c.HitPointsCur) return 1;
        return 0;
    }

    /// <summary>getSpellTargetBlock: how far a bolt travels before something stops it.</summary>
    public (int Distance, int Block) GetSpellTargetBlock(int currentBlock, int direction, int maxDistance)
    {
        int targetBlock = 0xffff;
        int c = Party.CalcNewBlockPosition(currentBlock, direction);
        int i = 0;
        for (; i < maxDistance; i += 1)
        {
            if ((_map.AssignedObjects[currentBlock] & 0x8000) != 0) return (i, currentBlock);
            if ((_walls.WallFlags[_map.Walls[c, direction ^ 2]] & 7) != 0) return (i, c);
            currentBlock = c;
            c = Party.CalcNewBlockPosition(currentBlock, direction);
        }
        return (i, targetBlock);
    }

    public void InflictMagicalDamage(int target, int attacker, int damage, int index, int hitType)
    {
        hitType = hitType != 0 ? 1 : 2;
        damage = CalcInflictableDamagePerItem(attacker, target, damage, index, hitType);
        InflictDamage(target, damage, attacker, 2, index);
    }

    public void InflictMagicalDamageForBlock(int block, int attacker, int damage, int index, int currentLevel)
    {
        int o = _map.AssignedObjects[block & 0x3ff];
        while ((o & 0x8000) != 0)
        {
            InflictDamage(o, CalcInflictableDamagePerItem(attacker, o, damage, index, 2), attacker, 2, index);
            if ((Monsters[o & 0x7fff].Flags & 0x20) != 0 && currentLevel != 22) break;
            o = Monsters[o & 0x7fff].NextAssignedObject;
        }
    }

    /// <summary>
    /// castSpell: pay for it, then do it. The paying is the same for every spell; what the spell
    /// then does is its own procedure, and only the ones whose effect is pure arithmetic are here.
    /// </summary>
    public bool CastSpell(int charNum, int spellType, int spellLevel, int currentLevel)
    {
        if ((SpellFlags(spellType) & 0x100) != 0
            && TestWallFlag(Party.CalcNewBlockPosition(Party.Block, Party.Direction), Party.Direction, 1)) return false;
        var c = Characters[charNum];
        if (SpellMpRequired(spellType, spellLevel) > c.MagicPointsCur) return false;
        if (SpellHpRequired(spellType, spellLevel) >= c.HitPointsCur) return false;
        SetCharacterMagicOrHitPoints(charNum, 1, -SpellMpRequired(spellType, spellLevel), 1);
        SetCharacterMagicOrHitPoints(charNum, 0, -SpellHpRequired(spellType, spellLevel), 1);
        RedrawPortrait?.Invoke(charNum);

        OnSpellCast?.Invoke(charNum, spellType, spellLevel);
        var proc = spellType >= 0 && spellType < SpellProcs.Length ? SpellProcs[spellType] : null;
        if (proc != null) proc(charNum, spellLevel);
        else if (spellType == 0) ProcessMagicSpark(charNum, spellLevel);   // no host: the arithmetic alone
        else SpellsSkipped += 1;
        return true;
    }

    /// <summary>
    /// processMagicSpark, without its animation: find what the bolt hits and hurt it.
    /// </summary>
    private void ProcessMagicSpark(int charNum, int spellLevel)
    {
        var (_, targetBlock) = GetSpellTargetBlock(Party.Block, Party.Direction, 4);
        int target = targetBlock == 0xffff ? 0xffff : GetNearestMonsterFromCharacterForBlock(targetBlock, charNum);
        int[] dmg = { 7, 15, 25, 60 };
        if (target != 0xffff) InflictMagicalDamage(target, charNum, dmg[spellLevel & 3], 5, 0);
        // The sparkles the animation scatters roll on the presentation stream, not this one.
    }

    /// <summary>getNearestMonsterFromCharacterForBlock: what a bolt landing in a block would hit.</summary>
    public int GetNearestMonsterFromCharacterForBlock(int block, int charNum)
    {
        int id = 0xffff;
        int minDist = 0x7fff;
        if (block == 0xffff) return id;
        var (cX, cY) = CalcCoordinatesForSingleCharacter(charNum);
        int o = _map.AssignedObjects[block & 0x3ff];
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
}
