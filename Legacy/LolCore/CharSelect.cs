// The screen a new game starts on: the king, and the four champions to choose between.
//
// Transliterated from the character-selection half of src/main.mjs, which composes the original's
// own artwork - CHAR.CPS behind, CHARGEN.WSA for the king, and the portraits copied out of
// BACKGRND.CPS - rather than inventing a screen of its own. It is all index-space work, so it
// belongs to the engine and can be compared byte for byte.
namespace LolCore;

public sealed class CharSelect
{
    /// <summary>The champions, in the order the screen shows them.</summary>
    public sealed class Champion
    {
        public string Name = "";
        public int X;
        public int Might, Protection, Magic;
        /// <summary>The id startupNew adds to the party for this choice.</summary>
        public int Id;
    }

    public static readonly Champion[] Champions =
    {
        new() { Name = "Ak'shel", X = 96, Might = 15, Protection = 8, Magic = 5, Id = -9 },
        new() { Name = "Michael", X = 154, Might = 6, Protection = 10, Magic = 15, Id = -1 },
        new() { Name = "Kieran", X = 212, Might = 8, Protection = 6, Magic = 8, Id = -8 },
        new() { Name = "Conrad", X = 271, Might = 10, Protection = 12, Magic = 10, Id = -5 },
    };

    /// <summary>Where each portrait sits in BACKGRND.CPS: four calm faces, then the same four again
    /// looking up, which is what makes them blink at the player.</summary>
    private static readonly (int X, int Y)[] AtlasPositions =
    {
        (111, 0), (143, 0), (175, 0), (207, 0), (239, 0),
        (111, 32), (143, 32), (175, 32), (207, 32),
    };

    /// <summary>The king's frames, back and forth.</summary>
    private static readonly int[] KingFrames = { 0, 1, 2, 3, 4, 5, 4, 3, 2, 1 };

    private readonly Screen _screen;
    private byte[] _background, _atlas;
    private WsaPlayer _king;

    public CharSelect(Screen screen) => _screen = screen;

    /// <summary>CHAR.CPS, BACKGRND.CPS and CHARGEN.WSA, all out of the intro archive.</summary>
    public void Load(Resources resources)
    {
        if (resources.Exists("CHAR.CPS")) _background = resources.Get("CHAR.CPS");
        if (resources.Exists("BACKGRND.CPS")) _atlas = resources.Get("BACKGRND.CPS");
        if (resources.Exists("CHARGEN.WSA"))
        {
            _king = new WsaPlayer(_screen) { Name = "CHARGEN.WSA" };
            _king.Open(resources.Get("CHARGEN.WSA"), 0, null);
        }
    }

    /// <summary>
    /// One frame of the screen onto page 0. `step` counts the frames the host has drawn: the king
    /// walks his loop on it, and every twelfth one the champions look up.
    /// </summary>
    public void Draw(int step)
    {
        if (_background != null) _screen.LoadBitmap(_background, 0, _screen.Palette(0));
        if (_king != null) _king.DisplayFrame(KingFrames[step % KingFrames.Length], 0, 113, 0);
        if (_atlas == null) return;
        // The atlas is a bitmap rather than a page, so the portraits are copied by hand.
        var atlas = Cps.DecodeBitmapData(_atlas);
        const int atlasWidth = Screen.Width;   // a CPS is always 320 across
        var page = _screen.Page(0);
        for (int i = 0; i < Champions.Length; i += 1)
        {
            var (sx, sy) = AtlasPositions[i + (step % 12 == 0 ? 5 : 0)];
            int dx = Champions[i].X, dy = 127;
            for (int y = 0; y < 32; y += 1)
            {
                int srcRow = (sy + y) * atlasWidth;
                int dstRow = (dy + y) * Screen.Width;
                if (dy + y >= Screen.Height) break;
                for (int x = 0; x < 32; x += 1)
                {
                    if (dx + x >= Screen.Width || sx + x >= atlasWidth) continue;
                    page[dstRow + dx + x] = atlas.Data[srcRow + sx + x];
                }
            }
        }
    }

    /// <summary>The line the browser build shows under a portrait when it is picked.</summary>
    public static string Describe(int index)
    {
        var c = Champions[index];
        return $"{c.Name}: Might {c.Might}, Protection {c.Protection}, Magic {c.Magic}. Accept?";
    }
}
