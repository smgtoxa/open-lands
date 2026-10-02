// What the game remembers about the player rather than about the world.
//
// Transliterated from src/game/meta.mjs: the counters, the bestiary, the item database, the places
// visited, the notes left on the map, the people spoken to, and the thirty-six achievements those
// add up to. The rules live in the engine so that every build earns the same ones from the same
// play; storing the state between sessions and drawing the panels stay the host's business.
namespace LolCore;

public sealed class MetaStats
{
    public int Kills, DamageDealt, DamageTaken, Seconds, MaxKind, MaxHit, CloseCalls, Deaths;
    public int Spells, SpellHeal, SpellFire, SpellBolt, SpellIce, Power4, Steps, Loot, Trades;
    public int Rests, Saves, Photos, PitRuns, Finished;

    /// <summary>The counters by the names the browser build's storage uses.</summary>
    public int this[string key]
    {
        get => key switch
        {
            "kills" => Kills, "damageDealt" => DamageDealt, "damageTaken" => DamageTaken,
            "seconds" => Seconds, "maxKind" => MaxKind, "maxHit" => MaxHit, "closeCalls" => CloseCalls,
            "deaths" => Deaths, "spells" => Spells, "spellHeal" => SpellHeal, "spellFire" => SpellFire,
            "spellBolt" => SpellBolt, "spellIce" => SpellIce, "power4" => Power4, "steps" => Steps,
            "loot" => Loot, "trades" => Trades, "rests" => Rests, "saves" => Saves, "photos" => Photos,
            "pitRuns" => PitRuns, "finished" => Finished,
            _ => 0,
        };
        set
        {
            switch (key)
            {
                case "kills": Kills = value; break;
                case "damageDealt": DamageDealt = value; break;
                case "damageTaken": DamageTaken = value; break;
                case "seconds": Seconds = value; break;
                case "maxKind": MaxKind = value; break;
                case "maxHit": MaxHit = value; break;
                case "closeCalls": CloseCalls = value; break;
                case "deaths": Deaths = value; break;
                case "spells": Spells = value; break;
                case "spellHeal": SpellHeal = value; break;
                case "spellFire": SpellFire = value; break;
                case "spellBolt": SpellBolt = value; break;
                case "spellIce": SpellIce = value; break;
                case "power4": Power4 = value; break;
                case "steps": Steps = value; break;
                case "loot": Loot = value; break;
                case "trades": Trades = value; break;
                case "rests": Rests = value; break;
                case "saves": Saves = value; break;
                case "photos": Photos = value; break;
                case "pitRuns": PitRuns = value; break;
                case "finished": Finished = value; break;
            }
        }
    }

    public static readonly string[] Keys =
    {
        "kills", "damageDealt", "damageTaken", "seconds", "maxKind", "maxHit", "closeCalls", "deaths",
        "spells", "spellHeal", "spellFire", "spellBolt", "spellIce", "power4", "steps", "loot", "trades",
        "rests", "saves", "photos", "pitRuns", "finished",
    };
}

/// <summary>What has been learned about one kind of monster.</summary>
public sealed class BestiaryEntry
{
    public int Kills, HpMax, Level;
    public readonly List<int> Levels = new();
    public int Might, HitChance, Evade, Protection, Danger, DamageTaken;
    public bool Ranged, Poison, Steals, Heals;
    public string Traits = "", Weak = "", Resist = "";
}

/// <summary>What is known about one kind of item.</summary>
public sealed class ItemDbEntry
{
    public string Name = "";
    public int Might, Protection, Level, Block;
    public bool Usable;
    public int[] Slots = Array.Empty<int>();
}

public sealed class MetaNote
{
    public int Block;
    public string Text = "";
}

public sealed class Achievement
{
    public string Name = "", Description = "", Icon = "";
    public Func<Meta, bool> Test = _ => false;
}

public sealed class Meta
{
    public readonly MetaStats Stats = new();
    public readonly Dictionary<string, BestiaryEntry> Bestiary = new(StringComparer.Ordinal);
    /// <summary>
    /// Where the party were on each level they have walked, and what that level's monsters are worth.
    /// The pit orders its floors by that last number, so a level recorded without it is a floor the
    /// imp cannot rank - and a list with nothing in it is a pit with nowhere to send anyone.
    /// </summary>
    public readonly Dictionary<int, (int Block, int Direction, int Danger)> Visited = new();
    public readonly Dictionary<int, ItemDbEntry> ItemDb = new();
    public readonly Dictionary<int, List<MetaNote>> Notes = new();
    public readonly Dictionary<string, int> Npcs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The achievements earned, in the order they were earned.</summary>
    public readonly List<string> Unlocked = new();

