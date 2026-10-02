// Smooth scrolling: the view slides and turns between blocks instead of jumping.
//
// Transliterated from src/game/screen.mjs (backupSceneWindow, restoreSceneWindow, _zoomStep,
// smoothScrollZoomStepTop, smoothScrollZoomStepBottom, smoothScrollHorizontalStep,
// smoothScrollTurnStep, clearGuiShapeMemory, copyGuiShapeFromSceneBackupBuffer,
// copyGuiShapeToSurface) and src/game/scene.mjs (movePartySmoothScrollBlocked, ...Up, ...Down,
// ...Left, ...Right, ...Turn, smoothScrollDrawSpecialGuiShape).
//
// It is all pointer arithmetic over the 64,000-byte pages, and the addresses are the original's:
// 0xa500 is the scene backup buffer inside a page, 0xc4a0 the lower half of it, 0x79b0 and 0xe7c3
// the rows the special GUI shape is stitched through. They are offsets into the page, not
// coordinates, which is why they are written as they are.
namespace LolCore;

public sealed partial class Screen
{
    /// <summary>backupSceneWindow: the 176x120 view put aside inside a page.</summary>
    public void BackupSceneWindow(int srcPage, int dstPage)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        for (int h = 0; h < 120; h += 1) Array.Copy(src, h * Width + 112, dst, 0xa500 + h * 176, 176);
    }

    /// <summary>restoreSceneWindow: and put back where it came from.</summary>
    public void RestoreSceneWindow(int srcPage, int dstPage)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        for (int h = 0; h < 120; h += 1) Array.Copy(src, 0xa500 + h * 176, dst, h * Width + 112, 176);
    }

    /// <summary>
    /// The one step of the zoom both the top and the bottom halves are drawn with: the view is
    /// stretched out from a rectangle inside it, a run of destination bytes per source byte.
    /// </summary>
    private void ZoomStep(int srcPage, int dstPage, int srcStart, int dstStart, int x, int y, int baseHeight)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        int s = srcStart;
        int d = dstStart;
        x <<= 1;
        int width = 176 - x;
        if (width <= 0) return;
        int scaleX = ((((x + 1) << 8) / width) + 0x100) & 0xffff;
        int cntW = scaleX >> 8;
        scaleX = (scaleX << 8) & 0xffff;
        width -= 1;
        int widthCnt = width;
        int height = baseHeight - y;
        if (height <= 0) return;
        int scaleY = ((((y + 1) << 8) / height) + 0x100) & 0xffff;
        scaleY = (scaleY << 8) & 0xffff;
        uint scaleYc = 0;
        while (height != 0)
        {
            uint scaleXc = 0;
            do
            {
                scaleXc += (uint)scaleX;
                int numbytes = cntW + (int)(scaleXc >> 16);
                scaleXc &= 0xffff;
                if (s < 0 || s >= src.Length || d < 0 || d + numbytes > dst.Length) return;
                byte v = src[s++];
                for (int i = 0; i < numbytes; i += 1) dst[d + i] = v;
                d += numbytes;
            } while (--widthCnt != 0);
            if (s < 0 || s >= src.Length || d < 0 || d >= dst.Length) return;
            dst[d++] = src[s++];
            widthCnt = width;
            s += x;
            scaleYc += (uint)scaleY;
            if ((scaleYc >> 16) != 0)
            {
                scaleYc = 0;
                s -= 176;
                continue;
            }
            height -= 1;
        }
    }

    public void SmoothScrollZoomStepTop(int srcPage, int dstPage, int x, int y) =>
        ZoomStep(srcPage, dstPage, 0xa500 + y * 176 + x, 0xa500, x, y, 46);

    public void SmoothScrollZoomStepBottom(int srcPage, int dstPage, int x, int y) =>
        ZoomStep(srcPage, dstPage, 0xc4a0 + x, 0xc4a0, x, y, 74);

    /// <summary>smoothScrollHorizontalStep: a strip of the view slid sideways within one page.</summary>
    public void SmoothScrollHorizontalStep(int pageNum, int srcX, int dstX, int w)
    {
        var pg = Page(pageNum);
        int d = 0;
        int s = 112 + srcX;
        int w2 = srcX + w - dstX;
        int pitchS = 320 + w2 - (w << 1);
        int pitchD = 320 - w;
        for (int h = 0; h < 120; h += 1)
        {
            for (int i = 0; i < w; i += 1) pg[d++] = pg[s++];
            d -= w;
            s -= w2;
            for (int i = 0; i < w; i += 1) pg[s++] = pg[d++];
            s += pitchS;
            d += pitchD;
        }
    }

    /// <summary>
    /// smoothScrollTurnStep: the three steps a turn is made of, each one taking two views and
    /// squeezing them into one at a different ratio.
    /// </summary>
    public void SmoothScrollTurnStep(int step, int srcPage1, int srcPage2, int dstPage)
    {
        var p1 = Page(srcPage1);
        var p2 = Page(srcPage2);
        var dst = Page(dstPage);
        int s, d;
        if (step == 1)
        {
            s = 273; d = 0xa500;
            for (int i = 0; i < 120; i += 1)
            {
                byte a = p1[s++];
                dst[d++] = a; dst[d++] = a;
                for (int ii = 0; ii < 14; ii += 1) { a = p1[s++]; dst[d++] = a; dst[d++] = a; dst[d++] = a; }
                s += 305; d += 132;
            }
            s = 112; d = 0xa52c;
            for (int i = 0; i < 120; i += 1)
            {
                for (int ii = 0; ii < 33; ii += 1)
                {
                    dst[d++] = p2[s++]; dst[d++] = p2[s++];
                    byte a = p2[s++];
                    dst[d++] = a; dst[d++] = a;
                }
                s += 221; d += 44;
            }
        }
        else if (step == 2)
        {
            s = 244; d = 0xa500;
            var src = p1;
            for (int k = 0; k < 2; k += 1)
            {
                for (int i = 0; i < 120; i += 1)
                {
                    for (int ii = 0; ii < 44; ii += 1) { byte a = src[s++]; dst[d++] = a; dst[d++] = a; }
                    s += 276; d += 88;
                }
                src = p2; s = 112; d = 0xa558;
            }
        }
        else
        {
            s = 189; d = 0xa500;
            for (int i = 0; i < 120; i += 1)
            {
                for (int ii = 0; ii < 33; ii += 1)
                {
                    dst[d++] = p1[s++]; dst[d++] = p1[s++];
                    byte a = p1[s++];
                    dst[d++] = a; dst[d++] = a;
                }
                s += 221; d += 44;
            }
            s = 112; d = 0xa584;
            for (int i = 0; i < 120; i += 1)
            {
                for (int ii = 0; ii < 14; ii += 1) { byte a = p2[s++]; dst[d++] = a; dst[d++] = a; dst[d++] = a; }
                byte b = p2[s++];
                dst[d++] = b; dst[d++] = b;
                s += 305; d += 132;
            }
        }
    }

    public void ClearGuiShapeMemory(int pageNum)
    {
        var dst = Page(pageNum);
        for (int i = 0; i < 23; i += 1) Array.Fill(dst, (byte)0, 0x79b0 + i * 320, 176);
    }

    public void CopyGuiShapeFromSceneBackupBuffer(int srcPage, int dstPage)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        int s = 0x79c3;
        int d = 0;
        for (int i = 0; i < 23; i += 1)
        {
            int len = 0;
            byte v;
            do { v = src[s++]; len += 1; } while (v == 0);
            dst[d++] = (byte)len;
            len = 69 - len;
            Array.Copy(src, s, dst, d, len);
            s += len + 251;
            d += len;
        }
    }

    public void CopyGuiShapeToSurface(int srcPage, int dstPage)
    {
        var src = Page(srcPage);
        var dst = Page(dstPage);
        int s = 0;
        int d = 0xe7c3;
        for (int i = 0; i < 23; i += 1)
        {
            int v = src[s++];
            int len = 69 - v;
            d += v;
            Array.Copy(src, s, dst, d, len);
            s += len - 1;
            d += len;
            for (int ii = 0; ii < len; ii += 1) dst[d++] = src[s--];
            s += len + 1;
            d += v + 38;
        }
    }
}

