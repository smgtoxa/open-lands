// Unity build only: the Journal's Spell book tab. Every spell the party knows (never one it has not found yet):
// what it does at each power, its mana, how it aims, how it was learnt, and which heroes can cast it how strong.
// The numbers are the engine's own (Spells.cs, Magic.cs, ExtraSpells.cs; mana: the SpellProperties table).
using System;
using System.Collections.Generic;
using System.Linq;
using Lol;
using UnityEngine.UIElements;

namespace LolHost
{
    public sealed partial class Web
    {
        sealed class SpellLore
        {
            public string school, about, learn;
            public string[] power;     // what each power does
            public string[] notes;     // how it works
        }

        // the game's own spells, by spell index
        static readonly Dictionary<int, SpellLore> SPELL_LORE = new Dictionary<int, SpellLore>
        {
            [0] = new SpellLore
            {
                school = "Lightning", about = "A bolt that jumps to the nearest monster in front of the party.",
                power = new[] { "7 lightning damage", "15 lightning damage", "25 lightning damage", "60 lightning damage" },
                notes = new[] { "Hits the first monster up to four blocks ahead. A wall stops it.", "Armour does not soften it.", "Damage grows with the caster's might and the target's resistance to lightning." },
                learn = "Every new game starts with it.",
            },
            [1] = new SpellLore
            {
                school = "Healing", about = "Mends wounds.",
                power = new[] { "Heals one hero for 25", "Heals one hero for 45", "Heals the whole party fully and cures poison", "Heals the whole party fully and cures poison" },
                notes = new[] { "At power 1 and 2 the game asks whom to heal: click a party card, or press Shift and a number.", "While it waits for a target, other attacks and spells are held back. Cancel gives the mana back." },
                learn = "Dawn gives the party her spellbook with Heal in Gladstone Keep, after Scotia's attack.",
            },
            [2] = new SpellLore
            {
                school = "Ice", about = "A blast of frost at the block in front of the party.",
                power = new[] { "10 ice damage to every monster ahead", "20 ice damage to every monster ahead", "30 ice damage to every monster ahead", "4d20+10 ice damage to each monster ahead; what it kills shatters" },
                notes = new[] { "Hits every monster on the block ahead.", "In the Gorkha swamp it can freeze the water so the party can cross." },
                learn = "From a Freeze scroll in a chest in the Draracle's caves.",
            },
            [3] = new SpellLore
            {
                school = "Fire", about = "A ball of fire that flies until it meets something.",
                power = new[] { "20 fire damage", "40 fire damage", "80 fire damage", "100 fire damage" },
                notes = new[] { "Flies up to three blocks and bursts on the first block with monsters or a wall.", "Hits every monster where it bursts. Armour softens it.", "Its burst can set off things in the world, such as gas." },
                learn = "From a Fireball scroll in the Urbish mining office or the White Tower.",
            },
            [4] = new SpellLore
            {
                school = "Magic", about = "A giant hand that shoves or crushes what stands ahead.",
                power = new[] { "Pushes the monsters ahead back one block", "Pushes the monsters ahead back one block", "75 magic damage to the block ahead", "125 magic damage to the block ahead" },
                notes = new[] { "A push needs a free block behind the monsters.", "Its damage gives no experience." },
                learn = "From a Hand of Fate scroll in the city of Yvel.",
            },
            [5] = new SpellLore
            {
                school = "Magic", about = "A deadly mist that fills the block in front of the party.",
                power = new[] { "30 magic damage to every monster ahead", "70 magic damage to every monster ahead", "110 magic damage to every monster ahead", "200 magic damage to every monster ahead" },
                notes = new[] { "Hits every monster on the block ahead. Armour does not soften it." },
                learn = "From a Mist of Doom scroll in the Catwalk caverns.",
            },
            [6] = new SpellLore
            {
                school = "Lightning", about = "A storm strike on the block in front of the party.",
                power = new[] { "18 lightning damage to every monster ahead", "35 lightning damage to every monster ahead", "50 lightning damage to every monster ahead", "72 lightning damage to every monster ahead" },
                notes = new[] { "Hits every monster on the block ahead. Armour does not soften it." },
                learn = "From a Lightning scroll in Opinwood or the Urbish mines.",
            },
        };