    /// <summary>What the tests read out of the game itself; the host wires these to the loader.</summary>
    public Func<int> KnownSpells = () => 0;
    public Func<int> Credits = () => 0;
    public Func<int> HeroCount = () => 0;
    public Func<int> BestSkill = () => 0;
    public Func<int> QuestsDone = () => 0;

    /// <summary>
    /// Everything back to nothing, for a game begun afresh. A new game keeps none of the last one's
    /// journal: not its kills, not the beasts it met, not the people it spoke to.
    /// </summary>
    public void Clear()
    {
        foreach (string key in MetaStats.Keys) Stats[key] = 0;
        Bestiary.Clear();
        Visited.Clear();
        ItemDb.Clear();
        Notes.Clear();
        Npcs.Clear();
        Unlocked.Clear();
    }

    public int VisitedCount() => Visited.Count;
    public int NoteCount() => Notes.Values.Sum(list => list.Count);
    public int NpcCount() => Npcs.Count;
    public int KindsKilled() => Bestiary.Values.Count(b => b.Kills > 0);
    public int ItemKinds() => ItemDb.Count;

    /// <summary>How many of the first `limit` achievements are earned - what the last one counts.</summary>
    public int UnlockedCount(int limit)
    {
        int n = 0;
        for (int i = 0; i < limit && i < Achievements.Length; i += 1)
        {
            try { if (Achievements[i].Test(this)) n += 1; }
            catch { /* a test that cannot run has not been earned */ }
        }
        return n;
    }

    private static Achievement A(string name, string description, Func<Meta, bool> test, string icon)
        => new() { Name = name, Description = description, Test = test, Icon = icon };

    public static readonly Achievement[] Achievements =
    {
        A("First blood", "Slay a monster", m => m.Stats.Kills >= 1, "skull"),
        A("Slayer", "Slay 250 monsters", m => m.Stats.Kills >= 250, "sword"),
        A("Exterminator", "Slay 1,000 monsters", m => m.Stats.Kills >= 1000, "axe"),
        A("Nemesis", "Slay 50 of one kind", m => m.Stats.MaxKind >= 50, "target"),
        A("Juggernaut", "Deal 25,000 damage", m => m.Stats.DamageDealt >= 25000, "hammer"),
        A("Overkill", "Deal 150 damage in one hit", m => m.Stats.MaxHit >= 150, "burst"),
        A("Iron skin", "Take 10,000 damage", m => m.Stats.DamageTaken >= 10000, "shield"),
        A("Nine lives", "Survive 9 close calls (5 hit points or less)", m => m.Stats.CloseCalls >= 9, "heart"),
        A("Phoenix", "Come back from 10 defeats", m => m.Stats.Deaths >= 10, "flame"),
        A("Apprentice", "Cast a spell", m => m.Stats.Spells >= 1, "wand"),
        A("Archmage", "Cast 500 spells", m => m.Stats.Spells >= 500, "hat"),
        A("Healer", "Cast Heal 25 times", m => m.Stats.SpellHeal >= 25, "cross"),
        A("Pyromancer", "Cast Fireball 50 times", m => m.Stats.SpellFire >= 50, "sun"),
        A("Storm caller", "Cast Spark or Lightning 50 times", m => m.Stats.SpellBolt >= 50, "bolt"),
        A("Frostbite", "Cast Freeze 25 times", m => m.Stats.SpellIce >= 25, "snow"),
        A("Full power", "Cast a spell at power 4", m => m.Stats.Power4 >= 1, "star"),
        A("Grimoire", "Know 8 spells", m => m.KnownSpells() >= 8, "book"),
        A("Explorer", "Visit 5 levels", m => m.VisitedCount() >= 5, "compass"),
        A("Cartographer", "Visit 25 levels", m => m.VisitedCount() >= 25, "map"),
        A("Marathon", "Walk 25,000 steps", m => m.Stats.Steps >= 25000, "boot"),
        A("Scribe", "Leave 25 map notes", m => m.NoteCount() >= 25, "note"),
        A("Diplomat", "Talk to 20 people", m => m.NpcCount() >= 20, "talk"),
        A("Zoologist", "Slay 40 kinds of monster", m => m.KindsKilled() >= 40, "eye"),
        A("Hoarder", "See 75 kinds of item", m => m.ItemKinds() >= 75, "bag"),
        A("Magpie", "Pick up 500 items from the floor", m => m.Stats.Loot >= 500, "hand"),
        A("Wealthy", "Hold 2,500 credits", m => m.Credits() >= 2500, "coin"),
        A("Merchant", "Trade 10 times", m => m.Stats.Trades >= 10, "chest"),
        // Three is the whole party: adding a character refuses a fourth, exactly as the original does.
        A("Full house", "Have three heroes in the party", m => m.HeroCount() >= 3, "crown"),
        A("Master", "A hero reaches level 8 in any skill", m => m.BestSkill() >= 8, "gem"),
        A("Sleepyhead", "Rest 50 times", m => m.Stats.Rests >= 50, "bed"),
        A("Dedicated", "Play for 25 hours", m => m.Stats.Seconds >= 90000, "hourglass"),
        A("Cautious", "Save 50 times", m => m.Stats.Saves >= 50, "save"),
        A("Shutterbug", "Take 10 photos", m => m.Stats.Photos >= 10, "camera"),
        A("Storyteller", "Complete 15 objectives", m => m.QuestsDone() >= 15, "scroll"),
        A("Throne of Chaos", "Finish the game", m => m.Stats.Finished >= 1, "trophy"),
        // The last one counts the others, so it never counts itself.
        A("Achievement hunter", "Unlock 20 achievements", m => m.UnlockedCount(35) >= 20, "flag"),
    };

