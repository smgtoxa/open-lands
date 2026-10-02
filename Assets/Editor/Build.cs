// Batch-mode entry points, run from WSL by build.sh:
//   Build.Setup    - the one scene: a camera (the audio listener) with the GameHost on it
//   Build.Windows  - Setup, then a Windows player in Build/Windows
//   Build.Release  - Setup, then the Windows and Linux players in Build/Release, without what may not be given away
//                    (the guide's screenshots show the original game: they stay out of the release)
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Build
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera");
        var camera = cam.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(8, 11, 12, 255);
        cam.AddComponent<AudioListener>();
        cam.tag = "MainCamera";
        new GameObject("Page").AddComponent<LolHost.HostMain>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.productName = "Open Lands";
        PlayerSettings.companyName = "open_lands";
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 1000;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.visibleInBackground = true;
        AssetDatabase.SaveAssets();
    }

    static bool Player(string path, BuildTarget target)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None });
        Debug.Log($"BUILD RESULT ({target}): {report.summary.result} errors={report.summary.totalErrors}");
        return report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
    }

    public static void Release()
    {
        Setup();
        // the guide's pictures are screenshots of the original game (its art): out of the project for the build
        const string guide = "Assets/Resources/Guide", aside = "Assets/GuideShots~";
        bool moved = false;
        try
        {
            if (Directory.Exists(guide))
            {
                Directory.Move(guide, aside);
                if (File.Exists(guide + ".meta")) File.Move(guide + ".meta", aside + ".meta.bak");
                moved = true;
                AssetDatabase.Refresh();
            }
            bool ok = Player("Build/Release/Windows/OpenLands.exe", BuildTarget.StandaloneWindows64)
                && Player("Build/Release/Linux/OpenLands.x86_64", BuildTarget.StandaloneLinux64);
            Debug.Log($"BUILD RESULT: {(ok ? "Succeeded" : "Failed")} errors={(ok ? 0 : 1)}");
            if (!ok) EditorApplication.Exit(1);
        }
        finally
        {
            if (moved)
            {
                Directory.Move(aside, guide);
                if (File.Exists(aside + ".meta.bak")) File.Move(aside + ".meta.bak", guide + ".meta");
                AssetDatabase.Refresh();
            }
        }
    }

    public static void Windows()
    {
        Setup();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/Windows/OpenLands.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log($"BUILD RESULT: {report.summary.result} errors={report.summary.totalErrors}");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