        // the spells Open Lands adds, by id (numbers from ExtraSpells.cs)
        static readonly Dictionary<string, SpellLore> EXTRA_LORE = new Dictionary<string, SpellLore>
        {
            ["drain"] = new SpellLore
            {
                school = "Magic, new in Open Lands",
                power = new[] { "8 damage, gives back up to 10 health", "14 damage, gives back up to 18 health", "24 damage, gives back up to 28 health", "38 damage, gives back up to 40 health" },
                notes = new[] { "Hits the block ahead. A wall in the way stops the cast.", "The caster gets back 35% of the damage dealt (40% at power 3 and 4), up to the amount shown.", "With nothing to drain, nothing is gained." },
            },
            ["thorns"] = new SpellLore
            {
                school = "Nature, new in Open Lands",
                power = new[] { "Thorns for 10 seconds, 4 damage", "Thorns for 14 seconds, 7 damage", "Thorns for 18 seconds, 11 damage", "Thorns for 24 seconds, 17 damage" },
                notes = new[] { "Grows a briar on the block ahead. Monsters standing in it take damage about every three seconds.", "Casting again on the same block renews it." },
            },
            ["viper"] = new SpellLore
            {
                school = "Poison, new in Open Lands",
                power = new[] { "9 damage", "16 damage", "26 damage", "42 damage" },
                notes = new[] { "Strikes the first monster up to three blocks ahead." },
            },
            ["vortex"] = new SpellLore
            {
                school = "Air, a spell cut from the original game and restored in Open Lands",
                power = new[] { "10 damage, throws back 1 block", "18 damage, throws back 1 block", "30 damage, throws back 2 blocks", "46 damage, throws back 3 blocks" },
                notes = new[] { "Hits every monster on the block ahead. A wall in the way stops the cast.", "The survivors are hurled back a block at a time while the floor behind them is open, and reel for a moment before they can move: good for buying room in a corridor.", "The original game has its name, mana and sound, but no way to learn it was ever finished." },
            },
            ["backstab"] = new SpellLore
            {
                school = "Shadow, new in Open Lands",
                power = new[] { "A normal hit from behind", "A hit for 125%", "A hit for 150%", "A hit for double damage" },
                notes = new[] { "The party steps behind the monster ahead and turns to face it; the hero strikes and cannot miss.", "Needs open floor behind the monster." },
            },
        };

        VisualElement spellPage;
        int spellOpen = -1;

        void init_C09_spellbook()
        {
            var bar = Q("#journal-overlay .tabs");
            if (bar == null) return;
            var tab = Dom.El("button", "tab");
            tab.SetAttr("type", "button");
            tab.SetAttr("data-tab", "spellbook");
            var icon = Dom.El("svg", "");
            tab.Add(icon);
            ((SvgEl)icon).UseSymbol("#i-cast");
            tab.Append("Spells");
            // after Bestiary
            var beasts = bar.Children().FirstOrDefault(t => t.GetAttr("data-tab") == "bestiary");
            if (beasts != null) bar.Insert(bar.IndexOf(beasts) + 1, tab); else bar.Append(tab);
            spellPage = Dom.El("section", "tab-page");
            spellPage.SetAttr("data-page", "spellbook");
            spellPage.SetHidden(true);
            bar.parent.Append(spellPage);
        }

        void renderSpellBook()
        {
            if (spellPage == null || engine == null) return;
            var known = engine.availableSpells.Select(s => (int)s).Where(s => s >= 0).ToList();
            spellPage.ReplaceChildren();
            if (known.Count == 0) { spellPage.Append(Dom.El("p", "panel-note", "The party knows no spells yet. Spells are learnt from scrolls and people in the story, and from the imp in the camp.")); return; }
            if (!known.Contains(spellOpen)) spellOpen = known[0];
            var list = Dom.El("div", "sb-list");
            var page = Dom.El("div", "sb-page");
            foreach (int spell in known)
            {
                var extra = engine.extraSpellAt(spell);
                var item = Dom.El("div", "sb-item" + (spell == spellOpen ? " on" : ""));
                item.Append(SpellWidget.spellIcon(extra != null ? (object)extra.icon : spell, 22), Dom.El("span", "sb-item-name", engine.spellName(spell)));
                int s = spell;
                item.On("click", () =>
                {
                    // only the page changes (and which entry is lit): rebuilding the tab lagged and flickered
                    spellOpen = s;
                    foreach (var other in list.Children()) other.ClassToggle("on", other == item);
                    page.ReplaceChildren();
                    renderSpellPage(page, s);
                    CssLayout.Touch(page);
                    CssLayout.SetScrollTop(spellPage, 0);
                });
                list.Append(item);
            }
            spellPage.Append(Dom.El("p", "panel-note", "Only the spells your party has learnt are written here."), Dom.El("div", "sb-wrap", null, true));
            var wrap = spellPage.Q(className: "sb-wrap");
            wrap.Append(list, page);
            renderSpellPage(page, spellOpen);
            CssLayout.Touch(spellPage);
        }