    public List<string> Bump(string key, int n = 1)
    {
        Stats[key] = Stats[key] + n;
        return CheckAchievements();
    }

    public List<string> Peak(string key, int value)
    {
        if (value <= Stats[key]) return new List<string>();
        Stats[key] = value;
        return CheckAchievements();
    }

    /// <summary>What the party has learned about a kind of monster by meeting it.</summary>
    public BestiaryEntry BestiaryMeet(string name, int level, MonsterInfo info = null)
    {
        if (!Bestiary.TryGetValue(name, out var entry))
        {
            entry = new BestiaryEntry { Level = level };
            Bestiary[name] = entry;
        }
        if (!entry.Levels.Contains(level)) entry.Levels.Add(level);
        if (info != null)
        {
            entry.HpMax = Math.Max(entry.HpMax, info.HpMax);
            entry.Might = info.Might;
            entry.HitChance = info.HitChance;
            entry.Evade = info.Evade;
            entry.Protection = info.Protection;
            entry.Ranged = info.Ranged;
            entry.Poison = info.Poison;
            entry.Steals = info.Steals;
            entry.Heals = info.Heals;
            entry.Danger = info.Danger;
            entry.DamageTaken = info.DamageTaken;
            entry.Traits = info.Traits ?? entry.Traits;
            entry.Weak = info.Weak ?? entry.Weak;
            entry.Resist = info.Resist ?? entry.Resist;
        }
        return entry;
    }

    public List<string> Killed(string name, int level, MonsterInfo info = null)
    {
        var entry = BestiaryMeet(name, level, info);
        entry.Kills += 1;
        Stats.Kills += 1;
        Peak("maxKind", entry.Kills);
        return CheckAchievements();
    }

    public bool SeeItem(int property, ItemDbEntry entry)
    {
        if (ItemDb.ContainsKey(property)) return false;
        ItemDb[property] = entry;
        CheckAchievements();
        return true;
    }

    public void RememberVisit(int level, int block, int direction, int danger = 0)
    {
        // The worst this level has been seen to hold, never less than it was.
        if (Visited.TryGetValue(level, out var had) && had.Danger > danger) danger = had.Danger;
        Visited[level] = (block, direction, danger);
        CheckAchievements();
    }

    public List<MetaNote> NoteAdd(int level, int block, string text)
    {
        if (!Notes.TryGetValue(level, out var list)) Notes[level] = list = new List<MetaNote>();
        list.Add(new MetaNote { Block = block, Text = text });
        CheckAchievements();
        return list;
    }

    public int NpcMet(string file)
    {
        Npcs[file] = Npcs.GetValueOrDefault(file) + 1;
        CheckAchievements();
        return Npcs[file];
    }

    /// <summary>Which achievements have just been earned; the full list is kept in Unlocked.</summary>
    public List<string> CheckAchievements()
    {
        var fresh = new List<string>();
        foreach (var achievement in Achievements)
        {
            if (Unlocked.Contains(achievement.Name)) continue;
            bool earned;
            try { earned = achievement.Test(this); }
            catch { earned = false; }
            if (!earned) continue;
            Unlocked.Add(achievement.Name);
            fresh.Add(achievement.Name);
        }
        return fresh;
    }

    /// <summary>The whole list, for the panel that shows it.</summary>
    public IEnumerable<(string Name, string Description, string Icon, bool Unlocked)> All()
    {
        foreach (var a in Achievements)
        {
            bool earned = Unlocked.Contains(a.Name);
            if (!earned)
            {
                try { earned = a.Test(this); }
                catch { earned = false; }
            }
            yield return (a.Name, a.Description, a.Icon, earned);
        }
    }
}

/// <summary>What the engine can say about a monster, as the bestiary records it.</summary>
public sealed class MonsterInfo
{
    public string Name = "";
    public int HpMax, Might, HitChance, Evade, Protection, Danger, DamageTaken;
    public bool Ranged, Poison, Steals, Heals;
    public string Traits, Weak, Resist;
}
