// Command-line playtest driver, for checking a build without a hand on the keyboard:
//   --champion N            start a new game with champion N (no intro)
//   --load SLOT             load a save slot instead
//   --do "t:action,..."     at t seconds: a key name (UpArrow, Space, F...), click:X:Y, choose:N, shot:FILE
//   --quit-after SECONDS    exit
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public sealed class Autopilot : MonoBehaviour
{
    public GameHost Host;
    readonly List<(float At, string What)> _plan = new List<(float, string)>();
    float _start = -1, _quitAt = -1;
    int _champion = -1;
    string _load;

    void Start()
    {
        var args = Environment.GetCommandLineArgs();
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--champion") is string c) _champion = int.Parse(c);
        _load = Arg("--load");
        if (Arg("--quit-after") is string q) _quitAt = float.Parse(q, CultureInfo.InvariantCulture);
        if (Arg("--do") is string plan)
            foreach (var step in plan.Split(','))
            {
                int colon = step.IndexOf(':');
                if (colon < 0) continue;
                _plan.Add((float.Parse(step.Substring(0, colon), CultureInfo.InvariantCulture), step.Substring(colon + 1)));
            }
        enabled = _champion >= 0 || _load != null || _plan.Count > 0 || _quitAt > 0;
    }

    void Update()
    {
        if (Host.State == GameHost.Mode.NoData) return;
        if (_start < 0)
        {
            if (Host.State != GameHost.Mode.Title) return;
            _start = Time.time;
            if (_champion >= 0) { Host.Settings.Intro = false; Host.NewGame(); Host.ChooseChampion(_champion); }
            else if (_load != null) Host.LoadFrom(_load);
        }
        float t = Time.time - _start;
        for (int i = 0; i < _plan.Count; i += 1)
        {
            if (_plan[i].At > t) continue;
            Run(_plan[i].What);
            _plan.RemoveAt(i);
            i -= 1;
        }
        if (_quitAt > 0 && t >= _quitAt) Application.Quit();
    }

    static System.Collections.IEnumerator Shot(string path)
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"autopilot: wrote {path}");
    }

    void Run(string what)
    {
        Debug.Log($"autopilot: {what} | level {Host.Loader.Level} block {Host.Party?.Block} dir {Host.Party?.Direction} flags {Host.Loader.UpdateFlags}");
        var parts = what.Split(':');
        switch (parts[0])
        {
            case "shot": StartCoroutine(Shot(what.Substring(5))); break;
            case "click": Host.OnSceneClick(int.Parse(parts[1]), int.Parse(parts[2]), parts.Length > 3 ? int.Parse(parts[3]) : 1); break;
            case "choose": Host.Choose(int.Parse(parts[1])); break;
            case "mon":
                var ms = Host.Loader.Board.Monsters;
                var sb = new System.Text.StringBuilder("autopilot: monsters");
                for (int i = 0; i < ms.Length; i += 1)
                    if (ms[i] != null && ms[i].HitPoints > 0) sb.Append($" #{i} b{ms[i].Block} mode{ms[i].Mode}");
                sb.Append($" | pending={(Host.Loader.PendingScript != null)} buttons={Host.Gui.DialogueNumButtons} flags={Host.Loader.UpdateFlags}");
                Debug.Log(sb.ToString());
                break;
            default:
                if (Enum.TryParse(parts[0], out KeyCode key)) Host.OnKey(key, false);
                else Debug.LogWarning($"autopilot: unknown step {what}");
                break;
        }
    }
}
