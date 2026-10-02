// Resting: the party sleeps where it stands until everyone is healed, or something wakes them.
//
// Transliterated from clickedRestParty in src/game/gui.mjs.
//
// The healing is on the clock rather than on a counter: each character gains a hit point every
// so many ticks, and how many depends on the largest maximum in the party, so a tough party heals
// at the same *rate on screen* as a weak one. Monsters keep taking their turns throughout, which is
// what makes a rest interruptible.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>Set when something wakes the party - a monster, or the player.</summary>
    public bool PartyAwake = true;

    /// <summary>How the host lets time pass inside the rest, and steps its timers.</summary>
    public Action<double> RestTick;

    /// <summary>clickedRestParty: the whole rest, from lying down to standing up.</summary>
    public int ClickedRestParty()
    {
        ToggleButtonDisplayMode(77, 1);
        if (WeaponsDisabled) CloseCharSheet();
        int tHp = -1, tMp = -1, tHa = -1;
        int needPoisoning = 0, needHealing = 0, needMagic = 0;
        for (int i = 0; i < 4; i += 1)
        {
            var c = Characters[i];
            if (!c.Active || (c.Flags & 8) != 0) continue;
            if (c.HitPointsMax > tHp) tHp = c.HitPointsMax;
            if (c.MagicPointsMax > tMp) tMp = c.MagicPointsMax;
            if ((c.Flags & 0x80) != 0)
            {
                needPoisoning |= 1 << i;
                if (c.HitPointsCur > tHa) tHa = c.HitPointsCur;
            }
            else if (c.HitPointsCur < c.HitPointsMax) needHealing |= 1 << i;
            if (c.MagicPointsCur < c.MagicPointsMax) needMagic |= 1 << i;
            c.Flags |= 0x1000;
        }

        if (needHealing == 0 && needMagic == 0)
        {
            // Nobody needs it: the party says so and stays on its feet.
            for (int i = 0; i < 4; i += 1) Characters[i].Flags &= unchecked((int)0xffffefff);
            if (needPoisoning != 0)
            {
                SetTemporaryFaceFrameForAll(0, 0, false);
                for (int i = 0; i < 4; i += 1) if ((needPoisoning & (1 << i)) != 0) SetTemporaryFaceFrame(i, 3, 8, false);
                _loader.Text?.PrintMessage(0x8000, LangString(0x405a));
                DrawAllCharPortraitsWithStats();
            }
            else
            {
                SetTemporaryFaceFrameForAll(2, 4, true);
                _loader.Text?.PrintMessage(0x8000, LangString(0x4058));
            }
            ToggleButtonDisplayMode(77, 0);
            return 1;
        }

        _screen.FillRect(112, 0, 288, 120, 1);
        DrawAllCharPortraitsWithStats();
        _loader.Text?.PrintMessage(0x8000, LangString(0x4057));
        ToggleButtonDisplayMode(77, 0);

        int h = Math.Min(30, 600 / Math.Max(1, tHp));
        int m = Math.Min(30, 600 / Math.Max(1, tMp));
        int a = tHa > 0 ? Math.Min(15, 600 / tHa) : 15;
        double tick = LevelLoader.TickLength;
        double now = _loader.Clock;
        double delay1 = now + h * tick, delay2 = now + m * tick, delay3 = now + a * tick;
        PartyAwake = false;

        // The monsters get their turns before the party settles, which is how a rest is refused.
        for (int i = 0; i < 32 && !PartyAwake; i += 1) RestTick?.Invoke(tick);
        _loader.ResetBlockProperties();

        int guard = 0;
        do
        {
            for (int i = 0; i < 8 && !PartyAwake; i += 1) RestTick?.Invoke(tick);
            if (!PartyAwake)
            {
                now = _loader.Clock;
                if (now > delay3)
                {
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((needPoisoning & (1 << i)) == 0) continue;
                        _loader.Board.InflictDamage(i, 1, 0x8000, 1, 0x80);
                        if ((Characters[i].Flags & 8) != 0) needPoisoning &= ~(1 << i);
                    }
                    delay3 = now + a * tick;
                }
                if (now > delay1)
                {
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((needHealing & (1 << i)) == 0) continue;
                        IncreaseCharacterHitpoints(i, 1);
                        DrawCharPortraitWithStats(i);
                        if (Characters[i].HitPointsCur == Characters[i].HitPointsMax) needHealing &= ~(1 << i);
                    }
                    delay1 = now + h * tick;
                }
                if (now > delay2)
                {
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((needMagic & (1 << i)) == 0) continue;
                        Characters[i].MagicPointsCur += 1;
                        DrawCharPortraitWithStats(i);
                        if (Characters[i].MagicPointsCur == Characters[i].MagicPointsMax) needMagic &= ~(1 << i);
                    }
                    delay2 = now + m * tick;
                }
            }
            RestTick?.Invoke(tick);
        } while (!PartyAwake && (needHealing != 0 || needMagic != 0) && ++guard < 20000);

        for (int i = 0; i < 4; i += 1)
        {
            int frm = 0, upd = 0;
            bool setFrame = true;
            if ((Characters[i].Flags & 0x1000) != 0)
            {
                Characters[i].Flags &= unchecked((int)0xffffefff);
                if (PartyAwake)
                {
                    frm = Characters[i].DamageSuffered != 0 ? 5 : 4;
                    upd = 6;
                }
            }
            else if (Characters[i].DamageSuffered != 0) setFrame = false;
            else frm = 4;
            if (setFrame) SetTemporaryFaceFrame(i, frm, upd, true);
        }
        PartyAwake = true;
        UpdateDrawPage2();
        DrawScene(0);
        _loader.Text?.PrintMessage(0x8000, LangString(0x4059));
        return 1;
    }

    /// <summary>increaseCharacterHitpoints: a point at a time, and never raising the dead.</summary>
    public void IncreaseCharacterHitpoints(int charNum, int points, bool ignoreDeath = false)
    {
        var c = Characters[charNum];
        if (c.HitPointsCur <= 0 && !ignoreDeath) return;
        if (points <= 1) points = 1;
        c.HitPointsCur = Math.Max(1, Math.Min(c.HitPointsMax, c.HitPointsCur + points));
        c.Flags &= unchecked((int)0xfffffff7);
    }

    public void SetTemporaryFaceFrame(int charNum, int frame, int updateDelay, bool redraw)
    {
        _ = updateDelay;
        Characters[charNum].TempFaceFrame = frame;
        if (redraw) DrawCharPortraitWithStats(charNum);
    }

    public void SetTemporaryFaceFrameForAll(int frame, int updateDelay, bool redraw)
    {
        for (int i = 0; i < 4; i += 1) SetTemporaryFaceFrame(i, frame, updateDelay, false);
        if (redraw) DrawAllCharPortraitsWithStats();
    }
}
