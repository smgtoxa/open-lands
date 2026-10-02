// The in-game TIM opcodes: what a conversation script can ask the game to do.
//
// Transliterated from makeTimIngameOpcodes in src/game/script.mjs. These are the calls a TIM makes
// through its own "exec opcode" command - opening the text panel, moving the party, drawing the
// scene, speaking a line - as opposed to the TIM VM's own commands, which live in Tim.cs.
//
// The ones that only make noise or fade the palette answer 1 without doing anything: they change
// nothing a comparison can see, and saying so here is better than a silent gap.
namespace LolCore;

public static class TimOpcodes
{
    public static Func<TimScript, int[], int>[] InGame(LevelLoader loader, Gui gui, SceneShapes scene)
    {
        _ = scene;
        var screen = loader.Screen;
        return new Func<TimScript, int[], int>[]
        {
            // 0: the text panel slides up over the portraits
            (tim, p) => { loader.InitSceneWindowDialogue(p[0]); return 1; },
            // 1: and down again
            (tim, p) => { loader.RestoreAfterSceneWindowDialogue(p[0] != 0); return 1; },
            null,
            // 3: an item into the party's row, or nowhere
            (tim, p) =>
            {
                int item = loader.Items.Make(p[0], p[1], p[2], loader.Level);
                if (loader.Items.AddToInventory(item)) return 1;
                loader.Items.Delete(item);
                return 0;
            },
            // 4: the party is turned, or put somewhere
            (tim, p) =>
            {
                if (p[0] == 1) loader.Party.Direction = p[1] & 3;
                else if (p[0] == 0) loader.Party.MoveTo(p[1]);
                return 1;
            },
            // 5: the fades. Each one costs time on the clock the script is scheduled against, and
            // some of them leave the view black, so they are not decoration.
            (tim, p) =>
            {
                var wait = loader.Tim?.Wait;
                var p0 = screen.Palette(0);
                var p3 = screen.Palette(3);
                switch (p[0])
                {
                    case 0:
                        screen.FadeClearSceneWindow(10, wait);
                        break;
                    case 1:
                        Array.Copy(p0, 128 * 3, p3, 128 * 3, p0.Length - 128 * 3);
                        screen.LoadSpecialColors(p3);
                        screen.FadePalette(p3, 10, wait);
                        screen.FadeFlag = 0;
                        break;
                    case 2:
                        screen.FadeToBlack(10, wait);
                        break;
                    case 3:
                        screen.LoadSpecialColors(p3);
                        screen.FadePalette(p3, 10, wait);
                        screen.FadeFlag = 0;
                        break;
                    case 4:
                        if (screen.FadeFlag != 2) screen.FadeClearSceneWindow(10, wait);
                        gui?.DrawPlayField();
                        screen.FadeFlag = 0;
                        break;
                    case 5:
                        screen.LoadSpecialColors(p3);
                        screen.FadePalette(screen.Palette(1), 10, wait);
                        screen.FadeFlag = 0;
                        break;
                }
                return 1;
            },
            // 6: a straight page-to-page blit
            (tim, p) => { screen.CopyRegion(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], true); return 1; },
            // 7: a character speaks. The line goes into the text panel - which dim, and how wide,
            // depends on the mode the script asked for. The portrait's mouth moving is presentation.
            (tim, p) =>
            {
                if (gui == null) return 1;
                string line = GameStrings.Get(p[2], gui.LandsFile, gui.LevelLangFile) ?? "";
                int mode = p[1];
                gui.StartCharacterChat((short)p[0], mode, 1, line);
                if (mode == 0) gui.PrintDialogueText(3, line, p, 3);
                else if (mode == 1)
                {
                    loader.Text?.ClearDim(4);
                    screen.ModifyScreenDim(4, 16, 123, 23, 47);
                    gui.PrintDialogueText(4, line, p, 3);
                    screen.ModifyScreenDim(4, 11, 123, 28, 47);
                }
                else if (mode == 2)
                {
                    loader.Text?.ClearDim(4);
                    screen.ModifyScreenDim(4, 9, 133, 30, 60);
                    gui.PrintDialogueText(4, line, p, 3);
                    screen.ModifyScreenDim(4, 1, 133, 37, 60);
                }
                gui.UpdatePortraitSpeechAnim();
                return 1;
            },
            // 8: draw the 3D view
            (tim, p) => { gui?.DrawScene(p[0]); return 1; },
            // 9: update - the host's own frame
            (tim, p) => 1,
            // 10: clear the dialogue box
            (tim, p) =>
            {
                if (gui != null && gui.CurrentControlMode != 0 && !(loader.Text?.TextEnabled ?? true)) return 1;
                screen.CurDimIndex = 5;
                var d = screen.Dims[5];
                screen.FillRect(d.Sx, d.Sy, d.Sx + d.W - 2, d.Sy + d.H - 2, (byte)d.Col2);
                loader.Text?.ClearDim(4);
                loader.Text?.ResetDimTextPositions(4);
                return 1;
            },
            // 11, 12: the music
            (tim, p) => { loader.OnLoadSoundFile?.Invoke(p[0]); return 1; },
            (tim, p) => { loader.OnMusicTrack?.Invoke(p[0]); return 1; },
            // 13: a line of speech, or its text when there is no speech
            (tim, p) =>
            {
                if (gui == null) return 1;
                string line = GameStrings.Get(p[0], gui.LandsFile, gui.LevelLangFile) ?? "";
                gui.PrintDialogueText(4, line, p, 1);
                return 1;
            },
            // 14: a sound effect
            (tim, p) => { loader.OnSoundEffect?.Invoke(p[0]); return 1; },
            // 15, 16: an animation slot started and stopped
            (tim, p) => { loader.Tim?.Animator.Start(p[0], p[1]); return 1; },
            (tim, p) => { loader.Tim?.Animator.Stop(p[0]); return 1; },
        };
    }
}
