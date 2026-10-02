// The five slots each hero carries for things that wear off: a swing recovering, a poison ticking,
// a paralysis expiring, a potion fading.
//
// Transliterated from src/game/party.mjs (setCharacterUpdateEvent, removeCharacterEffects,
// paralyzePoisonCharacter, timerSpecialCharacterUpdate). Timer 3 is what counts them down, and
// without it a port has no poison, no paralysis, no potion that ends and no cooldown between
// blows - every one of them is set and then never looked at again.
namespace LolCore;

public sealed partial class MonsterBoard
{
    /// <summary>Asked for when an event needs the host's timer 3 running.</summary>
    public Action EnableEffectTimer;

    /// <summary>Asked for when nothing is left to count down.</summary>
    public Action DisableEffectTimer;

    /// <summary>What the hero is told, and what the portrait is asked to redraw.</summary>
    /// <remarks>
    /// The message is a string id from the game's own table, not English written here: (charNum,
    /// the message channel, the string id). The loader puts the hero's name into it.
    /// </remarks>
    public Action<int, int, int> SayAboutCharacter;
    public Action<int> RedrawPortrait;

    /// <summary>The two effects that are the host's to play: a face frame, and the swamp thawing.</summary>
    public Action<int, int, int, bool> ShowTemporaryFaceFrame;
    public Action RestoreSwampPalette;

    /// <summary>Whether a hero is wearing a given kind of item (the loader owns the check).</summary>
    public Func<int, int, bool> WearsItem;

    /// <summary>setCharacterUpdateEvent: one of the five slots takes the job.</summary>
    public void SetCharacterUpdateEvent(int charNum, int updateType, int updateDelay, bool overwrite)
    {
        var c = Characters[charNum];
        for (int i = 0; i < 5; i += 1)
        {
            if (c.CharacterUpdateEvents[i] != 0 && (!overwrite || c.CharacterUpdateEvents[i] != updateType)) continue;
            c.CharacterUpdateEvents[i] = updateType;
            c.CharacterUpdateDelay[i] = updateDelay;
            EnableEffectTimer?.Invoke();
            break;
        }
    }

    /// <summary>removeCharacterEffects: a range of them cleared at once.</summary>
    public void RemoveCharacterEffects(Character c, int first, int last)
    {
        for (int i = first; i <= last; i += 1)
        {
            switch (i - 1)
            {
                case 0: c.Flags &= 0xfffb; c.WeaponHit = 0; break;
                case 1: c.DamageSuffered = 0; break;
                case 2: c.Flags &= 0xffbf; break;
                case 3: c.Flags &= 0xff7f; break;
                case 4: c.Flags &= 0xfeff; break;
                case 6: c.Flags &= 0xefff; break;
            }
            for (int ii = 0; ii < 5; ii += 1)
            {
                if (i != c.CharacterUpdateEvents[ii]) continue;
                c.CharacterUpdateEvents[ii] = 0;
                c.CharacterUpdateDelay[ii] = 0;
            }
        }
        EnableEffectTimer?.Invoke();
    }

    /// <summary>calcMonsterSkillLevel for a hero rather than a monster.</summary>
    public int CalcCharacterSkillLevel(int charNum, int a)
    {
        var c = Characters[charNum];
        int denominator = Math.Max(1, StatsOf(charNum)[4]);
        int r = (a << 8) / denominator;
        int sk = c.SkillLevels[1];
        if (sk > 7) r -= r >> 1;
        else if (sk > 3) r -= r >> 2;
        return r;
    }

    /// <summary>paralyzePoisonCharacter: a bite that sticks, if it gets through.</summary>
    public int ParalyzePoisonCharacter(int charNum, int typeFlag, int immunityFlags, int hitChance, int redraw)
    {
        var c = Characters[charNum];
        if ((c.Flags & 1) == 0 || (c.Flags & immunityFlags) != 0) return 0;
        if (_rng.RollDice(1, 100) > hitChance) return 0;
        int r = 0;
        if (typeFlag == 0x40)
        {
            c.Flags |= 0x40;
            SetCharacterUpdateEvent(charNum, 3, 3600, true);
            r = 1;
        }
        else if (typeFlag == 0x80 && !(WearsItem?.Invoke(charNum, 225) ?? false))
        {
            c.Flags |= 0x80;
            SetCharacterUpdateEvent(charNum, 4, 10, true);
            SayAboutCharacter?.Invoke(charNum, 6, 0x4021);   // the loader asks characterSays first
            r = 1;
        }
        else if (typeFlag == 0x1000)
        {
            c.Flags |= 0x1000;
            SetCharacterUpdateEvent(charNum, 7, 120, true);
            r = 1;
        }
        if (r != 0 && redraw != 0) RedrawPortrait?.Invoke(charNum);
        return r;
    }

