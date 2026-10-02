// Unity build only: the in-game interface (Settings > Fantasy interface). The web page's panels, laid out like a game
// screen: a carved bar on top (title, compass and lantern, where you are), the 3D view as big as the window allows
// with the hotbar under it, a stone column on the right (map, objectives, actions, spells, the log), the party
// along the bottom. Before a game the title picture fills the window alone.
//
// The page's panels are moved, not copied (the page finds them by id), and put back when the setting is turned off.
// Their places are worked out here in pixels from the window's size: the page's CSS sizes its column from the window
// height in a way that left half the screen empty.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        readonly List<(VisualElement el, VisualElement parent, int index)> hudMoved = new List<(VisualElement, VisualElement, int)>();
        bool hudOn;
        string hudKey;
        VisualElement hudPad;
        VisualElement hudPlay, hudMap, hudQuests, hudCompass, hudActions, hudSpells, hudTools, hudLog, hudParty, hudBar, hudStage;

        void hudFrame()
        {
            bool on = settings != null && settings.fantasy;
            if (on != hudOn)
            {
                hudOn = on;
                hudKey = null;
                sidebarKey = "";   // the spells are drawn as icons in this interface, as rows in the other
                if (on) hudMount(); else hudUnmount();
            }
            // the game screen is laid out for 1600x1000 and scaled to the window, so a small window and a 4K one
            // show the same screen (the classic interface keeps the page's own pixel sizes)
            float scale = on ? Mathf.Clamp(Math.Min(Screen.width / 1600f, Screen.height / 1000f), 0.5f, 3f) : 1f;
            if (HostMain.Panel != null && Math.Abs(HostMain.Panel.scale - scale) > 0.005f) HostMain.Panel.scale = scale;
            if (!on) return;
            hudFitSpells();
            // in the pit only its floor's goal is shown; the story's objectives and the errand come back outside
            hudQuests?.EnableInClassList("in-pit", playing && engine != null && engine.uiInDungeon());
            hudCompassLamp();
            var root = Dom.document;
            float w = root.layout.width, h = root.layout.height;
            if (float.IsNaN(w) || w < 100 || h < 100) return;
            int members = engine != null ? engine.characters.Count(c => (c.flags & 1) != 0) : 0;
            string key = $"{w}x{h}|{playing}|{compactActive}|{members}";
            if (key == hudKey) return;
            hudKey = key;
            hudPlace(w, h, members);
        }

        VisualElement hudRoseEl, hudLampEl;
        string hudLampKey;
        readonly Dictionary<string, Texture2D> hudArt = new Dictionary<string, Texture2D>();
        Texture2D hudPic(string name) { if (!hudArt.TryGetValue(name, out var t)) hudArt[name] = t = Resources.Load<Texture2D>("UI/hud/" + name); return t; }

        // The compass (the way the party faces on top, the needle to the north) and the lantern (the oil left in its
        // glass, the flame while it burns), redrawn when one of them changes.
        void hudCompassLamp()
        {
            if (hudRoseEl == null || engine == null) return;
            bool hasCompass = (engine.flagsTable[31] & 0x40) != 0, hasLamp = (engine.flagsTable[31] & 0x08) != 0;
            int oil = Math.Max(0, Math.Min(100, engine.lampOilStatus));
            int level = hasLamp ? (int)Math.Ceiling(oil * 14 / 100.0) : 0;
            bool lit = hasLamp && !engine.lampSwitchedOff && oil > 0;
            string key = $"{engine.currentDirection}|{hasCompass}|{hasLamp}|{level}|{lit}|{oil}";
            if (key == hudLampKey) return;
            hudLampKey = key;
            hudRoseEl.style.backgroundImage = hudPic("compass-" + (engine.currentDirection & 3));
            hudRoseEl.style.opacity = hasCompass ? 1 : 0.3f;
            hudRoseEl.tooltip = hasCompass ? $"Facing {directions[engine.currentDirection & 3]}" : "No compass yet";
            hudLampEl.style.backgroundImage = hudPic($"lantern-{(lit ? "on" : "off")}-{level}");
            hudLampEl.style.opacity = hasLamp ? 1 : 0.3f;
            hudLampEl.tooltip = !hasLamp ? "No lantern yet"
                : $"Lantern {(engine.lampSwitchedOff ? "off" : oil <= 0 ? "out of oil" : "lit")}, oil {oil}%. Left-click: light or put out. Right-click: refill from an oil flask.";
        }

        // The spell squares: as large as lets every spell the party knows fit the Spells box without scrolling
        // (60px at most), the grid centred with the same margin all round. Run every frame: the list is rebuilt
        // when a spell is learnt or cast and the new squares come without a size.
        void hudFitSpells()
        {
            var bar = Q("#spellbar");
            if (bar == null) return;
            var spells = bar.Children().Where(c => c.ClassListContains("spell")).ToList();
            float aw = bar.layout.width, ah = bar.layout.height;
            if (spells.Count == 0 || float.IsNaN(aw) || aw < 20 || ah < 20) return;
            const float GAP = 6;
            float size = 64;
            for (; size > 24; size -= 1)
            {
                int cols = Math.Max(1, (int)((aw + GAP) / (size + GAP)));
                int rows = (spells.Count + cols - 1) / cols;
                if (rows * (size + GAP) - GAP <= ah) break;
            }
            {
                // the grid's margins, the same left and right, top and bottom (the page's wrap layout ignores centring)
                int cols = Math.Min(spells.Count, Math.Max(1, (int)((aw + GAP) / (size + GAP))));
                int rows = (spells.Count + cols - 1) / cols;
                float padX = Mathf.Floor((aw - cols * (size + GAP)) / 2), padY = Mathf.Floor((ah - rows * (size + GAP)) / 2);
                if (Math.Abs(bar.style.paddingLeft.value.value - padX) > 0.5f || Math.Abs(bar.style.paddingTop.value.value - padY) > 0.5f)
                {
                    bar.style.paddingLeft = bar.style.paddingRight = Math.Max(0, padX);
                    bar.style.paddingTop = bar.style.paddingBottom = Math.Max(0, padY);
                }
            }
            foreach (var sp in spells)
            {
                if (Math.Abs(sp.style.width.value.value - size) < 0.1f && sp.style.width.keyword == StyleKeyword.Undefined) continue;
                sp.style.width = sp.style.height = size;
                sp.style.marginLeft = sp.style.marginRight = sp.style.marginTop = sp.style.marginBottom = GAP / 2;
                var cast = sp.Q(className: "spell-cast");
                if (cast != null) cast.style.width = cast.style.height = size;
                var icon = cast?.Children().FirstOrDefault();
                if (icon != null) icon.style.width = icon.style.height = Mathf.Round(size * 0.55f);
            }
        }

        void hudMove(VisualElement el, VisualElement to)
        {
            if (el == null || to == null || el.parent == to) return;
            hudMoved.Add((el, el.parent, el.parent != null ? el.parent.IndexOf(el) : -1));
            to.Add(el);
            CssLayout.Touch(el);
        }

        void hudMount()
        {
            var left = Q(".sidebar-left");
            var right = Q(".sidebar-right");
            hudPlay = Q(".play");
            hudStage = Q(".stage-column");
            var leftPanels = left.Children().Where(c => c.ClassListContains("panel")).ToList();
            hudMap = leftPanels.FirstOrDefault();
            hudQuests = Q("#quest-panel");
            hudCompass = leftPanels.FirstOrDefault(p => p.Q(className: "nav-row") != null);
            var rightPanels = right.Children().Where(c => c.ClassListContains("panel")).ToList();
            hudActions = rightPanels.FirstOrDefault(p => p.Q(className: "pad") != null);
            hudSpells = rightPanels.FirstOrDefault(p => p.Q("#spellbar") != null);
            hudTools = rightPanels.FirstOrDefault(p => p.ClassListContains("toolbar"));
            hudLog = Q(".ui-messages");
            hudParty = Q(".ui-party");
            hudBar = Q(".ui-hotbar");
            foreach (var el in new[] { hudMap, hudQuests, hudActions, hudSpells, hudLog, hudParty, hudBar }) hudMove(el, hudPlay);
            // the compass and the lantern, pixel art in the map's top corners (the panel with the page's own stays hidden)
            var wrap = hudMap?.Q(className: "minimap-wrap");
            if (wrap != null)
            {
                if (hudRoseEl == null)
                {
                    hudRoseEl = new VisualElement { name = "hud-rose" };
                    hudRoseEl.AddToClassList("hud-rose");
                    hudLampEl = new VisualElement { name = "hud-lamp" };
                    hudLampEl.AddToClassList("hud-lamp");
                    // left click: on or off; right click: refill from an oil flask
                    hudLampEl.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (!playing || engine == null) return;
                        if (e.button == 0) { engine.toggleLantern(); lanternKey = ""; }
                        else if (e.button == 1) engine.queueAsync(() => engine.refillLantern());
                        e.StopPropagation();
                    });
                }
                wrap.Add(hudRoseEl);
                wrap.Add(hudLampEl);
                hudLampKey = null;
            }
            // the walking arrows go (the keys and a click on the map walk); Menu, Journal, Settings take their place
            hudPad = hudActions?.Q(className: "pad");
            if (hudPad != null) hudPad.style.display = DisplayStyle.None;
            hudMove(hudTools, hudActions);
            hudTools?.AddToClassList("hud-tools");
            // the imp's errand is an objective too: shown under the objectives, not the map
            var errand = Q("#errand-note");
            var qlist = Q("#quest-list");
            if (errand != null && qlist?.parent != null)
            {
                hudMoved.Add((errand, errand.parent, errand.parent.IndexOf(errand)));
                qlist.parent.Insert(qlist.parent.IndexOf(qlist) + 1, errand);
                errand.AddToClassList("hud-errand");
            }
            // the pit's floor goal and the way out: in the objectives box too (in the pit they are the objectives)
            foreach (var id in new[] { "#dungeon-out", "#dungeon-note" })
            {
                var el = Q(id);
                if (el == null || qlist?.parent == null) continue;
                hudMoved.Add((el, el.parent, el.parent.IndexOf(el)));
                qlist.parent.Insert(qlist.parent.IndexOf(qlist), el);
            }
            // the purse is shown with the pack, beside the slot count
            var credits = Q("#credits");
            var count = Q("#inventory-count");
            if (credits != null && count?.parent != null)
            {
                hudMoved.Add((credits, credits.parent, credits.parent.IndexOf(credits)));
                count.parent.Insert(count.parent.IndexOf(count), credits);
                credits.AddToClassList("hud-credits");
            }
            foreach (var el in new[] { hudMap, hudQuests, hudActions, hudSpells, hudLog, hudParty, hudBar, hudStage })
                if (el != null) { el.AddToClassList("hud-box"); el.style.position = Position.Absolute; }
            // each box by name too (tools/guide_shots.py photographs them for the guide)
            foreach (var (el, name) in new[] { (hudMap, "hud-map"), (hudQuests, "hud-quests"), (hudActions, "hud-actions"), (hudSpells, "hud-spells"), (hudLog, "hud-log"), (hudParty, "hud-party"), (hudBar, "hud-bar") })
                el?.AddToClassList(name);

            // the page's width formula for the shell (CssInfo.Calc) left an inline width behind
            Q(".shell").style.width = StyleKeyword.Null;
            CssLayout.Restyle();
        }

        void hudUnmount()
        {
            for (int i = hudMoved.Count - 1; i >= 0; i -= 1)
            {
                var (el, parent, index) = hudMoved[i];
                if (parent == null) continue;
                parent.Insert(Math.Max(0, Math.Min(index, parent.childCount)), el);
            }
            hudMoved.Clear();
            foreach (var el in new[] { hudMap, hudQuests, hudActions, hudSpells, hudTools, hudLog, hudParty, hudBar, hudStage })
            {
                if (el == null) continue;
                el.RemoveFromClassList("hud-box");
                el.style.position = StyleKeyword.Null;
                el.style.left = el.style.top = el.style.width = el.style.height = StyleKeyword.Null;
                el.style.display = StyleKeyword.Null;
            }
            hudRoseEl?.RemoveFromHierarchy();
            hudLampEl?.RemoveFromHierarchy();
            hudTools?.RemoveFromClassList("hud-tools");
            Q("#credits")?.RemoveFromClassList("hud-credits");
            hudQuests?.RemoveFromClassList("in-pit");
            if (inventoryOverlay != null)
            {
                inventoryOverlay.RemoveFromClassList("hud-inv");
                inventoryOverlay.style.left = inventoryOverlay.style.top = inventoryOverlay.style.width = inventoryOverlay.style.height = inventoryOverlay.style.bottom = StyleKeyword.Null;
            }
            var bar = Q("#spellbar");
            if (bar != null) bar.style.paddingLeft = bar.style.paddingRight = bar.style.paddingTop = bar.style.paddingBottom = StyleKeyword.Null;
            if (hudPad != null) hudPad.style.display = StyleKeyword.Null;
            Dom.document.RemoveFromClassList("hud-title");
            CssLayout.Restyle();
        }

        static void hudRect(VisualElement el, float x, float y, float w, float h)
        {
            if (el == null) return;
            el.style.display = DisplayStyle.Flex;
            el.style.left = x; el.style.top = y; el.style.width = Math.Max(0, w); el.style.height = Math.Max(0, h);
        }

        static void hudHide(VisualElement el) { if (el != null) el.style.display = DisplayStyle.None; }

        void hudPlace(float W, float H, int members)
        {
            const float G = 8;          // the gap between boxes
            const float TOP = 66;       // the carved bar (.play's top in page-hud.uss)
            // the title: the picture alone, as large as it fits
            bool title = !playing;
            Dom.document.EnableInClassList("hud-title", title);
            float aspect = compactActive ? 176f / 120f : 320f / 200f;
            if (title)
            {
                foreach (var el in new[] { hudMap, hudQuests, hudActions, hudSpells, hudLog, hudParty, hudBar }) hudHide(el);
                const float TP = 12;
                float sw = Math.Min(W - 2 * G, (H - 2 * G - 2 * TP) * aspect + 2 * TP), sh = (sw - 2 * TP) / aspect + 2 * TP;
                hudRect(hudStage, (W - sw) / 2, (H - sh) / 2, sw, sh);   // the bar is hidden: .play starts at the top
                return;
            }
            float playH = H - TOP;
            float partyH = Mathf.Clamp(playH * 0.22f, 170, 214);
            const float SLOT = 76;               // a hotbar square (the page's grid makes ten of them, square)
            float barH = SLOT + 20;
            // the view: as big as fits beside a right column of at least 420px
            const float P = 12;          // the frame around the view (.stage-column's padding)
            float viewH = playH - partyH - barH - 4 * G;
            float viewW = (viewH - 2 * P) * aspect + 2 * P;
            float colMin = Math.Min(560, Math.Max(420, W * 0.3f));
            if (viewW > W - colMin - 3 * G) { viewW = W - colMin - 3 * G; viewH = (viewW - 2 * P) / aspect + 2 * P; }
            float x0 = G, y0 = G;
            hudRect(hudStage, x0, y0, viewW, viewH);
            // the inventory is a window-level box (position: fixed): over the view, inside its frame
            var inv = inventoryOverlay;
            if (inv != null)
            {
                inv.AddToClassList("hud-inv");
                inv.style.left = x0 + P; inv.style.top = TOP + y0 + P;
                inv.style.width = viewW - 2 * P; inv.style.height = viewH - 2 * P;
                inv.style.bottom = StyleKeyword.Auto;
            }
            // ten squares under the view, centred (the grid sizes them from the bar's width)
            float barW = Math.Min(viewW, SLOT * 10 + 9 * 4 + 28);
            hudRect(hudBar, x0 + (viewW - barW) / 2, y0 + viewH + G, barW, barH);
            // the right column: map and objectives beside actions and spells, the log under them
            float cx = x0 + viewW + G, cw = W - cx - G, ch = viewH + G + barH;
            float half = (cw - G) / 2;
            // the map | actions and spells; under them the log beside the objectives
            float upperH = Math.Max(420, ch * 0.6f);
            hudRect(hudMap, cx, y0, half, upperH);
            float actionsH = 268;
            hudRect(hudActions, cx + half + G, y0, half, actionsH);
            hudRect(hudSpells, cx + half + G, y0 + actionsH + G, half, upperH - actionsH - G);
            float lowY = y0 + upperH + G, lowH = ch - upperH - G;
            float questW = Math.Min(260, cw * 0.38f);
            hudRect(hudLog, cx, lowY, cw - questW - G, lowH);
            hudRect(hudQuests, cx + cw - questW, lowY, questW, lowH);
            // the party along the bottom
            float py = y0 + viewH + G + barH + G;
            hudRect(hudParty, x0, py, W - 2 * G, partyH);
        }
    }
}
