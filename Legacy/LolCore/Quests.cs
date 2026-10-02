// The quest log: what the party has been asked to do, and whether it is done.
//
// Transliterated from src/game/quests.mjs. The game itself keeps no quest records - objectives are
// implied by dialogue and tracked through game flags - so the table names them and says which flag,
// level, party member or item makes each one appear and each one finish. The table is data, exported
// from the JavaScript build by scripts/export_quests.mjs, so there is only ever one copy of it.
using System.Text.Json;

namespace LolCore;

public sealed class Quest
{
    public string Id = "";
    public string Title = "";
    public string Detail = "";
    public int Level;
    public bool Main;
    public int Expire;                 // 0 for none
    public JsonElement Reveal, Done;
}

public sealed class QuestLog
{
    public const string Hidden = "hidden";
    public const string Active = "active";
    public const string Done = "done";

    public static readonly Quest[] All = Load();

    private static Quest[] Load()
    {
        using var stream = typeof(QuestLog).Assembly.GetManifestResourceStream("LolCore.quests.json");
        if (stream == null) return Array.Empty<Quest>();
        using var doc = JsonDocument.Parse(stream);
        var quests = new List<Quest>();
        foreach (var q in doc.RootElement.EnumerateArray())
        {
            quests.Add(new Quest
            {
                Id = q.GetProperty("id").GetString(),
                Title = q.TryGetProperty("title", out var t) ? t.GetString() : "",
                Detail = q.TryGetProperty("detail", out var d) ? d.GetString() : "",
                Level = q.TryGetProperty("level", out var l) ? l.GetInt32() : 0,
                Main = q.TryGetProperty("main", out var m) && m.GetBoolean(),
                Expire = q.TryGetProperty("expire", out var e) ? e.GetInt32() : 0,
                Reveal = q.TryGetProperty("reveal", out var r) ? r.Clone() : default,
                Done = q.TryGetProperty("done", out var done) ? done.Clone() : default,
            });
        }
        return quests.ToArray();
    }

    /// <summary>The objectives already finished; a finished one stays finished, and saves keep it.</summary>
    public readonly HashSet<string> Finished = new();

    private readonly LevelLoader _loader;
    private readonly Gui _gui;

    public QuestLog(LevelLoader loader, Gui gui = null)
    {
        _loader = loader;
        _gui = gui;
    }

    /// <summary>evalCondition: flags, levels, who is in the party, and what they are carrying.</summary>
    public bool Eval(JsonElement cond)
    {
        if (cond.ValueKind != JsonValueKind.Object) return false;
        if (cond.TryGetProperty("never", out _)) return false;
        if (cond.TryGetProperty("all", out var all))
        {
            foreach (var c in all.EnumerateArray()) if (!Eval(c)) return false;
            return true;
        }
        if (cond.TryGetProperty("any", out var any))
        {
            foreach (var c in any.EnumerateArray()) if (Eval(c)) return true;
            return false;
        }
        if (cond.TryGetProperty("flag", out var flag))
        {
            int id = flag.GetInt32();
            return ((_loader.Flags[(id >> 3) & 0xff] >> (id & 7)) & 1) != 0;
        }
        if (cond.TryGetProperty("level", out var level)) return _loader.Level == level.GetInt32();
        if (cond.TryGetProperty("visited", out var visited))
        {
            int n = visited.GetInt32();
            return _loader.Level == n || (_loader.HasTempDataFlags & (1 << (n - 1))) != 0;
        }
        if (cond.TryGetProperty("party", out var party))
        {
            int id = party.GetInt32();
            foreach (var c in _loader.Characters) if (c.Active && c.Id == id) return true;
            return false;
        }
        if (cond.TryGetProperty("item", out var item))
        {
            int prop = item.GetInt32();
            bool Has(int i) => i != 0 && i < _loader.Items.InPlay.Length && _loader.Items.InPlay[i].ItemPropertyIndex == prop;
            foreach (int i in _loader.Items.Inventory) if (Has(i)) return true;
            foreach (var c in _loader.Characters)
            {
                if (!c.Active) continue;
                foreach (int i in c.Items) if (Has(i)) return true;
            }
            return Has(_gui?.ItemInHand ?? 0);
        }
        return false;
    }

    /// <summary>
    /// questStates: "hidden", "active" or "done" for every objective. An objective whose story has
    /// moved on (the party is past the level it expires at) counts as done whatever the flags say.
    /// </summary>
    public Dictionary<string, string> States()
    {
        var states = new Dictionary<string, string>();
        foreach (var q in All)
        {
            string state = Finished.Contains(q.Id) || Eval(q.Done) || (q.Expire != 0 && VisitedBeyond(q.Expire))
                ? Done
                : Eval(q.Reveal) ? Active : Hidden;
            if (state == Done) Finished.Add(q.Id);
            states[q.Id] = state;
        }
        return states;
    }

    private bool VisitedBeyond(int level)
    {
        if (_loader.Level >= level) return true;
        for (int l = level; l <= 29; l += 1) if ((_loader.HasTempDataFlags & (1 << (l - 1))) != 0) return true;
        return false;
    }
}
