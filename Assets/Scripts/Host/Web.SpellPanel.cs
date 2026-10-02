// Unity build only: which spells each hero keeps in the Spells panel of the in-game interface (up to five, so the
// squares stay large). Kept as the spells a hero has taken off: a spell learnt later shows up on its own while
// there is room. Chosen on the character screen (CharScreen's "In the panel" toggle).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LolHost
{
    public sealed partial class Web
    {
        public const int PANEL_SPELLS = 5;
        const string HIDDEN_SPELLS_KEY = "lol.hiddenSpells";
        Dictionary<int, List<int>> hiddenSpells = new Dictionary<int, List<int>>();

        void init_B99_spellPanel()
        {
            try { hiddenSpells = JsonSerializer.Deserialize<Dictionary<int, List<int>>>(localStorage.getItem(HIDDEN_SPELLS_KEY) ?? "null", B_json) ?? new Dictionary<int, List<int>>(); }
            catch (Exception) { hiddenSpells = new Dictionary<int, List<int>>(); }
            if (charScreen != null)
            {
                charScreen.spellShown = (id, slot) => panelSpells(id).Contains(slot);
                charScreen.toggleSpellShown = (id, slot) => { var why = toggleSpellShown(id, slot); if (why != null) toast(why); };
            }
        }

        /// <summary>The spell slots shown for hero `id`: the known ones not taken off, the first five.</summary>
        List<int> panelSpells(int id)
        {
            var hidden = hiddenSpells.TryGetValue(id, out var h) ? h : null;
            var known = engine.availableSpells.Select((s, slot) => (s: (int)s, slot)).Where(e => e.s != -1).Select(e => e.slot);
            return known.Where(slot => hidden == null || !hidden.Contains(slot)).Take(PANEL_SPELLS).ToList();
        }

        /// <summary>Takes a spell off hero `id`'s panel or puts it back; the reason when it cannot (the panel is full).</summary>
        string toggleSpellShown(int id, int slot)
        {
            if (!hiddenSpells.TryGetValue(id, out var hidden)) hiddenSpells[id] = hidden = new List<int>();
            if (panelSpells(id).Contains(slot)) hidden.Add(slot);
            else
            {
                int kept = engine.availableSpells.Select((s, i) => (s: (int)s, i)).Count(e => e.s != -1 && !hidden.Contains(e.i));
                if (kept >= PANEL_SPELLS) return $"The Spells panel holds {PANEL_SPELLS} spells: take one off first.";
                hidden.Remove(slot);
            }
            try { localStorage.setItem(HIDDEN_SPELLS_KEY, JsonSerializer.Serialize(hiddenSpells, B_json)); } catch (Exception) { /* ignore */ }
            sidebarKey = "";
            return null;
        }
    }
}
