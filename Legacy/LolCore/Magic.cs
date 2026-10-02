// The attack spells: ice, fireball, hand of fate, mist of doom, lightning, fog, swarm, Vaelan's
// cube, the guardian, the viper a trap launches, and healing.
//
// Transliterated from src/game/magic.mjs (processMagicIce, processMagicFireball,
// processMagicHandOfFate, processMagicMistOfDoom, processMagicLightning, processMagicFog,
// processMagicSwarm, processMagicVaelansCube, processMagicGuardian, callbackProcessMagicSwarm,
// callbackProcessMagicLightning, launchMagicViper, generateFadeTable, restoreMagicShroud,
// breakIceWall) and the parts of src/game/spells.mjs the same spells run through
// (playSpellAnimation, processMagicSpark, processMagicHeal).
//
// Every one of these is an animation and an arithmetic together, and the arithmetic is not
// separable: ice damage depends on how far the animation got, a fireball's damage on which block it
// stopped in. They run on the engine's clock through Wait, so a host stepping the clock plays them
// at the pace the original does.
namespace LolCore;

public sealed partial class Gui
{
    private static readonly int[][] MistAnim =
    {
        new[] { 0, 7, 7, 13, 155 }, new[] { 0, 16, 16, 17, 155 }, new[] { 0, 24, 24, 24, 174 },
        new[] { 0, 19, 19, 19, 174 }, new[] { 0, 16, 16, 17, 175 },
    };

    private static readonly int[] FireballCoords = StaticData.Table("FireballCoords");
    private static readonly int[] LightningDefs = StaticData.Table("LightningDefs");
    private static readonly int[] HealShapeFrames = StaticData.Table("HealShapeFrames");

    /// <summary>ICE.SHP, FIREBALL.SHP, HEAL.SHP and HEALI.SHP: what the spells are drawn with.</summary>
    public Shape[] EffectShapes = Array.Empty<Shape>();
    public Shape[] FireballShapes = Array.Empty<Shape>();
    public Shape[] HealShapes = Array.Empty<Shape>();
    public Shape[] HealiShapes = Array.Empty<Shape>();

    /// <summary>How the spells spend engine time. The host sets it; without it they draw instantly.</summary>
    public Action<double> Wait;

    private byte[] _healOverlay;
    private int _swarmSpellStatus;
    private int _lightningCurSfx, _lightningDiv, _lightningFirstSfx, _lightningSfxFrame;

    private double Millis => _loader.Clock;

    private void Tick(double ms) => Wait?.Invoke(ms);

    private static int Int16(int v) => (short)v;

    private MonsterBoard Board => _loader.Board;

    private BlockMap Map => _loader.Map;

    private WallData WallSet => _loader.Walls;

