// src/platform/filter-screen.mjs: presenter with optional display filters for the 320x200 indexed frame:
// "smooth" (xBR-style pixel-art upscaling), "crt" (scanlines, curvature, glow) or "both".
// The WebGL programs are the passes of Resources/FilterScreen.shader, the framebuffers are RenderTextures
// and each gl draw is a Graphics.Blit. The "canvas" (the WebGL drawing buffer) is the output
// RenderTexture, shown as the element's background. C# 9.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class FilterScreen
    {
        const int SCALE = 4;
        // the JS programs, as shader pass indices
        const int PALETTE = 0, COPY = 1, XBR = 2, SOFT = 3, CRT = 4;

        public readonly CanvasEl canvas;
        public int width, height;
        public string mode;
        readonly Material material;
        Texture2D indexTex;
        readonly Texture2D paletteTex;
        RenderTexture rgb, rgbLinear, scaled, output;

        public FilterScreen(CanvasEl canvas, int width = 320, int height = 200)
        {
            this.canvas = canvas;
            this.width = width;
            this.height = height;
            // canvas.getContext("webgl") failing -> the shader missing or unsupported on this GPU
            var shader = Resources.Load<Shader>("FilterScreen");
            if (shader == null || !shader.isSupported) throw new Exception("The filter shader is unavailable");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mode = "smooth";
            paletteTex = texture(TextureFormat.RGB24, 256, 1);
            allocate();
        }

        static Texture2D texture(TextureFormat format, int w, int h) =>
            new Texture2D(w, h, format, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        static RenderTexture target(int w, int h, bool linear = false)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                filterMode = linear ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        // The constructor's / resize's textures. canvas.width/height = size * SCALE in JS sizes the WebGL
        // drawing buffer: here that is the output RenderTexture (the CanvasEl's own 2D pixels stay unused).
        // The browser scales the canvas to the element smoothly, hence the bilinear output.
        void allocate()
        {
            indexTex = texture(TextureFormat.R8, width, height);
            rgb = target(width, height);
            rgbLinear = target(width, height, true);
            scaled = target(width * SCALE, height * SCALE);
            output = target(width * SCALE, height * SCALE, true);
            canvas.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(output));
        }

        public void setMode(string mode)
        {
            this.mode = mode;
        }

        void pass(int prog, Texture inputTex, RenderTexture target, Action setup)
        {
            material.SetFloat("uFlip", target != null ? 0f : 1f);
            setup?.Invoke();
            Graphics.Blit(inputTex, target ?? output, material, prog);
        }

        // The frame changes size when the view switches between the full 320x200 playfield and the
        // scene window, so follow it instead of failing (which used to turn the filter off for good).
        public void resize(int width, int height)
        {
            if (width == this.width && height == this.height) return;
            this.width = width;
            this.height = height;
            if (indexTex != null) UnityEngine.Object.Destroy(indexTex);
            foreach (var t in new[] { rgb, rgbLinear, scaled, output }) { if (t == null) continue; t.Release(); UnityEngine.Object.Destroy(t); }
            allocate();
        }

        public void draw(byte[] pixels, byte[] palette)
        {
            if (pixels.Length != width * height) throw new Exception("Pixel buffer does not match the screen");
            indexTex.SetPixelData(pixels, 0);
            indexTex.Apply(false);
            paletteTex.SetPixelData(palette, 0);
            paletteTex.Apply(false);
            // 1. indices -> colours
            pass(PALETTE, indexTex, rgb, () => material.SetTexture("uPalette", paletteTex));
            bool smooth = mode == "smooth" || mode == "both";
            bool soft = mode == "soft" || mode == "softcrt";
            bool crt = mode == "crt" || mode == "both" || mode == "softcrt";
            canvas.MarkDirtyRepaint();
            // 2. optional upscale into the 4x buffer
            Texture source = rgb;
            if (smooth || soft)
            {
                Texture input = rgb;
                if (soft)
                {
                    // the soft shader wants bilinear samples: copy the frame into the linear-filtered target
                    pass(COPY, rgb, rgbLinear, null);
                    input = rgbLinear;
                }
                pass(soft ? SOFT : XBR, input, crt ? scaled : null, () => material.SetVector("uSize", new Vector4(width, height)));
                if (!crt) return;
                source = scaled;
            }
            // 3. CRT look or plain copy to the canvas
            if (crt)
            {
                pass(CRT, source, null, () =>
                {
                    material.SetVector("uOut", new Vector4(output.width, output.height));
                    material.SetFloat("uLines", height);
                });
            }
            else pass(COPY, source, null, null);
        }
    }
}
