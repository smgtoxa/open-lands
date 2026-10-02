// What the interface can say about a monster: its name, what it hits for, what hurts it.
//
// Transliterated from the monsterName/monsterInfo half of src/game/host-ui.mjs. None of this is in
// the original game - the DOS interface never described a creature - but it is what the bestiary is
// made of, so it belongs to the engine rather than to one host's panels.
namespace LolCore;

public sealed partial class MonsterBoard
{
    /// <summary>The file a kind's shapes came from, which is the only name the data gives it.</summary>
    public readonly string[] ShapeNames = new string[64];

    /// <summary>monsterName: "SKELETON.SHP" is a Skeleton.</summary>
    public string MonsterName(Monster m)
    {
        string file = m?.Properties != null && m.Properties.ShapeIndex < ShapeNames.Length
            ? ShapeNames[m.Properties.ShapeIndex] : null;
        string basename = (file ?? "monster");
        int dot = basename.LastIndexOf(".SHP", StringComparison.OrdinalIgnoreCase);
        if (dot >= 0) basename = basename[..dot];
        basename = basename.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ToLowerInvariant();
        if (basename.Length == 0) basename = "monster";
        return char.ToUpperInvariant(basename[0]) + basename[1..];
    }

    private static readonly string[] AttackSkills = { "", "steals equipment", "poisons", "destroys an item", "steals an item" };
    private static readonly string[] DefenseSkills = { "", "grabs your weapon", "breaks your weapon", "flees from magic", "healed by fire" };

    /// <summary>
    /// The damage classes, by the index the item scripts and the spells use: 0-2 are the weapon
    /// kinds, 3 Freeze, 4 Fireball, 5 Spark and Lightning, 6 Caustic fog, 7 Hand of Fate and Mist
    /// of Doom. A protection of 256 means normal damage.
    /// </summary>
    public static readonly string[] DamageClasses =
    {
        "chopping (axes, halberds, sabres)", "blades (swords, daggers)", "blunt (maces, mauls)",
        "ice (Freeze)", "fire (Fireball)", "lightning (Spark, Lightning)", "acid (Caustic fog)",
        "magic (Hand of Fate, Mist of Doom)",
    };

    /// <summary>monsterInfo: everything the interface shows about a creature.</summary>
    public MonsterInfo Describe(Monster m, Character[] party)
    {
        if (m?.Properties == null) return null;
        var p = m.Properties;
        int partyHp = party?.Where(c => c.Active).Sum(c => c.HitPointsMax) ?? 0;
        if (partyHp == 0) partyHp = 1;
        double ratio = (double)p.HitPoints / partyHp;
        int might = p.ItemsMight.Max();

        var traits = new List<string>();
        if (p.NumDistAttacks > 0) traits.Add("ranged attacks");
        if (p.AttackSkillType > 0 && p.AttackSkillType < AttackSkills.Length && AttackSkills[p.AttackSkillType].Length != 0)
            traits.Add($"{AttackSkills[p.AttackSkillType]} ({p.AttackSkillChance}%)");
        if (p.DefenseSkillType > 0 && p.DefenseSkillType < DefenseSkills.Length && DefenseSkills[p.DefenseSkillType].Length != 0)
            traits.Add($"{DefenseSkills[p.DefenseSkillType]} ({p.DefenseSkillChance}%)");

        // Weak or resistant relative to the creature's own usual value: many take half of everything,
        // so the middle of its own table is what "normal for this one" means.
        var values = p.ProtectionAgainstItems.Select(v => (short)v).ToArray();
        var sorted = values.Where(d => d >= 0).OrderBy(d => d).ToArray();
        int basis = sorted.Length != 0 ? sorted[sorted.Length >> 1] : 256;
        if (basis == 0) basis = 256;
        var resist = new List<string>();
        var weak = new List<string>();
        var heals = new List<string>();
        for (int i = 0; i < values.Length && i < DamageClasses.Length; i += 1)
        {
            int d = values[i];
            int pct = JsMath.RoundToInt(d * 100.0 / basis);
            if (d < 0) heals.Add(DamageClasses[i]);
            else if (d < basis * 0.75) resist.Add($"{DamageClasses[i]} ({pct}% damage)");
            else if (d > basis * 1.25) weak.Add($"{DamageClasses[i]} ({pct}% damage)");
        }
        if (heals.Count != 0) traits.Add($"healed by {string.Join(", ", heals)}");

        return new MonsterInfo
        {
            Name = MonsterName(m),
            HpMax = p.HitPoints,
            Might = might,
            HitChance = Math.Min(100, JsMath.RoundToInt(p.FightingStats[0] * 100.0 / 256)),
            Evade = p.FightingStats[3],
            Protection = p.ItemProtection,
            Danger = ratio > 1.5 ? 2 : ratio > 0.6 ? 1 : 0,       // high, medium, low
            Ranged = p.NumDistAttacks > 0,
            Poison = p.AttackSkillType == 2,
            Steals = p.AttackSkillType is 1 or 4,
            Heals = heals.Count != 0,
            Traits = string.Join("; ", traits),
            Weak = string.Join("; ", weak),
            Resist = string.Join("; ", resist),
            DamageTaken = values.Length != 0 ? JsMath.RoundToInt(values[0] * 100.0 / 256) : 100,
        };
    }
}
