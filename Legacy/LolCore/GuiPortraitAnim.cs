// A character speaking: which face the portrait wears while a line is being delivered.
//
// Transliterated from src/game/party.mjs (updatePortraitSpeechAnim, stopPortraitSpeechAnim,
// setCharFaceFrame, faceFrameRefresh) and the portrait half of playCharacterScriptChat in
// src/game/text.mjs.
//
// The face is picked with a die, but not the game's die: it is presentation, and the engine rolls
// it on a separate stream so a conversation cannot shift the dice a fight later depends on. The
// port keeps the same separation, and the same stream, so a recorded conversation looks the same.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>Whose portrait is speaking, or -1.</summary>
    public int UpdateCharNum = -1;
    public int PortraitSpeechAnimMode;
    public int UpdatePortraitSpeechAnimDuration;
    public int ResetPortraitAfterSpeechAnim;

    /// <summary>The presentation stream: the same LCG the engine keeps beside its own dice.</summary>
    public uint PresentationSeed = 0x12345678;

    private double PresentationFloat()
    {
        PresentationSeed = unchecked(PresentationSeed * 1103515245 + 12345);
        return ((PresentationSeed >> 16) & 0x7fff) / (double)0x8000;
    }

    public int PresentationRoll(int times, int pips, int inc = 0)
    {
        if (times <= 0 || pips <= 0) return inc;
        int res = 0;
        while (times-- > 0) res += 1 + (int)(PresentationFloat() * pips);
        return res + inc;
    }

    public void SetCharFaceFrame(int charNum, int frameNum) => Characters[charNum].CurFaceFrame = frameNum;

    public void FaceFrameRefresh(int charNum)
    {
        var c = Characters[charNum];
        if (c.CurFaceFrame == 1) SetTemporaryFaceFrame(charNum);
        else if (c.CurFaceFrame == 6)
        {
            if (c.TempFaceFrame != 5) SetTemporaryFaceFrame(charNum);
            else c.CurFaceFrame = 5;
        }
        else c.CurFaceFrame = 0;
    }

    private void SetTemporaryFaceFrame(int charNum)
    {
        Characters[charNum].CurFaceFrame = 0;
        Characters[charNum].TempFaceFrame = 0;
    }

    public void StopPortraitSpeechAnim()
    {
        if (UpdateCharNum == -1) return;
        UpdatePortraitSpeechAnimDuration = 1;
        ResetPortraitAfterSpeechAnim = 2;
        UpdatePortraitSpeechAnim();
    }

    /// <summary>
    /// characterSays: the hero speaks. charId 1 means whoever is selected, 0 or less means nobody in
    /// particular, and anything else is a hero's own id. True when the words should be printed too.
    /// </summary>
    public bool CharacterSays(int track, int charId, bool redraw)
    {
        if (charId == 1) charId = SelectedCharacter;
        if (charId <= 0) charId = 0;
        else
        {
            int i = 0;
            for (; i < 4; i += 1)
            {
                if (Characters[i].Id != charId || !Characters[i].Active) continue;
                charId = i;
                break;
            }
            if (i == 4) return false;
        }
        bool played = _loader.PlayCharacterSpeech?.Invoke(track, charId) ?? false;
        if (played && redraw)
        {
            StopPortraitSpeechAnim();
            UpdateCharNum = charId;
            PortraitSpeechAnimMode = 0;
            ResetPortraitAfterSpeechAnim = 1;
            UpdatePortraitSpeechAnim();
        }
        // A voice that played says the words itself, so they are only printed when text is on as well.
        return played ? (_loader.Text?.TextEnabled ?? true) : true;
    }

    /// <summary>
    /// playCharacterScriptChat, the portrait half: who is speaking, in which of the three layouts,
    /// and for how long - the line's length decides that.
    /// </summary>
    public void StartCharacterChat(int charId, int mode, int restorePortrait, string line)
    {
        int ch = 0;
        bool skipAnim = false;
        if (charId == -1 || (charId & 0x70) == 0)
            charId = ch = charId == 1 ? (SelectedCharacter != 0 ? Characters[SelectedCharacter].Id : 0) : charId;
        else charId ^= 0x70;
        StopPortraitSpeechAnim();
        if (charId < 0) charId = ch = PresentationRandom(CountActiveCharacters() - 1);
        else if (charId > 0)
        {
            int i = 0;
            for (; i < 4; i += 1)
            {
                if (Characters[i].Id != charId || !Characters[i].Active) continue;
                if (charId == ch) ch = i;
                charId = i;
                break;
            }
            if (i == 4)
            {
                if (charId == 8) skipAnim = true;
                else return;
            }
        }
        if (!skipAnim && charId < 3)
        {
            UpdateCharNum = charId;
            PortraitSpeechAnimMode = mode;
            UpdatePortraitSpeechAnimDuration = (line?.Length ?? 0) >> 1;
            ResetPortraitAfterSpeechAnim = restorePortrait;
        }
        _ = ch;
    }

    public int PresentationRandom(int range) => (int)(PresentationFloat() * (range + 1));

    /// <summary>
    /// timerRegeneratePoints: every twenty seconds the party gets a little health and magic back -
    /// more with the right ring on, none at all while starving, and magic drains away instead while
    /// something is draining it.
    /// </summary>
    public void TimerRegeneratePoints()
    {
        for (int i = 0; i < 4; i += 1)
        {
            var c = Characters[i];
            if (!c.Active) continue;
            int hInc = (c.Flags & 8) != 0 ? 0 : (_loader.ItemEquipped(i, 228) ? 4 : 1);
            int mInc = _loader.DrainMagic != 0
                ? -(c.MagicPointsMax >> 5)
                : (c.Flags & 8) != 0 ? 0 : (_loader.ItemEquipped(i, 227) ? c.MagicPointsMax / 10 : 1);
            c.MagicPointsCur = Math.Max(0, Math.Min(c.MagicPointsMax, c.MagicPointsCur + mInc));
            if ((c.Flags & 0x80) == 0) IncreaseCharacterHitpoints(i, hInc, false);
            DrawCharPortraitWithStats(i);
        }
    }

    /// <summary>
    /// timerUpdatePortraitAnimations: the faces blink. Each one waits its own count of ticks, blinks
    /// for a tenth of a second, then draws a new count.
    /// </summary>
    public void TimerUpdatePortraitAnimations(int skipUpdate)
    {
        if (skipUpdate != 1) skipUpdate = 0;
        for (int i = 0; i < 4; i += 1)
        {
            var c = Characters[i];
            if (!c.Active || (c.Flags & 8) != 0 || c.CurFaceFrame > 1) continue;
            if (c.CurFaceFrame != 1)
            {
                if (--c.NextAnimUpdateCountdown <= 0 && skipUpdate == 0)
                {
                    c.CurFaceFrame = 1;
                    DrawCharPortraitWithStats(i);
                    _loader.SetTimerCountdown(9, 10);
                }
            }
            else
            {
                c.CurFaceFrame = 0;
                DrawCharPortraitWithStats(i);
                c.NextAnimUpdateCountdown = _loader.Dice.RollDice(1, 12) + 6;
            }
        }
    }

    /// <summary>
    /// updatePortraitSpeechAnim: the next face of a talking portrait, and where it is drawn - over
    /// the portrait itself, or over the text panel when a conversation has taken the screen.
    /// </summary>
    /// <summary>When the speaking mouth may move again (party.mjs updatePortraitNext).</summary>
    private double _portraitNext;

    /// <summary>Whether someone is speaking and their mouth is due to move.</summary>
    public bool PortraitSpeechAnimDue => UpdateCharNum != -1 && _loader.Clock > _portraitNext;

    public void UpdatePortraitSpeechAnim()
    {
        if (UpdateCharNum < 0 || UpdateCharNum > 3) return;
        int x = 0, y = 0;
        bool redraw = false;
        bool textEnabled = _loader.Text?.TextEnabled ?? true;
        if (PortraitSpeechAnimMode == 0)
        {
            x = ActiveCharsXpos[UpdateCharNum];
            y = 144;
            redraw = true;
        }
        else if (PortraitSpeechAnimMode == 1)
        {
            if (textEnabled) { x = 90; y = 130; }
            else { x = ActiveCharsXpos[UpdateCharNum]; y = 144; }
        }
        else if (PortraitSpeechAnimMode == 2)
        {
            if (textEnabled) { x = 16; y = 134; }
            else { x = ActiveCharsXpos[UpdateCharNum] + 10; y = 145; }
        }
        int f = PresentationRoll(1, 6) - 1;
        if (f == Characters[UpdateCharNum].CurFaceFrame) f += 1;
        if (f > 5) f -= 5;
        f += 7;
        // With no speech playing the animation lasts one more step, which is what the engine does
        // when it is showing text instead of speaking.
        if (ResetPortraitAfterSpeechAnim == 2) ResetPortraitAfterSpeechAnim = 1;
        UpdatePortraitSpeechAnimDuration -= 1;
        if (UpdatePortraitSpeechAnimDuration != 0)
        {
            SetCharFaceFrame(UpdateCharNum, f);
            if (redraw) DrawCharPortraitWithStats(UpdateCharNum);
            else DrawCharFaceShape(UpdateCharNum, x, y, 0);
            _portraitNext = _loader.Clock + 10 * LevelLoader.TickLength;
        }
        else if (ResetPortraitAfterSpeechAnim != 0)
        {
            FaceFrameRefresh(UpdateCharNum);
            if (redraw) DrawCharPortraitWithStats(UpdateCharNum);
            else DrawCharFaceShape(UpdateCharNum, x, y, 0);
            UpdateCharNum = -1;
        }
    }
}
