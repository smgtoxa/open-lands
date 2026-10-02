// The spell table, with the port's own added spells appended to it.
//
// Transliterated from the table half of src/game/extra-spells.mjs (uiRegisterExtraSpells). The game's
// own data knows nothing about the added spells, so they take the rows after it - which means every
// reader has to be looking at the extended table, not at the one baked into the static data. They all
// are now. Reading a row that is not there returns a zero rather than throwing: the engine does the
// same, and a saved party that knows a spell this build has never heard of must still be drawable.
namespace LolCore;

public static class SpellTable
{
    private static int[] _numbers;
    private static int _base = -1;

    /// <summary>Ten numbers per spell: the name, four magic costs, four blood costs, and the flags.</summary>
    public const int Stride = 10;

    private static int[] Numbers
    {
        get
        {
            if (_numbers == null)
            {
                var source = StaticData.Table("SpellNumbers");
                _numbers = (int[])source.Clone();
                // Decided once, from the game's own table, so a spell learned in one session is the
                // same spell in the next - extra-spells.mjs keeps extraSpellBase for the same reason.
                if (_base < 0) _base = source.Length / Stride;
            }
            return _numbers;
        }
    }

    /// <summary>Where the added spells start. The game's own table ends here.</summary>
    public static int ExtraBase
    {
        get { _ = Numbers; return _base; }
    }

    /// <summary>How many spells the table can answer for.</summary>
    public static int Count => Numbers.Length / Stride;

    /// <summary>
    /// One number of one spell, or zero when the table has no row for it. Falling off the end of the
    /// table is what took the whole interface down, so it cannot be allowed to throw.
    /// </summary>
    public static int Field(int spell, int field)
    {
        if (spell < 0 || field < 0 || field >= Stride) return 0;
        int at = spell * Stride + field;
        var table = Numbers;
        return at < table.Length ? table[at] : 0;
    }

    /// <summary>Makes room up to and including this spell, so it has a row of its own.</summary>
    private static void Grow(int spell)
    {
        int wanted = (spell + 1) * Stride;
        if (wanted <= Numbers.Length) return;
        var grown = new int[wanted];
        Array.Copy(_numbers, grown, _numbers.Length);
        _numbers = grown;
    }

    /// <summary>An added spell takes its row: what it is called, what it costs, and what it does.</summary>
    public static void Write(int spell, int nameCode, int[] mp, int[] hp, int flags)
    {
        if (spell < 0) return;
        Grow(spell);
        int at = spell * Stride;
        _numbers[at] = nameCode;
        for (int i = 0; i < 4; i += 1)
        {
            _numbers[at + 1 + i] = mp != null && i < mp.Length ? mp[i] : 0;
            _numbers[at + 5 + i] = hp != null && i < hp.Length ? hp[i] : 0;
        }
        _numbers[at + 9] = flags;
    }

    /// <summary>
    /// Anything the party knows must resolve. A save from a build with different added spells would
    /// otherwise leave a hole in the table for the first reader to fall into - which is exactly what
    /// happened. The line is extra-spells.mjs's own.
    /// </summary>
    public static void EnsureResolvable(IEnumerable<int> known)
    {
        if (known == null) return;
        foreach (int spell in known)
        {
            if (spell < 0) continue;
            if ((spell + 1) * Stride <= Numbers.Length) continue;
            Write(spell, 0, null, null, 0);
        }
    }
}
