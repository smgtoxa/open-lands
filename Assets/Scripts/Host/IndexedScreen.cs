// src/platform/indexed-screen.mjs: an 8-bit image with a 256-colour palette, drawn into a canvas.
using System;

namespace LolHost
{
    public sealed class IndexedScreen
    {
        public readonly CanvasEl canvas;
        readonly Ctx2D context;
        ImageData image;
        public byte[] pixels;
        public readonly byte[] palette = new byte[768];
        // screen.render: a field so main.mjs can replace it (the display filter) like the JS method
        public Action render;

        public IndexedScreen(CanvasEl canvas)
        {
            this.canvas = canvas;
            context = canvas.getContext("2d");
            context.imageSmoothingEnabled = false;
            image = context.createImageData(canvas.width, canvas.height);
            pixels = new byte[canvas.width * canvas.height];
            render = renderImage;
        }

        public void resize(int width, int height)
        {
            if (canvas.width == width && canvas.height == height) return;
            canvas.width = width;
            canvas.height = height;
            image = context.createImageData(width, height);
            pixels = new byte[width * height];
            context.imageSmoothingEnabled = false;
        }

        public void draw(byte[] pixels, byte[] palette)
        {
            if (pixels.Length != canvas.width * canvas.height) throw new Exception("Pixel buffer does not match the canvas");
            if (palette.Length != 768) throw new Exception("Palette must contain 256 RGB colors");
            Array.Copy(pixels, this.pixels, pixels.Length);
            Array.Copy(palette, this.palette, 768);
            render();
        }

        public void blit(byte[] pixels, int width, int height, int x, int y)
        {
            for (int sourceY = 0; sourceY < height; sourceY += 1)
            {
                int targetY = y + sourceY;
                if (targetY < 0 || targetY >= canvas.height) continue;
                int sourceX = x < 0 ? -x : 0;
                int targetX = Math.Max(0, x);
                int count = Math.Min(width - sourceX, canvas.width - targetX);
                if (count <= 0) continue;
                Array.Copy(pixels, sourceY * width + sourceX, this.pixels, targetY * canvas.width + targetX, count);
            }
            render();
        }

        public void copyRegion(byte[] source, int sourceWidth, int sourceX, int sourceY, int targetX, int targetY, int width, int height)
        {
            for (int row = 0; row < height; row += 1)
                Array.Copy(source, (sourceY + row) * sourceWidth + sourceX, pixels, (targetY + row) * canvas.width + targetX, width);
            render();
        }

        void renderImage()
        {
            var rgba = image.data;
            for (int i = 0, o = 0; i < pixels.Length; i += 1, o += 4)
            {
                int c = pixels[i] * 3;
                rgba[o] = palette[c];
                rgba[o + 1] = palette[c + 1];
                rgba[o + 2] = palette[c + 2];
                rgba[o + 3] = 255;
            }
            context.putImageData(image, 0, 0);
        }
    }
}