public sealed partial class Gui
{
    /// <summary>The two pages the view alternates between (sceneDrawPage1/sceneDrawPage2).</summary>
    public int SceneDrawPage1 = 2;
    public int SceneDrawPage2 = 6;

    /// <summary>
    /// Whether the view slides between blocks. Off unless a host asks for it, because the slide
    /// spends engine time - it waits a tick per frame, and a tick is a door moving and a monster
    /// stepping. Every parity dump sets smoothScrollingEnabled false for exactly that reason
    /// ("the scroll is an animation, not a state change"), so the default matches them and the game
    /// turns it on for itself.
    /// </summary>
    public bool SmoothScrollingEnabled;

    private static readonly int[] ScrollXTop = StaticData.Table("ScrollXTop");
    private static readonly int[] ScrollYTop = StaticData.Table("ScrollYTop");
    private static readonly int[] ScrollXBottom = StaticData.Table("ScrollXBottom");
    private static readonly int[] ScrollYBottom = StaticData.Table("ScrollYBottom");

    /// <summary>Whether the message line is on its way out, and when it started going.</summary>
    private bool _fadeText;
    private double _palUpdateTimer;

    /// <summary>initTextFading: start the message line fading, or stop it and clear the line.</summary>
    public void InitTextFading(int textType, int clearField)
    {
        if ((_loader.Text?.TextColorFlag ?? 0) == textType || textType == 0)
        {
            _fadeText = true;
            _palUpdateTimer = _loader.Clock;
        }
        if (clearField == 0) return;
        StopPortraitSpeechAnim();
        if (_loader.NeedSceneRestore) _screen.CurDimIndex = _loader.Text?.ClearDim(3) ?? _screen.CurDimIndex;
        _fadeText = false;
        _loader.DisableTimer(11);
    }

