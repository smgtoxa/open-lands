// A player for the Unity build (play.sh --play FILE): plays a step list in order, in real time, through the
// same inputs a person uses - the page's buttons and keys, clicks on the game window, walking with the
// movement keys - and checks what the walkthrough says happens. It fights, equips, loots and rests at the
// camp like a player; it never heals or teleports behind the game's back. Lines of FILE (# = comment):
//   wait:S             real seconds                 key:NAME      a key to the page (as typed)
//   click:SELECTOR     a page element (waits for it) game:X:Y      a click on the game picture (320x200)
//   walk:B             walk to block B, fighting on the way, resting when hurt
//   face:D             turn to 0 N 1 E 2 S 3 W        next:N        move a scene on N times (its button, or Enter)
//   press              click the decoration on the wall ahead (lever, button, lock, niche)
//   pick               take what is drawn in the view (niches, tables)   take   the page's "Take all"
//   hold:PROP          that item from the pack into the hand            stow   the hand item into the pack
//   equip              every hero's "Equip best"     rest          make camp, sleep until mended, leave
//   fight:S            fight what comes for up to S seconds
//   expect:say:TEXT    a line with TEXT has appeared in the message log (since the start)
//   expect:flag:N  expect:level:N  expect:item:PROP  expect:hero:NAME  expect:crowns>=N
//   log                the party's state to the log   shot:NAME     a screenshot (Shots/bot/NAME.png)
//   save               the game's quicksave (F5): the next segment starts from it with Continue
// Results: "bot:" lines in the player log, "BOT FAIL <line>: why" for a failed step, "BOT DONE" at the end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lol;
using UnityEngine;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed class PlayerBot : MonoBehaviour
    {
        public Web web;
        List<string> _lines;
        int _fails;
        readonly HashSet<string> _said = new HashSet<string>();
        bool _quitWhenDone;

        public static void Attach(GameObject go, Web web)
        {
            var args = Environment.GetCommandLineArgs();
            int p = Array.IndexOf(args, "--play");
            if (p < 0 || p + 1 >= args.Length) return;
            var bot = go.AddComponent<PlayerBot>();
            bot.web = web;
            bot._lines = File.ReadAllLines(args[p + 1]).Select(l => l.Trim()).ToList();
            bot._quitWhenDone = args.Contains("--quit-when-done");
            bot.StartCoroutine(bot.Play());
            bot.StartCoroutine(bot.Listen());
        }

        LandsOfLore Engine => (LandsOfLore)typeof(Web).GetField("engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(web);
        static void Log(string s) => Debug.Log("bot: " + s);
        void Fail(int line, string why) { _fails += 1; Debug.Log($"BOT FAIL {line + 1}: {why}"); }

        IEnumerator Play()
        {
            yield return new WaitForSeconds(4);   // the page settles
            for (int i = 0; i < _lines.Count; i += 1)
            {
                string line = _lines[i];
                if (line.Length == 0 || line.StartsWith("#")) continue;
                CollectLog();
                Log($"> {line}");
                int c = line.IndexOf(':');
                string cmd = c < 0 ? line : line.Substring(0, c), arg = c < 0 ? "" : line.Substring(c + 1);
                IEnumerator step = null;
                string failure = null;
                switch (cmd)
                {
                    case "wait": step = Seconds(float.Parse(arg, CultureInfo.InvariantCulture)); break;
                    case "key": Dom.Invoke(() => web.documentKeydown(new DomEvent { type = "keydown", key = arg == "Space" ? " " : arg })); step = Seconds(0.3f); break;
                    case "click": step = ClickElement(arg, e => failure = e); break;
                    case "game": { var p = arg.Split(':').Select(int.Parse).ToArray(); step = GameClick(p[0], p[1]); break; }
                    case "walk": step = Walk(int.Parse(arg), e => failure = e); break;
                    case "face": step = Face(int.Parse(arg)); break;
                    case "next": step = Next(int.Parse(arg)); break;
                    case "press": step = Press(); break;
                    case "pick": step = Pick(); break;
                    case "take": step = ClickElement("#loot-all", e => failure = e); break;
                    case "hold": step = Hold(int.Parse(arg), e => failure = e); break;
                    case "stow": step = Stow(); break;
                    case "equip": step = EquipBest(); break;
                    case "rest": step = Rest(e => failure = e); break;
                    case "fight": step = Fight(float.Parse(arg, CultureInfo.InvariantCulture)); break;
                    case "expect": failure = Expect(arg); break;
                    case "log": LogState(); break;
                    case "save": Dom.Invoke(() => web.documentKeydown(new DomEvent { type = "keydown", key = "F5" })); step = Seconds(3); break;   // quicksave; the storage file is written every 2 s
                    case "shot": Directory.CreateDirectory("Shots/bot"); ScreenCapture.CaptureScreenshot(Path.GetFullPath($"Shots/bot/{arg}.png")); step = Seconds(0.5f); break;
                    default: failure = "unknown step"; break;
                }
                if (step != null) yield return step;
                if (failure != null) Fail(i, failure);
            }
            CollectLog();
            LogState();
            Debug.Log($"BOT DONE: {_fails} failed");
            if (_quitWhenDone) { yield return Seconds(2); Application.Quit(); }
        }

        static IEnumerator Seconds(float s) { yield return new WaitForSeconds(s); }
        IEnumerator Ticks(int n) { yield return new WaitForSeconds((float)(n * Engine.tickLength / 1000.0)); }

        // ---- what the page shows ----
        // the page keeps only its last lines: they are read as they come, not only between steps
        IEnumerator Listen()
        {
            while (true) { yield return new WaitForSeconds(0.3f); try { CollectLog(); } catch (Exception) { /* the page is being rebuilt */ } }
        }

        // the page's message log (every line the game printed or spoke), each new line logged once
        void CollectLog()
        {
            foreach (var e in Dom.QAll(Dom.document, ".ui-msg").Concat(Dom.QAll(Dom.document, ".ui-say")).Distinct())
            {
                string t = string.Concat(Enumerable.Prepend(e.Query<TextElement>().ToList(), e as TextElement).Where(x => x != null && !x.ClassListContains("inline-run")).Select(x => Dom.RawText(x))).Trim();
                if (t.Length > 0 && _said.Add(t)) Log($"said: {t}");
            }
        }

        string Expect(string what)
        {
            var e = Engine;
            CollectLog();
            if (what.StartsWith("say:")) { string t = what.Substring(4); return _said.Any(s => s.Contains(t)) ? null : $"no line \"{t}\" in the message log"; }
            if (what.StartsWith("flag:")) { int f = int.Parse(what.Substring(5)); return e.queryGameFlag(f) != 0 ? null : $"flag {f} not set"; }
            if (what.StartsWith("level:")) { int l = int.Parse(what.Substring(6)); return e.currentLevel == l ? null : $"on level {e.currentLevel}, not {l}"; }
            if (what.StartsWith("item:")) { int p = int.Parse(what.Substring(5)); return HasItem(p) ? null : $"no item of property {p}"; }
            if (what.StartsWith("hero:")) { string n = what.Substring(5); return e.characters.Any(ch => (ch.flags & 1) != 0 && ch.name == n) ? null : $"{n} is not in the party"; }
            if (what.StartsWith("crowns>=")) { int n = int.Parse(what.Substring(8)); return e.credits >= n ? null : $"{e.credits} crowns, not {n}"; }
            return "unknown expectation";
        }

        bool HasItem(int prop)
        {
            var e = Engine;
            bool Is(int it) => it != 0 && e.itemsInPlay[it].itemPropertyIndex == prop;
            return Is(e.itemInHand) || e.inventory.Any(it => Is(it)) || e.characters.Any(ch => (ch.flags & 1) != 0 && ch.items.Any(it => Is(it)));
        }

        void LogState()
        {
            var e = Engine;
            if (e == null) return;
            string heroes = string.Join(" ", e.characters.Where(ch => (ch.flags & 1) != 0).Select(ch => $"{ch.name} {ch.hitPointsCur}/{ch.hitPointsMax}hp {ch.magicPointsCur}mp{((ch.flags & 8) != 0 ? " DEAD" : "")}{((ch.flags & 0x80) != 0 ? " poisoned" : "")}"));
            Log($"level {e.currentLevel} block {e.currentBlock} dir {e.currentDirection} crowns {e.credits} | {heroes} | hand {(e.itemInHand != 0 ? e.itemName(e.itemInHand) : "-")}");
        }

        // ---- input ----
        IEnumerator ClickElement(string sel, Action<string> fail)
        {
            for (float t = 0; t < 5; t += 0.1f)
            {
                var el = Dom.Q(sel);
                if (el != null && el.panel != null && el.resolvedStyle.visibility == Visibility.Visible && el.worldBound.width > 0 && el.enabledInHierarchy)
                {
                    bool shown = true;
                    for (var p = el; p != null; p = p.parent) if (p.resolvedStyle.display == DisplayStyle.None) shown = false;
                    if (shown) { Dom.Invoke(() => el.Fire("click", new DomEvent { type = "click", target = el, currentTarget = el })); yield return Seconds(0.4f); yield break; }
                }
                yield return Seconds(0.1f);
            }
            fail($"no clickable {sel}");
        }

        // a player's click on the game picture: the page's own mousedown / mouseup handlers on the game canvas
        // (#screen), at the window position of that spot - so the page turns it into game coordinates itself.
        // (UI Toolkit's own pointer events are not used: what the real mouse does over the window would race them.)
        IEnumerator GameClick(int x, int y)
        {
            var cv = Dom.Q("#screen");
            var b = cv.worldBound;
            int w = cv is CanvasEl ce && ce.width > 0 ? ce.width : 320, h = cv is CanvasEl ch && ch.height > 0 ? ch.height : 200;
            float cx = b.xMin + (x + 0.5f) * b.width / w, cy = b.yMin + (y + 0.5f) * b.height / h;
            Dom.Invoke(() => cv.Fire("mousedown", new DomEvent { type = "mousedown", target = cv, currentTarget = cv, clientX = cx, clientY = cy, button = 0 }));
            yield return Ticks(3);
            Dom.Invoke(() => cv.Fire("mouseup", new DomEvent { type = "mouseup", target = cv, currentTarget = cv, clientX = cx, clientY = cy, button = 0 }));
            yield return Ticks(8);
        }

        IEnumerator EngineKey(string key)
        {
            Dom.Invoke(() => Engine.events.Add(new Lol.InputEvent { type = "key", key = key }));
            yield return Ticks(8);
        }

        bool Busy()
        {
            var e = Engine;
            return (e.updateFlags & 3) != 0 || e.needSceneRestore != 0 || e.weaponsDisabled;
        }

        // wait until the game takes input again; a box or scene still waiting after a second is moved on as a
        // player does (its button, else Enter), once a second
        IEnumerator Idle(float max = 8)
        {
            float waited = 0;
            for (float t = 0; t < max && Busy(); t += 0.1f)
            {
                yield return Seconds(0.1f);
                if ((waited += 0.1f) < 1) continue;
                waited = 0;
                var e = Engine;
                if (e.dialogueNumButtons != 0) yield return GameClick(e.dialogueButtonPosX[0] + ((e.dialogueButtonWidth != 0 ? e.dialogueButtonWidth : 74) >> 1), e.dialogueButtonPosY[0] + 4);
                else yield return EngineKey("Enter");
            }
        }

        // ---- scenes ----
        IEnumerator Next(int n)
        {
            for (int i = 0; i < n; i += 1)
            {
                var e = Engine;
                if (e.dialogueNumButtons != 0) yield return GameClick(e.dialogueButtonPosX[0] + ((e.dialogueButtonWidth != 0 ? e.dialogueButtonWidth : 74) >> 1), e.dialogueButtonPosY[0] + 4);
                else if (Busy() || e.activeTim.Any(t => t != null)) yield return EngineKey("Enter");
                yield return Seconds(3);
            }
        }

        IEnumerator Press()
        {
            var e = Engine;
            int block = e.calcNewBlockPosition(e.currentBlock, e.currentDirection);
            int wall = e.levelBlockProperties[block].walls[e.currentDirection ^ 2];
            for (int l = e.wllShapeMap[wall]; l > 0; l = e.levelDecorationProperties[l].next)
            {
                var p = e.levelDecorationProperties[l];
                if (p.shapeIndex[1] == 0xffff) continue;
                var shape = e.levelDecorationShapes[p.shapeIndex[1]];
                yield return GameClick(p.shapeX[1] + e.clickedShapeXOffs + (shape.width >> 1), p.shapeY[1] + e.clickedShapeYOffs + (shape.height >> 1));
                yield return Seconds(0.5f);
                yield break;
            }
            yield return GameClick(200, 60);
        }

        IEnumerator Pick()
        {
            var e = Engine;
            for (int i = 0; i < 8; i += 1)
            {
                yield return Idle();
                int[] at = null;
                Dom.Invoke(() =>
                {
                    int cp = e.screen.curPage;
                    e.screen.curPage = e.sceneDrawPage1;
                    e.redrawSceneItem();
                    for (int y = 2; y < 118 && at == null; y += 2) for (int x = 114; x < 286 && at == null; x += 2) if (e.screen.getPagePixel(e.screen.curPage, x, y) != 0) at = new[] { x, y };
                    e.screen.curPage = cp;
                });
                if (at == null) yield break;
                yield return GameClick(at[0], at[1]);
                if (e.itemInHand == 0) yield break;
                Log($"picked {e.itemName(e.itemInHand)}");
                yield return Stow();
            }
        }

        IEnumerator Hold(int prop, Action<string> fail)
        {
            var e = Engine;
            int slot = Array.FindIndex(e.inventory, it => it != 0 && e.itemsInPlay[it].itemPropertyIndex == prop);
            if (slot < 0) { fail($"no item of property {prop} in the pack"); yield break; }
            Dom.Invoke(() => e.queueAsync(async () => { await e.inventorySlotClick(slot); }));
            yield return Seconds(0.5f);
        }

        IEnumerator Stow()
        {
            var e = Engine;
            if (e.itemInHand == 0) yield break;
            int slot = Array.IndexOf(e.inventory, (ushort)0);
            if (slot < 0) { Log("pack full: the hand item stays"); yield break; }
            Dom.Invoke(() => e.queueAsync(async () => { await e.inventorySlotClick(slot); }));
            yield return Seconds(0.5f);
        }

        IEnumerator EquipBest()
        {
            foreach (var b in Dom.QAll(Dom.document, ".ui-best").ToList())
            {
                Dom.Invoke(() => b.Fire("click", new DomEvent { type = "click", target = b, currentTarget = b }));
                yield return Seconds(0.5f);
            }
        }

        // ---- moving ----
        bool Passable(int from, int dir)
        {
            var e = Engine;
            int to = e.calcNewBlockPosition(from, dir);
            int wall = e.levelBlockProperties[to].walls[dir ^ 2];
            int flags = e.wllWallFlags[wall];
            if ((flags & 1) == 0) return true;
            return (flags & 8) != 0 && e.wllShapeMap[wall] > 0;
        }

        List<int> FindPath(int from, int to)
        {
            var prev = new Dictionary<int, (int b, int d)?> { [from] = null };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int b = queue.Dequeue();
                if (b == to) break;
                for (int d = 0; d < 4; d += 1)
                {
                    if (!Passable(b, d)) continue;
                    int n = Engine.calcNewBlockPosition(b, d);
                    if (prev.ContainsKey(n)) continue;
                    prev[n] = (b, d);
                    queue.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(to)) return null;
            var dirs = new List<int>();
            for (int b = to; prev[b] != null; b = prev[b].Value.b) dirs.Insert(0, prev[b].Value.d);
            return dirs;
        }

        IEnumerator Face(int d)
        {
            for (int i = 0; i < 4 && Engine.currentDirection != d; i += 1)
            {
                yield return Idle();
                yield return EngineKey(((d - Engine.currentDirection + 4) & 3) == 3 ? "q" : "e");
                yield return Idle();
            }
        }

        IEnumerator OpenDoorAhead()
        {
            var e = Engine;
            int block = e.calcNewBlockPosition(e.currentBlock, e.currentDirection);
            int wall = e.levelBlockProperties[block].walls[e.currentDirection ^ 2];
            if ((e.wllWallFlags[wall] & 1) == 0) yield break;
            yield return Press();
            for (float t = 0; t < 4; t += 0.1f)
            {
                if ((e.wllWallFlags[e.levelBlockProperties[block].walls[e.currentDirection ^ 2]] & 1) == 0) yield break;
                yield return Seconds(0.1f);
            }
        }

        bool Hostile(Monster m, int range) => m.properties != null && m.hitPoints > 0 && m.mode != 1 && m.mode < 13 && m.block != 0 && Engine.getBlockDistance(Engine.currentBlock, m.block) <= range;
        bool Alive() => Engine.characters.Any(ch => (ch.flags & 1) != 0 && ch.hitPointsCur > 0 && (ch.flags & 8) == 0);
        bool Hurt() => Engine.characters.Any(ch => (ch.flags & 1) != 0 && (ch.flags & 8) == 0 && ch.hitPointsCur * 2 < ch.hitPointsMax);

        IEnumerator Walk(int target, Action<string> fail)
        {
            int stuck = 0;
            for (int attempt = 0; attempt < 400; attempt += 1)
            {
                var e = Engine;
                if (e.currentBlock == target) yield break;
                if (!Alive()) { fail("the party is dead"); yield break; }
                if (e.monsters.Any(m => Hostile(m, 1))) yield return Fight(120);
                if (Hurt() && !e.monsters.Any(m => Hostile(m, 3))) yield return Rest(_ => { });
                yield return Idle();
                var dirs = FindPath(e.currentBlock, target);
                if (dirs == null) { fail($"no path from {e.currentBlock} to {target}"); yield break; }
                int d = dirs[0];
                if (e.currentDirection != d) { yield return Face(d); if (e.currentDirection != d) continue; }
                yield return OpenDoorAhead();
                int before = e.currentBlock;
                yield return EngineKey("w");
                yield return Idle();
                for (float t = 0; t < 2.5f && e.currentBlock == before; t += 0.1f) yield return Seconds(0.1f);
                if (e.currentBlock == before)
                {
                    Log($"stuck at {before} facing {e.currentDirection}; flags {e.updateFlags} buttons {e.dialogueNumButtons} scene {e.needSceneRestore} weapons {e.weaponsDisabled} tim {string.Concat(e.activeTim.Select(t => t != null ? "1" : "0"))} ahead {e.calcNewBlockPosition(e.currentBlock, e.currentDirection)}");
                    if (++stuck > 8) { fail($"stuck at {before}"); yield break; }
                    yield return Seconds(0.5f);
                }
            }
            if (Engine.currentBlock != target) fail($"did not reach {target}");
        }

        // fight what stands in reach, as a player: face it, swing with whoever is ready (the page's attack)
        IEnumerator Fight(float maxSec)
        {
            float quiet = 0;
            int swings = 0;
            for (float t = 0; t < maxSec && Alive();)
            {
                var e = Engine;
                var near = e.monsters.Where(m => Hostile(m, 1)).ToList();
                if (near.Count == 0)
                {
                    if (e.monsters.Any(m => Hostile(m, 2))) quiet = 0; else if ((quiet += 0.1f) > 1.5f) break;
                    yield return Seconds(0.1f); t += 0.1f; continue;
                }
                quiet = 0;
                int d = Array.IndexOf(new[] { -32, 1, 32, -1 }, near[0].block - e.currentBlock);
                if (d >= 0 && d != e.currentDirection && (e.updateFlags & 3) == 0) { yield return EngineKey(((d - e.currentDirection + 4) & 3) == 3 ? "q" : "e"); t += 0.2f; continue; }
                if (!Enumerable.Range(0, 4).Any(c => (e.characters[c].flags & 1) != 0 && e.characters[c].hitPointsCur > 0 && e.uiCanAct(c))) { yield return Seconds(0.1f); t += 0.1f; continue; }
                Dom.Invoke(() => e.queueAsync(async () => { await e.quickAttack(); }));
                swings += 1;
                yield return Seconds(0.2f); t += 0.2f;
            }
            if (swings > 0) Log($"fought: {swings} swings; {string.Join(", ", Engine.characters.Where(ch => (ch.flags & 1) != 0).Select(ch => $"{ch.name} {ch.hitPointsCur}/{ch.hitPointsMax}"))}");
        }

        // the camp (the page's Camp button), sleeping until everyone has mended, then out
        IEnumerator Rest(Action<string> fail)
        {
            var e = Engine;
            if (e.monsters.Any(m => Hostile(m, 3))) { fail("monsters too close to camp"); yield break; }
            yield return ClickElement("#rest", _ => { });
            yield return Seconds(1.5f);
            if (!e.uiInCamp()) { Log("could not make camp here"); yield break; }
            yield return ClickElement("#sleep-8", _ => { });
            for (float t = 0; t < 30 && !e.uiCampRested(); t += 0.5f) yield return Seconds(0.5f);
            Log($"rested: {e.uiCampRested()}");
            yield return ClickElement("#rest-wake", _ => { });
            yield return Seconds(1.5f);
        }
    }
}
