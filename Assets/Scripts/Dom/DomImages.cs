// Pictures for the page: <img src> (data: URLs, the page's PNG / JPEG files, SVG files drawn by SvgEl) and
// CSS url() backgrounds. Page-relative paths resolve under StreamingAssets/web, the web build's root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class DomImages
    {
        /// <summary>The web root on disk (set by the host).</summary>
        public static string WebRoot = Path.Combine(Application.streamingAssetsPath, "web");

        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        /// <summary>A raster picture (null for a missing file or an SVG).</summary>
        public static Texture2D Texture(string url)
        {
            if (string.IsNullOrEmpty(url) || IsSvg(url)) return null;
            if (Textures.TryGetValue(url, out var tex)) return tex;
            byte[] bytes = null;
            try
            {
                if (url.StartsWith("data:"))
                {
                    int comma = url.IndexOf(',');
                    if (comma > 0 && url.Substring(0, comma).EndsWith(";base64")) bytes = Convert.FromBase64String(url.Substring(comma + 1));
                }
                else
                {
                    string file = FilePath(url);
                    if (File.Exists(file)) bytes = File.ReadAllBytes(file);
                }
            }
            catch (Exception) { /* a broken picture, as in the browser */ }
            if (bytes != null)
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(bytes)) tex = null;
            }
            if (Textures.Count > 512) Textures.Clear();
            Textures[url] = tex;
            return tex;
        }

        static string FilePath(string url)
        {
            string rel = url.Split('?', '#')[0].TrimStart('/');
            return Path.Combine(WebRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        static bool IsSvg(string url) => url.StartsWith("data:image/svg") || url.Split('?', '#')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

        /// <summary>img.src = url</summary>
        public static void SetSrc(Image img, string url)
        {
            foreach (var old in img.Children().PruneSvg()) old.RemoveFromHierarchy();
            if (IsSvg(url))
            {
                img.image = null;
                var svg = LoadSvg(url);
                if (svg == null) return;
                svg.style.position = Position.Absolute;
                svg.style.left = svg.style.top = svg.style.right = svg.style.bottom = 0;
                svg.pickingMode = PickingMode.Ignore;
                svg.AddToClassList("img-svg");
                img.Add(svg);
                return;
            }
            img.image = Texture(url);
        }

        static IEnumerable<VisualElement> PruneSvg(this IEnumerable<VisualElement> children)
        {
            var list = new List<VisualElement>();
            foreach (var c in children) if (c.ClassListContains("img-svg")) list.Add(c);
            return list;
        }

        /// <summary>An SVG file as an SvgEl (its viewBox and child nodes).</summary>
        public static SvgEl LoadSvg(string url)
        {
            string text;
            try
            {
                if (url.StartsWith("data:"))
                {
                    int comma = url.IndexOf(',');
                    string payload = url.Substring(comma + 1);
                    text = url.Substring(0, comma).EndsWith(";base64") ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)) : Uri.UnescapeDataString(payload);
                }
                else text = File.ReadAllText(FilePath(url));
            }
            catch (Exception) { return null; }
            var doc = new XmlDocument();
            try { doc.LoadXml(text); } catch (XmlException) { return null; }
            var root = doc.DocumentElement;
            if (root == null || root.LocalName != "svg") return null;
            var svg = new SvgEl();
            Dom.Data(svg).tag = "svg";
            foreach (XmlAttribute a in root.Attributes) if (a.LocalName != "width" && a.LocalName != "height") svg.SetAttr(a.LocalName, a.Value);
            foreach (XmlNode n in root.ChildNodes) if (n is XmlElement el) svg.Add(Node(el));
            return svg;
        }

        static SvgNode Node(XmlElement el)
        {
            var n = new SvgNode(el.LocalName);
            foreach (XmlAttribute a in el.Attributes) n.SetAttr(a.LocalName, a.Value);
            foreach (XmlNode c in el.ChildNodes)
            {
                if (c is XmlElement ce) n.Add(Node(ce));
                else if (c is XmlText t && el.LocalName == "text") n.SetText(t.Value);
            }
            return n;
        }
    }
}