    /// <summary>fadeTextStep: one step of that fade, run from inside whatever else is drawing.</summary>
    public void FadeTextStep()
    {
        if (!_fadeText) return;
        int elapsed = (int)((_loader.Clock - _palUpdateTimer) / LevelLoader.TickLength);
        if (_screen.FadeColor(192, 252, elapsed, 60)) return;
        if (_loader.NeedSceneRestore) return;
        _screen.CurDimIndex = _loader.Text?.ClearDim(3) ?? _screen.CurDimIndex;
        _loader.DisableTimer(11);
        _fadeText = false;
    }

    /// <summary>timerFadeMessageText: the line has been up long enough.</summary>
    public void TimerFadeMessageText()
    {
        _loader.DisableTimer(11);
        InitTextFading(0, 0);
    }

    /// <summary>The decoration a level script hung over the view, and where it hangs.</summary>
    public Shape SpecialGuiShape;
    public int SpecialGuiShapeX, SpecialGuiShapeY, SpecialGuiShapeMirrorFlag;

    /// <summary>drawSpecialGuiShape: that decoration, drawn over a page, and mirrored if it says so.</summary>
    public void DrawSpecialGuiShape(int pageNum)
    {
        if (SpecialGuiShape == null) return;
        _screen.DrawShape(pageNum, SpecialGuiShape, SpecialGuiShapeX, SpecialGuiShapeY, 2, 0);
        if ((SpecialGuiShapeMirrorFlag & 1) != 0)
            _screen.DrawShape(pageNum, SpecialGuiShape, SpecialGuiShapeX + SpecialGuiShape.Width, SpecialGuiShapeY, 2, 1);
    }