    /// <summary>paralyzePoisonAllCharacters: the whole party rolls against it, one redraw at the end.</summary>
    public void ParalyzePoisonAllCharacters(int typeFlag, int immunityFlags, int hitChance)
    {
        bool any = false;
        for (int i = 0; i < 4; i += 1) if (ParalyzePoisonCharacter(i, typeFlag, immunityFlags, hitChance, 0) != 0) any = true;
        if (any) for (int i = 0; i < 4; i += 1) RedrawPortrait?.Invoke(i);
    }

    /// <summary>stunCharacter: a blow that leaves a hero reeling for twenty ticks.</summary>
    public void StunCharacter(int charNum)
    {
        var c = Characters[charNum];
        if ((c.Flags & 1) == 0 || (c.Flags & 0x108) != 0) return;
        c.Flags |= 0x100;
        SetCharacterUpdateEvent(charNum, 5, 20, true);
        RedrawPortrait?.Invoke(charNum);
        SayAboutCharacter?.Invoke(charNum, 6, 0x4026);
    }

    /// <summary>
    /// timerSpecialCharacterUpdate: every slot counts down a tick, and what runs out takes effect.
    /// </summary>
    public void TimerSpecialCharacterUpdate()
    {
        int eventsLeft = 0;
        for (int i = 0; i < 4; i += 1)
        {
            var c = Characters[i];
            if ((c.Flags & 1) == 0) continue;
            for (int ii = 0; ii < 5; ii += 1)
            {
                if (c.CharacterUpdateEvents[ii] == 0) continue;
                if (--c.CharacterUpdateDelay[ii] > 0)
                {
                    if (c.CharacterUpdateDelay[ii] > eventsLeft) eventsLeft = c.CharacterUpdateDelay[ii];
                    continue;
                }
                switch (c.CharacterUpdateEvents[ii] - 1)
                {
                    case 0:   // a swing recovering
                        if (c.WeaponHit != 0)
                        {
                            c.WeaponHit = 0;
                            c.CharacterUpdateDelay[ii] = CalcCharacterSkillLevel(i, 6);
                            if (c.CharacterUpdateDelay[ii] > eventsLeft) eventsLeft = c.CharacterUpdateDelay[ii];
                        }
                        else c.Flags &= 0xfffb;
                        RedrawPortrait?.Invoke(i);
                        break;
                    case 1:
                        c.DamageSuffered = 0;
                        RedrawPortrait?.Invoke(i);
                        break;
                    case 2:
                        c.Flags &= 0xffbf;
                        RedrawPortrait?.Invoke(i);
                        break;
                    case 3:   // the poison bites again
                        eventsLeft = _rng.RollDice(1, 2);
                        if (InflictDamage(i, eventsLeft, 0x8000, 0, 0x80) != 0)
                        {
                            SayAboutCharacter?.Invoke(i, 2, 0x4022);
                            c.CharacterUpdateDelay[ii] = 10;
                            if (c.CharacterUpdateDelay[ii] > eventsLeft) eventsLeft = c.CharacterUpdateDelay[ii];
                        }
                        break;
                    case 4:
                        c.Flags &= 0xfeff;
                        SayAboutCharacter?.Invoke(i, 0, 0x4027);
                        RedrawPortrait?.Invoke(i);
                        break;
                    case 5:   // a temporary face frame runs out
                        ShowTemporaryFaceFrame?.Invoke(i, 0, 0, true);
                        break;
                    case 6:
                        c.Flags &= 0xefff;
                        RedrawPortrait?.Invoke(i);
                        break;
                    case 7:   // the swamp thaws
                        RestoreSwampPalette?.Invoke();
                        break;
                    case 8: case 9: case 10:   // a potion running out
                    {
                        int skill = c.CharacterUpdateEvents[ii] - 9;
                        c.SkillModifiers[skill] -= c.PotionSkillBonus[skill];
                        c.PotionSkillBonus[skill] = 0;
                        SayAboutCharacter?.Invoke(i, 0, -1);
                        RedrawPortrait?.Invoke(i);
                        break;
                    }
                }
                if (c.CharacterUpdateDelay[ii] <= 0) c.CharacterUpdateEvents[ii] = 0;
            }
        }
        if (eventsLeft != 0) EnableEffectTimer?.Invoke(); else DisableEffectTimer?.Invoke();
    }
}
