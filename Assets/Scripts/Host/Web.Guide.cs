// Unity build only: the Journal's Guide tab, how to use what the original game did not have.
// The articles are Resources/guide.txt (its header explains the format), the pictures Resources/Guide/*.png.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        sealed class GuideArticle
        {
            public string category, title, image, keys = "";
            public readonly List<string> lines = new List<string>();
            public string Text => $"{title} {category} {keys} {string.Join(" ", lines)}".ToLowerInvariant();
        }

        List<GuideArticle> guide;
        VisualElement guidePage;
        string guideOpen;   // the title of the article shown in full

        // the tab goes in before init_C10 wires the journal's tabs, so it is one of them
        void init_C09_guide()
        {
            var bar = Q("#journal-overlay .tabs");
            if (bar == null) return;
            var tab = Dom.El("button", "tab");
            tab.SetAttr("type", "button");
            tab.SetAttr("data-tab", "guide");
            var icon = Dom.El("svg", "");
            tab.Add(icon);
            ((SvgEl)icon).UseSymbol("#i-compass");
            tab.Append("Guide");
            bar.Append(tab);
            guidePage = Dom.El("section", "tab-page");
            guidePage.SetAttr("data-page", "guide");
            guidePage.SetHidden(true);
            bar.parent.Append(guidePage);
        }

        static List<GuideArticle> parseGuide()
        {
            var list = new List<GuideArticle>();
            var text = UnityEngine.Resources.Load<TextAsset>("guide")?.text ?? "";
            string category = "";
            GuideArticle a = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("# ")) continue;
                if (line.StartsWith("= ")) { category = line.Substring(2).Trim(); continue; }
                if (line.StartsWith("## ")) { list.Add(a = new GuideArticle { category = category, title = line.Substring(3).Trim() }); continue; }
                if (a == null) continue;
                if (line.StartsWith("img:")) a.image = line.Substring(4).Trim();
                else if (line.StartsWith("keys:")) a.keys = line.Substring(5).Trim();
                else a.lines.Add(line);
            }
            return list;
        }

        // the list of articles (with the search over it), or one article on the whole page with a way back
        void renderGuide()
        {
            if (guidePage == null) return;
            guide = guide ?? parseGuide();
            guidePage.ReplaceChildren();
            var tools = Dom.El("div", "actions");
            var search = (DomInput)Dom.El("input");
            search.SetAttr("type", "search");
            search.SetAttr("placeholder", "Search the guide…");
            var count = Dom.El("span", "guide-count");
            tools.Append(search, count);
            var list = Dom.El("div", "guide-list");
            var article = Dom.El("div", "guide-article");
            guidePage.Append(tools, list, article);
            float listScroll = 0;

            void showList()
            {
                var words = (search.value ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var found = guide.Where(g => words.All(w => g.Text.Contains(w))).ToList();
                count.SetText(words.Length == 0 ? $"{guide.Count} articles" : $"{found.Count} of {guide.Count} articles");
                list.ReplaceChildren();
                if (found.Count == 0) list.Append(Dom.El("p", "panel-note", "Nothing in the guide matches. Try another word."));
                string category = null;
                foreach (var g in found)
                {
                    if (g.category != category) { category = g.category; list.Append(Dom.El("h5", "guide-cat", category)); }
                    var card = Dom.El("div", "guide-card");
                    card.Append(Dom.El("span", "guide-name", g.title), Dom.El("p", "guide-sum", g.lines.FirstOrDefault() ?? ""));
                    card.On("click", () => open(g));
                    list.Append(card);
                }
                CssLayout.Touch(list);
            }

            void open(GuideArticle g)
            {
                listScroll = CssLayout.ScrollTop(guidePage);
                tools.SetHidden(true); list.SetHidden(true); article.SetHidden(false);
                article.ReplaceChildren();
                var back = Dom.El("button", "guide-back", "‹  All articles");
                back.SetAttr("type", "button");
                back.On("click", () =>
                {
                    article.SetHidden(true); tools.SetHidden(false); list.SetHidden(false);
                    CssLayout.SetScrollTop(guidePage, listScroll);
                });
                article.Append(back, Dom.El("h5", "guide-cat", g.category), Dom.El("h3", "guide-title", g.title));
                var shot = g.image != null ? UnityEngine.Resources.Load<Texture2D>("Guide/" + g.image) : null;
                if (shot != null)
                {
                    var img = new VisualElement { pickingMode = PickingMode.Ignore };
                    img.AddToClassList("guide-shot");
                    float w = Math.Min(shot.width, 640);
                    img.style.width = w; img.style.height = w * shot.height / shot.width;
                    img.style.backgroundImage = shot;
                    article.Append(img);
                }
                foreach (var line in g.lines)
                    article.Append(line.StartsWith("- ") ? Dom.El("p", "guide-point", "•  " + line.Substring(2)) : Dom.El("p", "guide-text", line));
                CssLayout.Touch(article);
                CssLayout.SetScrollTop(guidePage, 0);
            }

            article.SetHidden(true);
            search.On("input", showList);
            showList();
        }
    }
}