    /// <summary>
    /// smoothScrollDrawSpecialGuiShape: the shape is stitched into the rows the scroll will stretch,
    /// so it stretches with the picture rather than sitting still on top of it.
    /// </summary>
    private int SmoothScrollDrawSpecialGuiShape(int pageNum)
    {
        if (SpecialGuiShape == null) return 0;
        _screen.ClearGuiShapeMemory(pageNum);
        _screen.DrawShape(pageNum, SpecialGuiShape, SpecialGuiShapeX, SpecialGuiShapeY, 2, 0);
        _screen.CopyGuiShapeFromSceneBackupBuffer(pageNum, 14);
        return 1;
    }

    /// <summary>movePartySmoothScrollBlocked: the lurch when the party walks into a wall.</summary>
    public void MovePartySmoothScrollBlocked(int speed)
    {
        if (!SmoothScrollingEnabled || _loader.NeedSceneRestore) return;
        _screen.BackupSceneWindow(SceneDrawPage2 == 2 ? 2 : 6, 6);
        for (int i = 0; i < 2; i += 1) BlockedStep(i, speed);
        for (int i = 2; i > 0; i -= 1) BlockedStep(i, speed);
        if (_loader.SceneDefaultUpdate != 2) _screen.RestoreSceneWindow(6, 0);
        UpdateDrawPage2();
    }

    private void BlockedStep(int i, int speed)
    {
        _screen.SmoothScrollZoomStepTop(6, 2, ScrollXTop[i], ScrollYTop[i]);
        _screen.SmoothScrollZoomStepBottom(6, 2, ScrollXBottom[i], ScrollYBottom[i]);
        _screen.RestoreSceneWindow(2, 0);
        FadeTextStep();
        Wait?.Invoke(speed * LevelLoader.TickLength);
    }

    /// <summary>movePartySmoothScrollUp: a step forward, the view growing towards the party.</summary>
    public void MovePartySmoothScrollUp(int speed)
    {
        if (!SmoothScrollingEnabled || _loader.NeedSceneRestore) return;
        int d;
        if (SceneDrawPage2 == 2)
        {
            d = SmoothScrollDrawSpecialGuiShape(6);
            DrawScene(6);
            _screen.BackupSceneWindow(6, 12);
            _screen.BackupSceneWindow(2, 6);
        }
        else
        {
            d = SmoothScrollDrawSpecialGuiShape(2);
            DrawScene(2);
            _screen.BackupSceneWindow(2, 12);
            _screen.BackupSceneWindow(6, 6);
        }
        for (int i = 0; i < 5; i += 1)
        {
            _screen.SmoothScrollZoomStepTop(6, 2, ScrollXTop[i], ScrollYTop[i]);
            _screen.SmoothScrollZoomStepBottom(6, 2, ScrollXBottom[i], ScrollYBottom[i]);
            if (d != 0) _screen.CopyGuiShapeToSurface(14, 2);
            _screen.RestoreSceneWindow(2, 0);
            FadeTextStep();
            Wait?.Invoke(speed * LevelLoader.TickLength);
        }
        if (d != 0) _screen.CopyGuiShapeToSurface(14, 12);
        if (_loader.SceneDefaultUpdate != 2) _screen.RestoreSceneWindow(12, 0);
        UpdateDrawPage2();
    }

