// drawIconInto (main.mjs): a game shape - a face, an item icon - drawn through the game's palette
// into a small texture for the page. The shape is rasterised by the engine's own DrawShape on a
// private screen, so it comes out exactly as the game draws it.
using System.Collections.Generic;
using LolCore;
using UnityEngine;
using Screen = LolCore.Screen;

public static class Icons
{
    static readonly Screen Scratch = new Screen();
    static readonly Dictionary<string, Texture2D> Slots = new Dictionary<string, Texture2D>();

    /// <summary>Draws into the texture kept under `slot`, reusing it from call to call.</summary>
    public static Texture2D Draw(Shape shape, byte[] palette6, string slot, int scale = 1)
    {
        int sw = Mathf.Max(1, shape.Width), sh = Mathf.Max(1, shape.Height);
        int w = sw * scale, h = sh * scale;
        if (!Slots.TryGetValue(slot, out var tex) || tex == null)
        {
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Slots[slot] = tex;
        }
        else if (tex.width != w || tex.height != h) tex.Reinitialize(w, h);

        var page = Scratch.Page(0);
        for (int y = 0; y < sh && y < Screen.Height; y += 1)
            System.Array.Clear(page, y * Screen.Width, Mathf.Min(sw, Screen.Width));
        Scratch.DrawShape(0, shape, 0, 0, 0, 0);

        var px = new Color32[w * h];
        for (int y = 0; y < h; y += 1)
            for (int x = 0; x < w; x += 1)
            {
                int i = page[(y / scale) * Screen.Width + x / scale];
                px[(h - 1 - y) * w + x] = i == 0 ? new Color32(0, 0, 0, 0)
                    : new Color32((byte)(palette6[i * 3] * 255 / 63), (byte)(palette6[i * 3 + 1] * 255 / 63), (byte)(palette6[i * 3 + 2] * 255 / 63), 255);
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}
