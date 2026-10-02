// Unity build only: the Journal's Statistics tab as a board (the page shows a two-column table): the party, the
// run's numbers as tiles, progress bars, the most slain creatures and the spells cast.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lol;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        static string Thousands(double v) => Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture);

        void renderStatsBoard(VisualElement box)
        {
            var board = Dom.El("div", "st-board");

            // the party and where it stands
            var head = Dom.El("div", "st-head");
            var party = Dom.El("div", "st-party");
            for (int c = 0; engine != null && c < engine.characters.Length; c += 1)
            {
                var info = engine.uiCharacterInfo(c);
                if (info == null) continue;
                var hero = Dom.El("div", "st-hero");
                var shape = engine.characterFaceShapes[c] != null ? engine.characterFaceShapes[c][0] : null;
                if (shape != null)
                {
                    var face = (CanvasEl)Dom.El("canvas");
                    face.SetClassName("st-face");
                    face.width = shape.width; face.height = shape.height;
                    drawIconInto(face, shape, engine.uiPalette());
                    hero.Append(face);
                }
                var who = Dom.El("div", "st-hero-text");
                who.Append(Dom.El("div", "st-hero-name", info.name),
                    Dom.El("div", "st-hero-sub", $"Fighter {info.skills.fighter.level} · Rogue {info.skills.rogue.level} · Mage {info.skills.mage.level}"));
                hero.Append(who);
                party.Append(hero);
            }
            var when = Dom.El("div", "st-when");
            double h = Math.Floor(stats.seconds / 3600), min = Math.Floor(stats.seconds % 3600 / 60);
            when.Append(Dom.El("div", "st-big", $"{h:0}h {min:00}m"), Dom.El("div", "st-label", "time on the road"));
            if (engine != null) when.Append(Dom.El("div", "st-where", engine.levelName(engine.currentLevel)));
            head.Append(party, when);
            board.Append(head);

            // the numbers
            var tiles = Dom.El("div", "st-tiles");
            void tile(string icon, double value, string label, string text = null)
            {
                var t = Dom.El("div", "st-tile");
                var ic = achIcon(icon, 22); ic.AddToClassList("st-icon");
                var words = Dom.El("div", "st-tile-text");
                words.Append(Dom.El("div", "st-value", text ?? Thousands(value)), Dom.El("div", "st-label", label));
                t.Append(ic, words);
                tiles.Append(t);
            }
            tile("skull", stats.kills, "monsters slain");
            tile("sword", stats.damageDealt, "damage dealt");
            tile("shield", stats.damageTaken, "damage taken");
            tile("burst", stats["maxHit"], "biggest hit");
            tile("boot", stats["steps"], "steps walked");
            tile("wand", stats["spells"], "spells cast");
            tile("bag", stats["loot"], "items picked up");
            tile("coin", stats["trades"], "trades");
            tile("bed", stats["rests"], "rests");
            tile("heart", stats["closeCalls"], "close calls");
            tile("cross", stats["deaths"], "defeats");
            tile("eye", stats.bestiary.Count, "kinds of creature met");
            board.Append(Dom.El("h3", "st-title", "The run in numbers"), tiles);

            // progress
            var bars = Dom.El("div", "st-bars");
            void bar(string icon, int have, int of, string label)
            {
                var row = Dom.El("div", "st-bar-row");
                var ic = achIcon(icon, 16); ic.AddToClassList("st-icon");
                var track = Dom.El("div", "st-track");
                var fill = Dom.El("div", "st-fill");
                fill.style.width = new Length(of > 0 ? Math.Min(100f, 100f * have / of) : 0, LengthUnit.Percent);
                track.Append(fill);
                row.Append(ic, Dom.El("div", "st-bar-label", label), track, Dom.El("div", "st-bar-value", $"{have} / {of}"));
                bars.Append(row);
            }
            bar("map", visited.Count, 29, "Levels visited");
            if (engine != null)
            {
                var states = LandsOfLore.questStates(engine);
                bar("scroll", LandsOfLore.QUESTS.Count(q => states.TryGetValue(q.id, out var st) && st == "done"), LandsOfLore.QUESTS.Length, "Objectives done");
            }
            var ach = achievementList();
            bar("trophy", ach.Count(a => a.unlocked), ach.Count, "Achievements");
            board.Append(Dom.El("h3", "st-title", "Progress"), bars);

            // the most slain creatures
            var top = stats.bestiary.Where(kv => kv.Value != null && kv.Value.kills > 0).OrderByDescending(kv => kv.Value.kills).Take(6).ToList();
            if (top.Count > 0)
            {
                var list = Dom.El("div", "st-bars");
                int most = top[0].Value.kills;
                foreach (var kv in top)
                {
                    var row = Dom.El("div", "st-bar-row st-beast");
                    var pic = Dom.El("img", "st-thumb");
                    if (stats.notes.TryGetValue(kv.Key, out var n) && n != null && !string.IsNullOrEmpty(n.thumb)) setImageSrc(pic, n.thumb);
                    var track = Dom.El("div", "st-track");
                    var fill = Dom.El("div", "st-fill st-red");
                    fill.style.width = new Length(100f * kv.Value.kills / most, LengthUnit.Percent);
                    track.Append(fill);
                    row.Append(pic, Dom.El("div", "st-bar-label", kv.Key), track, Dom.El("div", "st-bar-value", Thousands(kv.Value.kills)));
                    list.Append(row);
                }
                board.Append(Dom.El("h3", "st-title", "Most slain"), list);
            }

            // spells by school
            var schools = new[] { ("bolt", "spellBolt", "Lightning"), ("flame", "spellFire", "Fire"), ("snow", "spellIce", "Ice"), ("heart", "spellHeal", "Healing") };
            double castMost = schools.Max(s => stats[s.Item2]);
            if (castMost > 0)
            {
                var list = Dom.El("div", "st-bars");
                foreach (var (icon, key, label) in schools)
                {
                    var row = Dom.El("div", "st-bar-row");
                    var ic = achIcon(icon, 16); ic.AddToClassList("st-icon");
                    var track = Dom.El("div", "st-track");
                    var fill = Dom.El("div", "st-fill st-blue");
                    fill.style.width = new Length((float)(100 * stats[key] / castMost), LengthUnit.Percent);
                    track.Append(fill);
                    row.Append(ic, Dom.El("div", "st-bar-label", label), track, Dom.El("div", "st-bar-value", Thousands(stats[key])));
                    list.Append(row);
                }
                board.Append(Dom.El("h3", "st-title", "Spells cast"), list);
            }
            box.ReplaceChildren(board);
        }
    }
}