    private int AheadBlock => Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction);

    /// <summary>
    /// playSpellAnimation: a WSA over the frozen view, optionally with a palette fade running
    /// alongside it and a callback that gets to draw before each frame.
    /// </summary>
    public void PlaySpellAnimation(WsaPlayer mov, int firstFrame, int lastFrame, int frameDelay, int x, int y,
                                   Action<WsaPlayer, int, int> callback, bool restoreScreen,
                                   byte[] pal1 = null, byte[] pal2 = null, int fadeDelay = 0)
    {
        int w = mov?.Width ?? 0;
        int h = mov?.Height ?? 0;
        if (x < 0) w += x;
        if (y < 0) h += y;
        double startTime = Millis;
        int dir = lastFrame >= firstFrame ? 1 : -1;
        int curFrame = firstFrame;
        // A spell with neither a movie nor a fade would spin here for ever, exactly as it would in
        // the original; the guard is on the frame count, which no spell in the game reaches.
        for (int guard = 0; guard < 20000; guard += 1)
        {
            double delayTimer = Millis + LevelLoader.TickLength * frameDelay;
            if (mov != null || callback != null) _screen.CopyPage(12, 2);
            callback?.Invoke(mov, x, y);
            if (mov != null) mov.DisplayFrame(curFrame % mov.NumFrames, 2, x, y, 0x5000);
            if (mov != null || callback != null) _screen.CopyRegion(x, y, x, y, w, h, 2, 0, true);
            if (pal1 != null && pal2 != null)
            {
                double del = Math.Max(0, delayTimer - Millis);
                do
                {
                    double step = Math.Min(del, LevelLoader.TickLength);
                    if (!TimedPaletteFadeStep(pal1, pal2, Millis - startTime, LevelLoader.TickLength * fadeDelay) && mov == null) return;
                    if (del > 0) { Tick(step); del -= step; } else Tick(0);
                } while (del > 0);
            }
            else _loader.AdvanceClockTo(delayTimer);
            if (mov == null) continue;
            curFrame += dir;
            if ((dir > 0 && curFrame >= lastFrame) || (dir < 0 && curFrame < lastFrame)) break;
        }
        if (restoreScreen && mov != null)
        {
            _screen.CopyPage(12, 2);
            _screen.CopyRegion(x, y, x, y, w, h, 2, 0, true);
        }
    }

    /// <summary>
    /// Screen_LoL::generateFadeTable: numTabs palettes stepping from src1 (the live palette when it
    /// is null) to src2.
    /// </summary>
    public byte[][] GenerateFadeTable(byte[] src1, byte[] src2, int numTabs)
    {
        var p2 = src1 ?? _screen.ScreenPalette;
        var output = new byte[numTabs][];
        var delta = new sbyte[768];
        for (int i = 0; i < 768; i += 1) delta[i] = (sbyte)(src2[i] - p2[i]);
        output[0] = (byte[])p2.Clone();
        int t = 0;
        int d = 256 / numTabs;
        for (int i = 1; i < numTabs - 1; i += 1)
        {
            t += d;
            var pal = new byte[768];
            for (int ii = 0; ii < 768; ii += 1) pal[ii] = (byte)((((delta[ii] * t) >> 8) + (sbyte)p2[ii]) & 0xff);
            output[i] = pal;
        }
        output[numTabs - 1] = (byte[])src2.Clone();
        for (int i = 0; i < numTabs; i += 1) output[i] ??= (byte[])src2.Clone();
        return output;
    }

    // ---- the spell entry points (spellProcs in spells.mjs) ----
    public int CastIce(int charNum, int level) { ProcessMagicIce(charNum, level); return 1; }

    public int CastFireball(int charNum, int level) { ProcessMagicFireball(charNum, level); return 1; }

    public int CastHandOfFate(int level) { ProcessMagicHandOfFate(level); return 1; }

    public int CastMistOfDoom(int charNum, int level) { ProcessMagicMistOfDoom(charNum, level); return 1; }

    public int CastLightning(int charNum, int level) { ProcessMagicLightning(charNum, level); return 1; }

    public int CastFog() { ProcessMagicFog(); return 1; }

    public int CastSwarm(int charNum) { ProcessMagicSwarm(charNum, 10); return 1; }

    public int CastVaelansCube() => ProcessMagicVaelansCube();

    public int CastGuardian(int charNum) => ProcessMagicGuardian(charNum);

    public int CastHeal(int charNum, int level)
    {
        if (level < 3) ProcessMagicHealSelectTarget();
        else ProcessMagicHeal(-1, level);
        return 1;
    }

    public int CastHealOnSingleCharacter(int target, int level) { ProcessMagicHeal(target, level); return 1; }

    /// <summary>processMagicIce: the view freezes, then what stands ahead takes the cold.</summary>
    public void ProcessMagicIce(int charNum, int spellLevel)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _loader.PauseSysTimers(true);
        DrawScene(0);
        _screen.CopyPage(0, 12);
        if (_loader.Level == 11 && (_loader.Flags[52] & 0x04) == 0)
        {
            var sc = _screen.Palette(0);
            var dc = _screen.Palette(2);
            for (int i = 1; i < 768; i += 1) (sc[i], dc[i]) = (dc[i], sc[i]);
            _loader.Flags[52] |= 0x04;
            int[] freezeTimes = { 20, 28, 40, 60 };
            Board.SetCharacterUpdateEvent(charNum, 8, freezeTimes[spellLevel & 3], true);
        }
        var s = (byte[])_screen.Palette(1).Clone();
        var swampCol = _loader.LoadPaletteFile("SWAMPICE.COL");
        var tpal = (byte[])s.Clone();
        Array.Copy(s, 128 * 3, swampCol, 128 * 3, 768 - 128 * 3);
        for (int i = 1; i < 128; i += 1)
        {
            tpal[i * 3] = 0;
            int v = (s[i * 3] + s[i * 3 + 1] + s[i * 3 + 2]) / 3;
            tpal[i * 3 + 1] = (byte)v;
            tpal[i * 3 + 2] = (byte)Math.Min(v << 1, 0x3f);
        }
        _loader.GenerateBrightnessPalette(tpal, tpal, _loader.Brightness, _loader.LampEffect);
        _loader.GenerateBrightnessPalette(swampCol, swampCol, _loader.Brightness, _loader.LampEffect);
        swampCol[0] = swampCol[1] = swampCol[2] = tpal[0] = tpal[1] = tpal[2] = 0;
        _loader.GenerateBrightnessPalette(_screen.Palette(0), s, _loader.Brightness, _loader.LampEffect);

        int sX = 112, sY = 0;
        WsaPlayer mov = null;
        if (spellLevel == 0) sX = 0;
        if (spellLevel == 1 || spellLevel == 2) mov = _loader.OpenWsa("SNOW.WSA", 1);
        if (spellLevel == 3) { mov = _loader.OpenWsa("ICE.WSA", 1); sX = 136; sY = 12; }
        OnSoundEffect?.Invoke(71);
        PlaySpellAnimation(null, 0, 0, 2, 0, 0, null, false, s, tpal, 40);
        TimedPaletteFadeStep(s, tpal, Millis, LevelLoader.TickLength);
        if (mov != null)
        {
            bool restore = true;
            if (spellLevel > 2)
            {
                Map.Flags[AheadBlock] |= 0x10;
                OnSoundEffect?.Invoke(165);
                restore = false;
            }
            PlaySpellAnimation(mov, 0, mov.NumFrames, 2, sX, sY, null, restore);
        }

        int[] snowDamage = { 10, 20, 30, 55 };
        int[] iceDamageMax = { 1, 2, 15, 20, 35 };
        int[] iceDamageMin = { 10, 10, 3, 4, 4 };
        int[] iceDamageAdd = { 5, 10, 30, 10, 10 };
        bool breakWall = false;
        int ahead = AheadBlock;
        if (spellLevel < 3) Board.InflictMagicalDamageForBlock(ahead, charNum, snowDamage[spellLevel], 3, _loader.Level);
        else
        {
            int o = Map.AssignedObjects[ahead];
            while ((o & 0x8000) != 0)
            {
                int might = _loader.Dice.RollDice(iceDamageMin[spellLevel], iceDamageMax[spellLevel]) + iceDamageAdd[spellLevel];
                int dmg = Board.CalcInflictableDamagePerItem(charNum, 0, might, 3, 2);
                var m = Board.Monsters[o & 0x7fff];
                if (m.HitPoints <= dmg)
                {
                    Board.IncreaseExperience(charNum, 2, m.HitPoints);
                    // Shattering is still a kill: the host counts kills, drops, errands and the pit's
                    // cull objective from this event, and it has to be raised while the monster is
                    // still on its block.
                    Board.OnMonsterSlain?.Invoke(m.Id, charNum);
                    o = m.NextAssignedObject;
                    if ((m.Flags & 0x20) != 0)
                    {
                        m.Mode = 0;
                        Board.MonsterDropItems(m);
                        if (_loader.Level != 29) Board.SetMonsterMode(m, 14);
                        _loader.RunLevelScript(0x404, -1, o, o);
                        if (m.Mode != 14) Board.Place(m, 0, 0);
                    }
                    else Board.KillMonster(m);
                }
                else
                {
                    breakWall = true;
                    Board.InflictDamage(o, dmg, charNum, 2, 3);
                    m.DamageReceived = 0;
                    o = m.NextAssignedObject;
                }
                if ((m.Flags & 0x20) != 0) break;
            }
        }
        UpdateDrawPage2();
        DrawScene(0);
        _loader.PauseSysTimers(false);
        if (_loader.Level != 11) _loader.GenerateBrightnessPalette(_screen.Palette(0), swampCol, _loader.Brightness, _loader.LampEffect);
        PlaySpellAnimation(null, 0, 0, 2, 0, 0, null, false, tpal, swampCol, 40);
        TimedPaletteFadeStep(tpal, swampCol, Millis, LevelLoader.TickLength);
        if (breakWall) BreakIceWall(tpal, swampCol);
        _screen.CurPage = cp;
    }

    /// <summary>processMagicFireball: how far the ball gets decides both the damage and the picture.</summary>
    public void ProcessMagicFireball(int charNum, int spellLevel)
    {
        int fbCnt = new[] { 4, 5, 6, 5 }[spellLevel];
        int d = spellLevel == 3 ? 0 : 1;
        int drawPage1 = 2, drawPage2 = 4;
        int bl = _loader.Party.Block;
        int fireballItem = Items.MakeItem(9, 0, 0, _loader.Level);
        int i = 0;
        for (; i < 3; i += 1)
        {
            _loader.RunLevelScript(bl, 0x200, -1, fireballItem);
            int o = Map.AssignedObjects[bl];
            if ((o & 0x8000) != 0 || (WallSet.WallFlags[Map.Walls[bl, _loader.Party.Direction ^ 2]] & 7) != 0)
            {
                while ((o & 0x8000) != 0)
                {
                    int[] fireballDamage = { 20, 40, 80, 100 };
                    int dmg = Board.CalcInflictableDamagePerItem(charNum, o, fireballDamage[spellLevel], 4, 1);
                    var m = Board.Monsters[o & 0x7fff];
                    o = m.NextAssignedObject;
                    Board.EnvSfxUseQueue = true;
                    Board.InflictDamage(m.Id | 0x8000, dmg, charNum, 2, 4);
                    Board.EnvSfxUseQueue = false;
                }
                break;
            }
            bl = Party.CalcNewBlockPosition(bl, _loader.Party.Direction);
        }
        d = Math.Min(d + i, 3);
        Items.Delete(fireballItem);
        OnSoundEffect?.Invoke(69);

        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 12);
        int fireBallWH = (d << 4) * -1;
        int numFireballs = fbCnt > 3 ? fbCnt - 3 : 1;
        var states = new Fireball[numFireballs];
        for (int k = 0; k < numFireballs; k += 1)
            states[k] = new Fireball { Active = true, DestX = 200, DestY = 60, TblIndex = ((k * 50) % 255) + 200, Progress = 1000, Step = 10 };
        _screen.CopyPage(12, drawPage1);
        int[] finShpIndex1 = { 5, 6, 7, 7, 6, 5 };
        int[] finShpIndex2 = { -1, 1, 2, 3, 4, -1 };

        for (i = 0; i < numFireballs;)
        {
            _screen.CurPage = drawPage1;
            double ctime = Millis;
            for (int ii = 0; ii < Math.Min(fbCnt, 3); ii += 1)
            {
                if (ii >= states.Length) continue;
                var fb = states[ii];
                if (fb == null || !fb.Active) continue;
                var shp = fb.Finalize ? Shape(FireballShapes, finShpIndex1[fb.FinProgress]) : Shape(FireballShapes, 0);
                var (fX, fY, sW, sH) = FireballPlace(fb, shp, fireBallWH);
                if (shp != null)
                {
                    _screen.DrawShape(_screen.CurPage, shp, fX, fY, 0, 0x1004, new ShapeDrawOptions { ScaleW = sW, ScaleH = sH });
                    if (fb.Finalize && finShpIndex2[fb.FinProgress] != -1)
                    {
                        shp = Shape(FireballShapes, finShpIndex2[fb.FinProgress]);
                        (fX, fY, sW, sH) = FireballPlace(fb, shp, fireBallWH);
                        if (shp != null) _screen.DrawShape(_screen.CurPage, shp, fX, fY, 0, 4, new ShapeDrawOptions { ScaleW = sW, ScaleH = sH });
                    }
                }
                if (fb.Finalize)
                {
                    if (++fb.FinProgress >= 6) { fb.Active = false; i += 1; }
                }
                else
                {
                    fb.Step = fb.Step < 40 ? fb.Step + 2 : 40;
                    if (fb.Progress < fb.Step)
                    {
                        if (ii < 1) { fb.Progress = fb.Step = fb.FinProgress = 0; fb.Finalize = true; }
                        else { fb.Active = false; i += 1; }
                        int[] fireballSfx = { 98, 167, 167, 168 };
                        OnSoundEffect?.Invoke(fireballSfx[d]);
                    }
                    else fb.Progress -= fb.Step;
                }
            }
            double del = LevelLoader.TickLength - (Millis - ctime);
            if (del > 0) Tick(del);
            _screen.CheckedPageUpdate(drawPage1, drawPage2);
            (drawPage1, drawPage2) = (drawPage2, drawPage1);
            _screen.CopyPage(12, drawPage1);
        }
        _screen.CurPage = cp;
        _screen.CopyPage(12, 0);
        UpdateDrawPage2();
        Board.PlayQueuedEffects();
        _loader.RunLevelScript(bl, 0x20, charNum, 3);
    }

    private sealed class Fireball
    {
        public bool Active;
        public int DestX, DestY, TblIndex, Progress, Step, FinProgress;
        public bool Finalize;
    }

    private static Shape Shape(Shape[] set, int index) => index >= 0 && index < set.Length ? set[index] : null;

    private static (int X, int Y, int W, int H) FireballPlace(Fireball fb, Shape shape, int fireBallWH)
    {
        if (shape == null) return (0, 0, 0x100, 0x100);
        int fX = ((fb.Progress * Int16(FireballCoords[fb.TblIndex & 0xff])) >> 16) + fb.DestX
                 - ((fb.Progress / 8 + shape.Width + fireBallWH) >> 1);
        int fY = ((fb.Progress * Int16(FireballCoords[(fb.TblIndex + 64) & 0xff])) >> 16) + fb.DestY
                 - ((fb.Progress / 8 + shape.Height + fireBallWH) >> 1);
        int sW = ((fb.Progress / 8 + shape.Width + fireBallWH) << 8) / Math.Max(1, shape.Width);
        int sH = ((fb.Progress / 8 + shape.Height + fireBallWH) << 8) / Math.Max(1, shape.Height);
        return (fX, fY, sW, sH);
    }

    /// <summary>processMagicHandOfFate: the low levels shove what is ahead back, the high ones crush it.</summary>
    public void ProcessMagicHandOfFate(int spellLevel)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 12);
        var mov = _loader.OpenWsa("HAND.WSA", 1);
        int[] frames = { 17, 26, 11, 16, 27, 35, 27, 35, 0, 75 };
        OnSoundEffect?.Invoke(173);
        PlaySpellAnimation(mov, 0, 10, 3, 112, 0, null, false);
        OnSoundEffect?.Invoke(151);
        PlaySpellAnimation(mov, frames[spellLevel * 2], frames[spellLevel * 2 + 1], 3, 112, 0, null, false);
        OnSoundEffect?.Invoke(18);
        PlaySpellAnimation(mov, 10, 0, 3, 112, 0, null, false);
        _screen.CurPage = cp;
        _screen.CopyPage(12, 2);
        DrawScene(2);
        if (spellLevel < 2)
        {
            int b1 = AheadBlock;
            int b2 = Party.CalcNewBlockPosition(b1, _loader.Party.Direction);
            if (!Board.TestWallFlag(b2, 0, 4) && (Map.AssignedObjects[b2] & 0x8000) == 0)
            {
                int dir = _loader.Party.Direction << 1;
                int o = Map.AssignedObjects[b1];
                while ((o & 0x8000) != 0)
                {
                    int o2 = o;
                    var m = Board.Monsters[o & 0x7fff];
                    o = m.NextAssignedObject;
                    var (nX, nY) = MonsterBoard.GetNextStepCoords(m.X, m.Y, dir);
                    for (int k = 0; k < 7; k += 1) (nX, nY) = MonsterBoard.GetNextStepCoords(nX, nY, dir);
                    Board.Place(m, nX, nY);
                    _loader.RunLevelScript(b2, 0x800, -1, o2);
                }
            }
        }
        else
        {
            int b1 = AheadBlock;
            int[] damage = { 75, 125, 175 };
            int o = Map.AssignedObjects[b1];
            while ((o & 0x8000) != 0)
            {
                int t = o;
                o = Board.Monsters[o & 0x7fff].NextAssignedObject;
                int dmg = Board.CalcInflictableDamagePerItem(-1, t, damage[spellLevel - 2], 0x80, 1);
                Board.InflictDamage(t, dmg, 0xffff, 3, 0x80);
            }
        }
        if (_loader.Level == 29) _screen.CopyPage(12, 2);
        _screen.CopyPage(2, 0);
        DrawScene(2);
        UpdateDrawPage2();
    }

    /// <summary>processMagicMistOfDoom: the damage lands first, then the mist rolls over the view.</summary>
    public void ProcessMagicMistOfDoom(int charNum, int spellLevel)
    {
        int[] mistDamage = { 30, 70, 110, 200 };
        Board.EnvSfxUseQueue = true;
        Board.InflictMagicalDamageForBlock(AheadBlock, charNum, mistDamage[spellLevel], 0x80, _loader.Level);
        Board.EnvSfxUseQueue = false;
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 2);
        DrawScene(2);
        _screen.CopyPage(2, 12);
        OnSoundEffect?.Invoke(155);
        var mov = _loader.OpenWsa($"MISTS{spellLevel + 1}.WSA", 1);
        var md = MistAnim[spellLevel];
        OnSoundEffect?.Invoke(md[4]);
        PlaySpellAnimation(mov, md[0], md[1], 7, 112, 0, null, false);
        PlaySpellAnimation(mov, md[2], md[3], 14, 112, 0, null, false);
        _screen.CurPage = cp;
        _screen.CopyPage(12, 0);
        UpdateDrawPage2();
        Board.PlayQueuedEffects();
    }

    /// <summary>processMagicLightning: four passes of the bolt, the palette flashing with it.</summary>
    public void ProcessMagicLightning(int charNum, int spellLevel)
    {
        _screen.CopyPage(0, 2);
        DrawScene(2);
        _screen.CopyPage(2, 12);
        _lightningCurSfx = LightningDefs[(spellLevel << 2) + 2] | (LightningDefs[(spellLevel << 2) + 3] << 8);
        _lightningDiv = LightningDefs[(spellLevel << 2) + 1];
        _lightningFirstSfx = 0;
        _lightningSfxFrame = 0;
        var mov = _loader.OpenWsa($"LITNING{spellLevel + 1}.WSA", 1);
        for (int i = 0; i < 4; i += 1)
            PlaySpellAnimation(mov, 0, LightningDefs[spellLevel << 2], 3, 93, 0, (_, _, _) => CallbackProcessMagicLightning(), false);
        _screen.SetScreenPalette(_screen.Palette(1));
        _screen.CopyPage(12, 2);
        _screen.CopyPage(12, 0);
        UpdateDrawPage2();
        int[] lightningDamage = { 18, 35, 50, 72 };
        Board.InflictMagicalDamageForBlock(AheadBlock, charNum, lightningDamage[spellLevel], 5, _loader.Level);
        DrawScene(0);
    }

    private void CallbackProcessMagicLightning()
    {
        if (_lightningDiv == 2) ShakeScene(1, 2, 3, false, Wait);
        var p1 = _screen.Palette(1);
        if (_lightningSfxFrame % Math.Max(1, _lightningDiv) != 0) _screen.SetScreenPalette(p1);
        else
        {
            var tpal = (byte[])p1.Clone();
            for (int i = 6; i < 384; i += 1)
            {
                int v = tpal[i] * 120 / 64;
                tpal[i] = (byte)(v < 64 ? v : 63);
            }
            _screen.SetScreenPalette(tpal);
        }
        if (_lightningDiv == 2)
        {
            if (_lightningFirstSfx == 0)
            {
                OnSoundEffect?.Invoke(_lightningCurSfx);
                _lightningFirstSfx = 1;
            }
        }
        else if ((_lightningSfxFrame & 7) == 0) OnSoundEffect?.Invoke(_lightningCurSfx);
        _lightningSfxFrame += 1;
    }

    /// <summary>processMagicFog: the whole movie plays, then everything ahead takes a flat 15.</summary>
    public void ProcessMagicFog()
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 12);
        var mov = _loader.OpenWsa("FOG.WSA", 0);
        OnSoundEffect?.Invoke(145);
        if (mov != null)
        {
            int numFrames = mov.NumFrames;
            for (int curFrame = 0; curFrame < numFrames; curFrame += 1)
            {
                double delayTimer = Millis + 3 * LevelLoader.TickLength;
                _screen.CopyPage(12, 2);
                mov.DisplayFrame(curFrame % numFrames, 2, 112, 0, 0x5000);
                _screen.CopyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
                _loader.AdvanceClockTo(delayTimer);
            }
        }
        _screen.CopyPage(12, 2);
        _screen.CurPage = cp;
        UpdateDrawPage2();
        int o = Map.AssignedObjects[AheadBlock];
        while ((o & 0x8000) != 0)
        {
            Board.InflictMagicalDamage(o, -1, 15, 6, 0);
            o = Board.Monsters[o & 0x7fff].NextAssignedObject;
        }
        DrawScene(0);
    }

    /// <summary>
    /// processMagicSwarm: the monsters ahead are frozen into their stung pose for one drawn frame,
    /// which is what the swarm flickers between, and then put back exactly as they were.
    /// </summary>
    public void ProcessMagicSwarm(int charNum, int damage)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 12);
        OnSoundEffect?.Invoke(74);
        var destIds = new List<int>();
        int o = Map.AssignedObjects[AheadBlock];
        while ((o & 0x8000) != 0)
        {
            o &= 0x7fff;
            if (Board.Monsters[o].Mode != 13)
            {
                destIds.Add(o);
                if ((Board.Monsters[o].Flags & 0x2000) == 0)
                {
                    Board.EnvSfxUseQueue = true;
                    Board.InflictMagicalDamage(o | 0x8000, charNum, damage, 0, 0);
                    Board.EnvSfxUseQueue = false;
                    Board.Monsters[o].Flags &= 0xffef;
                }
            }
            o = Board.Monsters[o].NextAssignedObject;
        }
        var destModes = new List<int>();
        var destTicks = new List<int>();
        foreach (int id in destIds)
        {
            destModes.Add(Board.Monsters[id].Mode);
            destTicks.Add(Board.Monsters[id].FightCurTick);
            Board.Monsters[id].Mode = 8;
            Board.Monsters[id].FightCurTick = 0;
        }
        DrawScene(_screen.CurPage);
        _screen.CopyRegion(112, 0, 112, 0, 176, 120, _screen.CurPage, 7);
        for (int k = 0; k < destIds.Count; k += 1)
        {
            Board.Monsters[destIds[k]].Mode = destModes[k];
            Board.Monsters[destIds[k]].FightCurTick = destTicks[k];
        }
        var mov = _loader.OpenWsa("SWARM.WSA", 0);
        _swarmSpellStatus = 0;
        PlaySpellAnimation(mov, 0, 37, 2, 0, 0, null, false);
        PlaySpellAnimation(mov, 38, 41, 8, 0, 0, (_, _, _) => CallbackProcessMagicSwarm(), false);
        _screen.CopyPage(12, 0);
        UpdateDrawPage2();
        Board.PlayQueuedEffects();
        _screen.CurPage = cp;
    }

    private void CallbackProcessMagicSwarm()
    {
        if (_swarmSpellStatus != 0) _screen.CopyRegion(112, 0, 112, 0, 176, 120, 6, _screen.CurPage);
        _swarmSpellStatus ^= 1;
    }

    /// <summary>
    /// processMagicVaelansCube: the screen goes violet, and what that light falls on - a false wall,
    /// an undead thing - gives way. Returns 1 when it found something, which the script checks.
    /// </summary>
    public int ProcessMagicVaelansCube()
    {
        var sp1 = _screen.Palette(1);
        var tmpPal1 = (byte[])sp1.Clone();
        var tmpPal2 = (byte[])sp1.Clone();
        for (int i = 0; i < 128; i += 1)
        {
            tmpPal2[i * 3] = (byte)Math.Min(sp1[i * 3] + 16, 60);
            tmpPal2[i * 3 + 1] = sp1[i * 3 + 1];
            tmpPal2[i * 3 + 2] = (byte)Math.Min(sp1[i * 3 + 2] + 19, 60);
        }
        OnSoundEffect?.Invoke(146);
        double total = 70 * LevelLoader.TickLength;
        double ctime = Millis;
        while (Millis < ctime + total)
        {
            TimedPaletteFadeStep(tmpPal1, tmpPal2, Millis - ctime, total);
            Tick(LevelLoader.TickLength);
        }
        int bl = AheadBlock;
        int s = Map.Walls[bl, _loader.Party.Direction ^ 2];
        int flg = WallSet.WallFlags[s];
        int res = s == 47 && (_loader.Level == 17 || _loader.Level == 24) ? 1 : 0;
        if ((WallSet.VmpMap[s] == 1 || WallSet.VmpMap[s] == 2) && (flg & 1) == 0 && _loader.Level != 22)
        {
            for (int side = 0; side < 4; side += 1) Map.Walls[bl, side] = 0;
            DrawScene(0);
            res = 1;
        }
        int o = Map.AssignedObjects[bl];
        while ((o & 0x8000) != 0)
        {
            var m = Board.Monsters[o & 0x7fff];
            if (m.Properties != null && (m.Properties.Flags & 0x1000) != 0)
            {
                Board.InflictDamage(o, 100, 0xffff, 0, 0x80);
                res = 1;
            }
            o = m.NextAssignedObject;
        }
        ctime = Millis;
        while (Millis < ctime + total)
        {
            TimedPaletteFadeStep(tmpPal2, tmpPal1, Millis - ctime, total);
            Tick(LevelLoader.TickLength);
        }
        return res;
    }

    /// <summary>processMagicGuardian: 200 damage to the block ahead, between the two halves of the movie.</summary>
    public int ProcessMagicGuardian(int charNum)
    {
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        _screen.CopyPage(0, 2);
        _screen.CopyPage(2, 12);
        var mov = _loader.OpenWsa("GUARDIAN.WSA", 0);
        OnSoundEffect?.Invoke(156);
        PlaySpellAnimation(mov, 0, 37, 2, 112, 0, null, false);
        _screen.CopyPage(2, 12);
        int bl = AheadBlock;
        int res = (Map.AssignedObjects[bl] & 0x8000) != 0 ? 1 : 0;
        Board.InflictMagicalDamageForBlock(bl, charNum, 200, 0x80, _loader.Level);
        _screen.CopyPage(12, 2);
        UpdateDrawPage2();
        DrawScene(2);
        _screen.CopyPage(2, 12);
        OnSoundEffect?.Invoke(176);
        PlaySpellAnimation(mov, 38, 48, 8, 112, 0, null, false);
        _screen.CurPage = cp;
        DrawPlayField();
        UpdateDrawPage2();
        return res;
    }

    /// <summary>launchMagicViper: the trap's viper strikes one hero, whoever the dice pick.</summary>
    public void LaunchMagicViper()
    {
        PartyAwake = true;
        int d = 0;
        for (int b = _loader.Party.Block; d < 3; d += 1)
        {
            if ((Map.AssignedObjects[b] & 0x8000) != 0) break;
            b = Party.CalcNewBlockPosition(b, _loader.Party.Direction);
            if ((WallSet.WallFlags[Map.Walls[b, _loader.Party.Direction ^ 2]] & 7) != 0) break;
        }
        _screen.CopyPage(0, 12);
        OnSoundEffect?.Invoke(148);
        var mov = _loader.OpenWsa("VIPER.WSA", 1);
        if (mov != null)
        {
            int numFrames = mov.NumFrames;
            int[] viperAnimData = { 15, 25, 20, 10, 25, 20, 5, 25, 20, 0, 25, 20 };
            int v0 = viperAnimData[d * 3], v1 = viperAnimData[d * 3 + 1], v2 = viperAnimData[d * 3 + 2];
            int frm = v0;
            while (true)
            {
                double etime = Millis + 5 * LevelLoader.TickLength;
                _screen.CopyPage(12, 2);
                if (frm == v2) OnSoundEffect?.Invoke(172);
                mov.DisplayFrame(frm++ % numFrames, 2, 112, 0, 0x5000);
                _screen.CopyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
                _loader.AdvanceClockTo(etime);
                if (frm > v1) break;
            }
        }
        _screen.CopyPage(12, 0);
        _screen.CopyPage(12, 2);
        int t = _loader.Dice.RollDice(1, 4);
        for (int i = 0; i < 4; i += 1)
        {
            if (!Characters[i].Active) { t %= 4; continue; }
            Board.InflictDamage(t, _loader.Level + 10, 0x8000, 2, 0x86);
        }
    }

    /// <summary>breakIceWall: the ice the spell put up shatters.</summary>
    public void BreakIceWall(byte[] pal1, byte[] pal2)
    {
        int bl = AheadBlock;
        Map.Flags[bl] &= 0xef;
        _screen.CopyPage(0, 2);
        DrawScene(2);
        _screen.CopyPage(2, 10);
        var mov = _loader.OpenWsa("SHATTER.WSA", 1);
        OnSoundEffect?.Invoke(166);
        PlaySpellAnimation(mov, 0, mov?.NumFrames ?? 0, 1, 58, 0, null, true, pal1, pal2, 20);
        _screen.CopyPage(10, 0);
        UpdateDrawPage2();
        DrawScene(0);
    }

    /// <summary>olol_restoreMagicShroud: the DARKLITE cutscene that lifts the shroud.</summary>
    public void RestoreMagicShroud()
    {
        var mov = _loader.OpenWsa("DARKLITE.WSA", 2);
        if (mov == null) return;
        var pal1 = _loader.LoadPaletteFile("LITEPAL1.COL");
        var tab1 = GenerateFadeTable(null, pal1, 21);
        var pal2 = _loader.LoadPaletteFile("LITEPAL2.COL");
        var pal3 = _loader.LoadPaletteFile("LITEPAL3.COL");
        var tab2 = GenerateFadeTable(pal2, pal3, 4);
        int[] sparkFrames = { 2, 5, 8, 11, 13, 15, 17, 19 };
        for (int i = 0; i < 21; i += 1)
        {
            double etime = Millis + 20 * LevelLoader.TickLength;
            mov.DisplayFrame(i, 0, 0, 0);
            _screen.SetScreenPalette(tab1[i]);
            if (Array.IndexOf(sparkFrames, i) >= 0) OnSoundEffect?.Invoke(95);
            _loader.AdvanceClockTo(etime);
        }
        OnSoundEffect?.Invoke(91);
        _screen.FadePalette(pal2, 300, Wait);
        int k = 0;
        for (int i = 22; i < 38; i += 1)
        {
            double etime = Millis + 12 * LevelLoader.TickLength;
            mov.DisplayFrame(i, 0, 0, 0);
            if (i == 22 || i == 24 || i == 28 || i == 32)
            {
                OnSoundEffect?.Invoke(131);
                if (k < tab2.Length) _screen.SetScreenPalette(tab2[k++]);
            }
            _loader.AdvanceClockTo(etime);
        }
    }

    /// <summary>
    /// restoreSwampPalette: the swamp thaws. The frozen palette that processMagicIce parked in
    /// palette 2 swaps back, and the screen fades from ice to normal.
    /// </summary>
    public void RestoreSwampPalette()
    {
        _loader.Flags[52] &= 0xfb;
        if (_loader.Level != 11) return;
        var s = _screen.Palette(2);
        var d = _screen.Palette(0);
        var d2 = _screen.Palette(1);
        for (int i = 1; i < 768; i += 1) (s[i], d[i]) = (d[i], s[i]);
        _loader.GenerateBrightnessPalette(d, d2, _loader.Brightness, _loader.LampEffect);
        _screen.LoadSpecialColors(s);
        _screen.LoadSpecialColors(d2);
        PlaySpellAnimation(null, 0, 0, 2, 0, 0, null, false, (byte[])s.Clone(), d2, 40);
    }

    /// <summary>processMagicSpark, with the sparkles the bolt scatters where it lands.</summary>
    public void ProcessMagicSparkFull(int charNum, int spellLevel)
    {
        _screen.CopyPage(0, 12);
        var mov = _loader.OpenWsa("SPARK1.WSA", 0);
        OnSoundEffect?.Invoke(72);
        PlaySpellAnimation(mov, 0, 7, 4, ActiveCharsXpos[charNum] - 2, 138, null, false);
        _screen.CopyPage(12, 0);
        var (dist, targetBlock) = Board.GetSpellTargetBlock(_loader.Party.Block, _loader.Party.Direction, 4);
        int target = Board.GetNearestMonsterFromCharacterForBlock(targetBlock, charNum);
        int[] dmg = { 7, 15, 25, 60 };
        if (target != 0xffff)
        {
            Board.InflictMagicalDamage(target, charNum, dmg[spellLevel & 3], 5, 0);
            UpdateDrawPage2();
            DrawScene(0);
            _screen.CopyPage(0, 12);
        }
        mov = _loader.OpenWsa("SPARK2.WSA", 0);
        if (mov == null) { UpdateDrawPage2(); return; }
        int numFrames = mov.NumFrames;
        var wX = new int[6];
        var wY = new int[6];
        var wFrames = new int[6];
        for (int i = 0; i < 6; i += 1)
        {
            wX[i] = (PresentationRandom(0x7fff) % 64) + ((176 - mov.Width) >> 1) + 80;
            wY[i] = (PresentationRandom(0x7fff) % 32) + ((120 - mov.Height) >> 1) - 16;
            wFrames[i] = i << 1;
        }
        for (int i = 0, d = (spellLevel << 1) + 12; i < d; i += 1)
        {
            double delayTimer = Millis + 4 * LevelLoader.TickLength;
            _screen.CopyPage(12, 2);
            for (int ii = 0; ii <= spellLevel && ii < 6; ii += 1)
            {
                if (wFrames[ii] >= i || wFrames[ii] + 13 <= i) continue;
                if (i - wFrames[ii] == 1) OnSoundEffect?.Invoke(162);
                mov.DisplayFrame((i - wFrames[ii] + (dist << 4)) % numFrames, 2, wX[ii], wY[ii], 0x5000);
                _screen.CopyRegion(wX[ii], wY[ii], wX[ii], wY[ii], mov.Width, mov.Height, 2, 0, true);
            }
            if (i < d - 1) _loader.AdvanceClockTo(delayTimer);
        }
        _screen.CopyPage(12, 2);
        UpdateDrawPage2();
    }

    /// <summary>processMagicHealSelectTarget: the prompt that asks which hero the heal is for.</summary>
    public void ProcessMagicHealSelectTarget()
    {
        _loader.Text?.PrintMessage(0, LangString(0x4040));
        AwaitingSpellTarget = true;
        OnSpellTargetWanted?.Invoke();
    }

    /// <summary>Set while the game waits for the player to pick which hero a heal is cast on.</summary>
    public bool AwaitingSpellTarget;

    /// <summary>
    /// activeSpell: what is being cast and by whom, kept while a spell asks the player something.
    /// The heal prompt needs it to finish the cast on the hero that gets picked.
    /// </summary>
    public int ActiveSpellChar, ActiveSpellNumber, ActiveSpellLevel;
    public Action OnSpellTargetWanted;

    /// <summary>
    /// processMagicHeal: the hit points come back a sixteenth at a time, with the glow drawn over
    /// each portrait as they do, so the bar filling and the light are the same sixteen frames.
    /// </summary>
    public void ProcessMagicHeal(int charNum, int spellLevel)
    {
        if (_healOverlay == null)
        {
            _healOverlay = new byte[256];
            Screen.GenerateGrayOverlay(_screen.Palette(1), _healOverlay, 52, 22, 20, 0, 256, true);
        }
        int[] healShpFrames, healiShpFrames;
        bool curePoison = false;
        int points;
        if (spellLevel == 0) { points = 25; healShpFrames = Slice(HealShapeFrames, 0, 16); healiShpFrames = Slice(HealShapeFrames, 32, 48); }
        else if (spellLevel == 1) { points = 45; healShpFrames = Slice(HealShapeFrames, 16, 32); healiShpFrames = Slice(HealShapeFrames, 48, 64); }
        else if (spellLevel > 3) { curePoison = true; points = spellLevel; healShpFrames = Slice(HealShapeFrames, 16, 32); healiShpFrames = Slice(HealShapeFrames, 64, 80); }
        else { curePoison = true; points = 10000; healShpFrames = Slice(HealShapeFrames, 16, 32); healiShpFrames = Slice(HealShapeFrames, 64, 80); }

        int ch = 0, n = 4;
        if (charNum != -1) { ch = charNum; n = charNum + 1; }
        var pX = new int[4];
        const int pY = 138;
        var diff = new int[4];
        var pts = new int[4];
        for (int c = ch; c < n; c += 1)
        {
            if (!Characters[c].Active) continue;
            pX[c] = ActiveCharsXpos[c] - 6;
            Characters[c].DamageSuffered = 0;
            int dmg = Characters[c].HitPointsMax - Characters[c].HitPointsCur;
            diff[c] = dmg < points ? dmg : points;
            _screen.CopyRegion(pX[c], pY, c * 77, 32, 77, 44, 0, 2, true);
        }
        int cp = _screen.CurPage;
        _screen.CurPage = 2;
        OnSoundEffect?.Invoke(68);
        for (int i = 0; i < 16; i += 1)
        {
            double delayTimer = Millis + 4 * LevelLoader.TickLength;
            for (int c = ch; c < n; c += 1)
            {
                if (!Characters[c].Active) continue;
                _screen.CopyRegion(c * 77, 32, pX[c], pY, 77, 44, 2, 2, true);
                pts[c] &= 0xff;
                pts[c] += (diff[c] << 8) / 16;
                IncreaseCharacterHitpoints(c, pts[c] >> 8, true);
                DrawCharPortraitWithStats(c);
                var healShape = Shape(HealShapes, healShpFrames[i]);
                if (healShape != null) _screen.DrawShape(2, healShape, pX[c], pY, 0, 0x1000);
                _screen.FillRect(0, 0, 31, 31, 0);
                var healiShape = Shape(HealiShapes, healiShpFrames[i]);
                if (healiShape != null) _screen.DrawShape(_screen.CurPage, healiShape, 0, 0, 0, 0);
                _screen.ApplyOverlaySpecial(_screen.CurPage, 0, 0, 2, pX[c] + 7, pY + 6, 32, 32, 0, 0, _healOverlay);
                _screen.CopyRegion(pX[c], pY, pX[c], pY, 77, 44, 2, 0, true);
            }
            _loader.AdvanceClockTo(delayTimer);
        }
        for (int c = ch; c < n; c += 1)
        {
            if (!Characters[c].Active) continue;
            _screen.CopyRegion(c * 77, 32, pX[c], pY, 77, 44, 2, 2, true);
            if (curePoison) Board.RemoveCharacterEffects(Characters[c], 4, 4);
            DrawCharPortraitWithStats(c);
            _screen.CopyRegion(pX[c], pY, pX[c], pY, 77, 44, 2, 0, true);
        }
        _screen.CurPage = cp;
        UpdateDrawPage2();
    }

    private static int[] Slice(int[] source, int from, int to)
    {
        var output = new int[to - from];
        for (int i = 0; i < output.Length; i += 1) output[i] = from + i < source.Length ? source[from + i] : 0;
        return output;
    }
}
