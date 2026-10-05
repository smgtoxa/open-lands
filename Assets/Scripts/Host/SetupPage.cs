// setup.html, the desktop app's "Game data" page (desktop/main.js shows it when the game is missing, and
// Settings > Game data opens it): choose the GOG folder, import it into the app's data folder, start.
// window.lolDesktop's calls are here: dataDir, pickGogFolder (the folder dialog), importGame (on a worker
// thread, progress polled), importDone (the player starts again, as the desktop app reloads its window).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class SetupPage
    {
        /// <summary>desktop.dataDir(): where the import goes.</summary>
        public static string DataDir => Path.Combine(Application.persistentDataPath, "game");

        static VisualElement _page;

        /// <summary>window.location.href = "/setup.html": the page replaces the game.</summary>
        public static void Show(Scheduler sched, Action leavePage = null)
        {
            leavePage?.Invoke();
            var root = Dom.document;
            // the game page is left (as a browser leaves it): it is hidden, the setup page is the page now
            foreach (var c in root.Children().ToList()) if (c != _page) c.style.display = DisplayStyle.None;
            if (_page != null) { _page.style.display = StyleKeyword.Null; return; }
            _page = new VisualElement();
            _page.style.position = Position.Absolute;
            _page.style.left = _page.style.top = _page.style.right = _page.style.bottom = 0;
            root.Add(_page);
            SetupMarkup.Build(_page);
            Dom.ApplyUppercase(_page);
            var status = Dom.Q("#setup-status");
            var bar = Dom.Q(_page, ".bar");
            var fill = Dom.Q("#setup-fill");
            var pick = Dom.Q("#setup-pick");
            Dom.Q("#setup-target").SetText(DataDir);
            pick.On("click", async () =>
            {
                string folder = FileDialog.PickFolder("Select the GOG Lands of Lore folder (contains GAME.DAT)");
                if (folder == null)
                {
                    if (FileDialog.Missing != null) { status.SetClassName("err"); status.SetText(FileDialog.Missing); }
                    return;
                }
                pick.SetDisabled(true);
                bar.SetHidden(false);
                status.SetClassName("");
                status.SetText("Importing…");
                // desktop.onImportProgress: the worker reports, the page shows it
                (int done, int total, string name) progress = (0, 1, "");
                var import = Task.Run(() => ImportGame.importGame(folder, DataDir, (done, total, name) => progress = (done, total, name)));
                while (!import.IsCompleted)
                {
                    var p = progress;
                    fill.SetStyle("width", $"{Math.Round(100.0 * p.done / Math.Max(1, p.total))}%");
                    status.SetText($"{p.done}/{p.total} {p.name}");
                    await sched.Sleep(50);
                }
                if (import.IsFaulted)
                {
                    status.SetClassName("err");
                    status.SetText($"Import failed: {import.Exception?.GetBaseException().Message}");
                    pick.SetDisabled(false);
                    return;
                }
                var result = import.Result;
                fill.SetStyle("width", "100%");
                status.SetText($"Imported {result.files} files from {result.volumeId}. Starting…");
                await sched.Sleep(600);
                ImportDone();
            });
        }

        /// <summary>desktop.importDone(): the game starts on the imported data (the player is started again).</summary>
        static void ImportDone()
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            // an explicit --data would win over the import: the new copy is what starts
            int at = args.IndexOf("--data");
            if (at >= 0) args.RemoveRange(at, Math.Min(2, args.Count - at));
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(new ProcessStartInfo(exe, string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))) { UseShellExecute = false });
            Application.Quit();
        }
    }
}