        void renderSpellPage(VisualElement page, int spell)
        {
            var extra = engine.extraSpellAt(spell);
            SpellLore lore = extra != null ? (EXTRA_LORE.TryGetValue(extra.id, out var el) ? el : null) : (SPELL_LORE.TryGetValue(spell, out var ol) ? ol : null);
            var props = spell < engine.@static.SpellProperties.Length ? engine.@static.SpellProperties[spell] : null;
            int[] mp = props?.mpRequired ?? extra?.mp ?? new[] { 0, 0, 0, 0 };

            var head = Dom.El("div", "sb-head");
            head.Append(SpellWidget.spellIcon(extra != null ? (object)extra.icon : spell, 48));
            var title = Dom.El("div", "sb-title-box");
            title.Append(Dom.El("h3", "sb-title", engine.spellName(spell)));
            if (lore != null) title.Append(Dom.El("div", "sb-school", lore.school));
            head.Append(title);
            page.Append(head);
            string about = extra?.about ?? lore?.about;
            if (!string.IsNullOrEmpty(about)) page.Append(Dom.El("p", "sb-about", about));

            // power by power
            page.Append(Dom.El("h5", "sb-sub", "Power"));
            var table = Dom.El("div", "sb-table");
            var hrow = Dom.El("div", "sb-row sb-hrow");
            hrow.Append(Dom.El("div", "sb-c1", "Power"), Dom.El("div", "sb-c2", "Mana"), Dom.El("div", "sb-c3", "Effect"));
            table.Append(hrow);
            for (int p = 0; p < 4; p += 1)
            {
                var row = Dom.El("div", "sb-row");
                row.Append(Dom.El("div", "sb-c1", (p + 1).ToString()), Dom.El("div", "sb-c2", $"{mp[p]} MP"), Dom.El("div", "sb-c3", lore != null ? lore.power[p] : ""));
                table.Append(row);
            }
            page.Append(table);

            // who can cast it, how strong
            page.Append(Dom.El("h5", "sb-sub", "Your heroes"));
            for (int c = 0; c < engine.characters.Length; c += 1)
            {
                var info = engine.uiCharacterInfo(c);
                if (info == null) continue;
                int best = -1;
                for (int p = 0; p < 4; p += 1) if (mp[p] <= info.mpMax) best = p;
                page.Append(Dom.El("p", "sb-point", best < 0
                    ? $"•  {info.name} ({info.mpMax} MP at most) cannot cast it yet."
                    : $"•  {info.name} ({info.mpMax} MP at most) can cast it up to power {best + 1}."));
            }

            if (lore?.notes != null && lore.notes.Length > 0)
            {
                page.Append(Dom.El("h5", "sb-sub", "How it works"));
                foreach (var n in lore.notes) page.Append(Dom.El("p", "sb-point", "•  " + n));
            }
            page.Append(Dom.El("h5", "sb-sub", "How it is learnt"));
            page.Append(Dom.El("p", "sb-point", extra != null ? $"Bought from the imp in the camp (Spells tab) for {extra.price} crowns." : lore?.learn ?? "Found in the story."));
            page.Append(Dom.El("h5", "sb-sub", "Casting"));
            page.Append(Dom.El("p", "sb-point", "•  Every cast costs the hero its mana. Casting from the Spells panel also teaches the mage skill (more at higher power)."));
            page.Append(Dom.El("p", "sb-point", "•  Set the power with the spell's slider. If the hero is short of mana, Open Lands casts at the highest power the hero can pay for."));
        }
    }
}
