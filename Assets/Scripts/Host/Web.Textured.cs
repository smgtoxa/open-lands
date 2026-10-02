// Unity build only: the Textured world is built the first time it is turned on (TexturedBuilder, from the player's own
// game files), in the background with a progress line; the scene keeps the original art for a level until its pack is
// in place, then switches. Turning the option off stops the build (no half-written pack is ever left).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        TexturedBuilder texturedBuild;
        Label texturedLabel;
        volatile int texturedReadyLevel;   // the last level whose pack the builder put in place
        int texturedSeenLevel;

        static readonly string MaterialsFolder = Path.Combine(Application.streamingAssetsPath, "materials");

        void updateTexturedBuild()
        {
            if (texturedFolder == null || dataFolder == null) return;
            bool want = settings.textured && !TexturedBuilder.AllBuilt(texturedFolder, dataFolder);
            if (want && texturedBuild == null)
            {
                // the materials are decoded here (Unity's image decoder lives on the main thread); the rest runs on
                // the builder's own thread
                var mats = new Dictionary<string, TexturedBuilder.Material>();
                foreach (var name in TexturedBuilder.MaterialNames)
                {
                    string file = Path.Combine(MaterialsFolder, name + ".jpg");
                    if (!File.Exists(file)) continue;
                    var tex = new Texture2D(2, 2);
                    if (!tex.LoadImage(File.ReadAllBytes(file))) continue;
                    var px = tex.GetPixels32();
                    var lum = new byte[px.Length];
                    // Texture2D rows run bottom-up: the material is read top-down like the image file
                    for (int y = 0; y < tex.height; y += 1)
                        for (int x = 0; x < tex.width; x += 1)
                        {
                            var c = px[(tex.height - 1 - y) * tex.width + x];
                            lum[y * tex.width + x] = (byte)((c.r * 299 + c.g * 587 + c.b * 114 + 500) / 1000);
                        }
                    mats[name] = new TexturedBuilder.Material { w = tex.width, h = tex.height, lum = lum };
                    UnityEngine.Object.Destroy(tex);
                }
                if (mats.Count == 0) { log("Textured world: no materials in StreamingAssets/materials"); return; }
                texturedBuild = new TexturedBuilder(dataFolder, texturedFolder, mats) { levelReady = lvl => texturedReadyLevel = lvl };
                texturedBuild.Start();
                timers.setInterval(texturedTick, 250);
            }
            else if (!settings.textured && texturedBuild != null && !texturedBuild.done) texturedBuild.cancel.Cancel();
        }

        void texturedTick()
        {
            var b = texturedBuild;
            if (b == null) return;
            // a level's pack just arrived: the scene looks for it again (a pack that failed to load stays failed)
            if (texturedReadyLevel != texturedSeenLevel)
            {
                texturedSeenLevel = texturedReadyLevel;
                foreach (var key in hdPacks.Keys.Where(k => k.StartsWith(texturedFolder + "|")).ToList()) if (!hdPacks[key].ready) hdPacks.Remove(key);
                if (engine != null) engine.sceneUpdateRequired = true;
            }
            if (texturedLabel == null)
            {
                texturedLabel = new Label { pickingMode = PickingMode.Ignore };
                var st = texturedLabel.style;
                st.position = Position.Absolute; st.right = 12; st.bottom = 12; st.paddingLeft = st.paddingRight = 10; st.paddingTop = st.paddingBottom = 5;
                st.backgroundColor = new Color(.08f, .06f, .03f, .9f); st.color = new Color(.94f, .83f, .53f); st.fontSize = 13;
                st.borderTopWidth = st.borderBottomWidth = st.borderLeftWidth = st.borderRightWidth = 1;
                st.borderTopColor = st.borderBottomColor = st.borderLeftColor = st.borderRightColor = new Color(.79f, .65f, .35f);
                Dom.document.Add(texturedLabel);
            }
            texturedLabel.BringToFront();
            if (!b.done)
            {
                texturedLabel.style.display = DisplayStyle.Flex;
                texturedLabel.text = b.cancel.IsCancellationRequested ? "Stopping the textured world…" : $"Building the textured world… level {Math.Max(1, b.level)} of 29";
                return;
            }
            texturedLabel.style.display = DisplayStyle.None;
            if (b.failed) toast($"The textured world could not be built: {b.error}", "danger");
            else if (!b.cancel.IsCancellationRequested) toast("The textured world is ready.", "fx-toast-ach");
            texturedBuild = null;
            foreach (var key in hdPacks.Keys.Where(k => k.StartsWith(texturedFolder + "|")).ToList()) if (!hdPacks[key].ready) hdPacks.Remove(key);
            if (engine != null) engine.sceneUpdateRequired = true;
            // (the interval keeps running harmlessly; a new build starts its own)
        }
    }
}