    /// <summary>movePartySmoothScrollDown: a step back, the same zoom run the other way.</summary>
    public void MovePartySmoothScrollDown(int speed)
    {
        if (!SmoothScrollingEnabled) return;
        int d = SmoothScrollDrawSpecialGuiShape(2);
        DrawScene(2);
        _screen.BackupSceneWindow(2, 6);
        for (int i = 4; i >= 0; i -= 1)
        {
            _screen.SmoothScrollZoomStepTop(6, 2, ScrollXTop[i], ScrollYTop[i]);
            _screen.SmoothScrollZoomStepBottom(6, 2, ScrollXBottom[i], ScrollYBottom[i]);
            if (d != 0) _screen.CopyGuiShapeToSurface(14, 2);
            _screen.RestoreSceneWindow(2, 0);
            FadeTextStep();
            Wait?.Invoke(speed * LevelLoader.TickLength);
        }
        if (d != 0) _screen.CopyGuiShapeToSurface(14, 12);
        if (_loader.SceneDefaultUpdate != 2) _screen.RestoreSceneWindow(6, 0);
        UpdateDrawPage2();
    }

    /// <summary>movePartySmoothScrollLeft: a sidestep, the view sliding across in three strips.</summary>
    public void MovePartySmoothScrollLeft(int speed)
    {
        if (!SmoothScrollingEnabled) return;
        speed <<= 1;
        DrawScene(SceneDrawPage1);
        for (int i = 88, d = 88; i > 22; i -= 22, d += 22)
        {
            _screen.SmoothScrollHorizontalStep(SceneDrawPage2, 66, d, i);
            _screen.CopyRegion(112 + i, 0, 112, 0, d, 120, SceneDrawPage1, SceneDrawPage2, true);
            _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage2, 0, true);
            FadeTextStep();
            Wait?.Invoke(speed * LevelLoader.TickLength);
        }
        if (_loader.SceneDefaultUpdate != 2) _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage1, 0, true);
        (SceneDrawPage1, SceneDrawPage2) = (SceneDrawPage2, SceneDrawPage1);
    }

    /// <summary>movePartySmoothScrollRight: the same sidestep, and its three steps are written out.</summary>
    public void MovePartySmoothScrollRight(int speed)
    {
        if (!SmoothScrollingEnabled) return;
        speed <<= 1;
        DrawScene(SceneDrawPage1);
        _screen.CopyRegion(112, 0, 222, 0, 66, 120, SceneDrawPage1, SceneDrawPage2, true);
        _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage2, 0, true);
        FadeTextStep();
        Wait?.Invoke(speed * LevelLoader.TickLength);
        int[] srcX = { 22, 44 };
        int[] width = { 66, 22 };
        int[] copyX = { 200, 178 };
        int[] copyW = { 88, 110 };
        for (int k = 0; k < 2; k += 1)
        {
            _screen.SmoothScrollHorizontalStep(SceneDrawPage2, srcX[k], 0, width[k]);
            _screen.CopyRegion(112, 0, copyX[k], 0, copyW[k], 120, SceneDrawPage1, SceneDrawPage2, true);
            _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage2, 0, true);
            FadeTextStep();
            Wait?.Invoke(speed * LevelLoader.TickLength);
        }
        if (_loader.SceneDefaultUpdate != 2) _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage1, 0, true);
        (SceneDrawPage1, SceneDrawPage2) = (SceneDrawPage2, SceneDrawPage1);
    }

    /// <summary>movePartySmoothScrollTurn: the view swings round through three squeezed frames.</summary>
    public void MovePartySmoothScrollTurn(int speed)
    {
        if (!SmoothScrollingEnabled) return;
        speed <<= 1;
        DrawScene(SceneDrawPage1);
        for (int step = 1; step <= 3; step += 1)
        {
            _screen.SmoothScrollTurnStep(step, SceneDrawPage2, SceneDrawPage1, 2);
            _screen.RestoreSceneWindow(2, 0);
            FadeTextStep();
            Wait?.Invoke(speed * LevelLoader.TickLength);
        }
        if (_loader.SceneDefaultUpdate != 2) _screen.CopyRegion(112, 0, 112, 0, 176, 120, SceneDrawPage1, 0, true);
        (SceneDrawPage1, SceneDrawPage2) = (SceneDrawPage2, SceneDrawPage1);
    }
}
