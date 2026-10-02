// The four set-pieces a level script can play: falling down a pit, a gas trap going off, drinking
// from the bezel cup, and the magic shroud coming back.
//
// Transliterated from src/game/spells.mjs (pitDropScroll, processGasExplosion, drinkBezelCup) and
// src/game/magic.mjs (timedPaletteFadeStep, restoreMagicShroud). Each runs on the engine's clock,
// so a host stepping the clock plays them at the pace the original does.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>timedPaletteFadeStep: one step of a fade between two palettes.</summary>
    public bool TimedPaletteFadeStep(byte[] pal1, byte[] pal2, double elapsed, double total)
    {
        var p1 = _screen.Palette(1);
        var output = new byte[768];
        bool more = false;
        for (int i = 0; i < 768; i += 1)
        {
            if (elapsed < total)
            {
                int d = (pal2[i] & 0x3f) - (pal1[i] & 0x3f);
                if (d != 0) more = true;
                int val = (int)(((d << 8) / total) * elapsed) >> 8;
                output[i] = (byte)(((pal1[i] & 0x3f) + (sbyte)val) & 0xff);
            }
            else
            {
                output[i] = p1[i] = (byte)(pal2[i] & 0x3f);
                more = false;
            }
        }
        _screen.SetScreenPalette(output);
        return more;
    }

    /// <summary>pitDropScroll: the view scrolls away beneath the party.</summary>
    public void PitDropScroll(int numSteps, Action<double> wait)
    {
        if (numSteps <= 0) return;
        _screen.CopyRegion(112, 0, 0, 0, 176, 120, 0, 6, true);
        for (int i = 0; i < numSteps; i += 1)
        {
            int ys = (30720 / numSteps * i) >> 8;
            _screen.CopyRegion(0, ys, 112, 0, 176, 120 - ys, 6, 0, true);
            _screen.CopyRegion(112, 0, 112, 120 - ys, 176, ys, 2, 0, true);
            wait?.Invoke(LevelLoader.TickLength);
        }
        _screen.CopyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
        wait?.Invoke(LevelLoader.TickLength);
        UpdateDrawPage2();
    }

    /// <summary>processGasExplosion: the trap, either as its animation or as a red flash.</summary>
    public void ProcessGasExplosion(int soundId, int distance, Action<double> wait)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 12);
        int[] sounds = { 0x62, 0xa7, 0xa7, 0xa8 };
        OnSoundEffect?.Invoke(sounds[soundId & 3]);
        if (distance == 0)
        {
            var p2 = _screen.Palette(3);
            Array.Copy(_screen.Palette(1), p2, 768);
            for (int i = 1; i < 128; i += 1) p2[i * 3] = 0x3f;
            double elapsed = 0;
            while (TimedPaletteFadeStep(_screen.Palette(0), p2, elapsed, 10)) { elapsed += 1; wait?.Invoke(LevelLoader.TickLength); }
            elapsed = 0;
            while (TimedPaletteFadeStep(p2, _screen.Palette(0), elapsed, 50)) { elapsed += 1; wait?.Invoke(LevelLoader.TickLength); }
        }
        _screen.CopyPage(12, 2);
        _screen.CurPage = cp;
        UpdateDrawPage2();
    }

    /// <summary>A sound the effects ask for; the loader passes it on to the host.</summary>
    public Action<int> OnSoundEffect;

    /// <summary>drinkBezelCup: the cup empties and the hero comes back up to full.</summary>
    public void DrinkBezelCup(int numUses, int charNum, Func<string, WsaPlayer> openWsa, Action<double> wait)
    {
        if (numUses < 0 || numUses > 3) return;
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        OnSoundEffect?.Invoke(73);
        var mov = openWsa?.Invoke("BEZEL.WSA");
        int x = ActiveCharsXpos[charNum] - 11;
        int y = 124;
        int w = mov?.Width ?? 0, h = mov?.Height ?? 0;
        if (w > 0) _screen.CopyRegion(x, y, 0, 0, w, h, 0, 2, true);
        int[] bezel = { 0, 26, 20, 27, 61, 55, 62, 92, 86, 93, 131, 125 };
        int frm = bezel[numUses * 3];
        var c = Characters[charNum];
        int hpDiff = c.HitPointsMax - c.HitPointsCur;
        int step = 0;
        do
        {
            step = (step & 0xff) + hpDiff * 256 / Math.Max(1, bezel[numUses * 3 + 1]);
            IncreaseCharacterHitpoints(charNum, step >> 8, true);
            DrawCharPortraitWithStats(charNum);
            if (w > 0)
            {
                _screen.CopyRegion(0, 0, x, y, w, h, 2, 2, true);
                mov.DisplayFrame(frm, 2, x, y, 0x5000);
                _screen.CopyRegion(x, y, x, y, w, h, 2, 0, true);
            }
            wait?.Invoke(4 * LevelLoader.TickLength);
        } while (++frm < bezel[numUses * 3 + 1]);
        c.HitPointsCur = c.HitPointsMax;
        if (w > 0) _screen.CopyRegion(0, 0, x, y, w, h, 2, 2, true);
        _loader.Board.RemoveCharacterEffects(c, 4, 4);
        DrawCharPortraitWithStats(charNum);
        if (w > 0) _screen.CopyRegion(x, y, x, y, w, h, 2, 0, true);
        _screen.CurPage = cp;
    }
}
