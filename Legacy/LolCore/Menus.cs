// The in-game menus (GUI_LoL subset): the main menu and the death menu.
//
// Transliterated from src/game/menu.mjs. Only the drawing is here - which button the player pressed
// is the host's business - but the drawing is the part that has to match: the engine's own shaded
// boxes, its fonts and its string table, with the highlighted item in a different colour.
namespace LolCore;

public sealed partial class Gui
{
    public sealed class MenuItem
    {
        public int Id, X, Y, W, H;
        public string Key;
    }

    public sealed class Menu
    {
        public int Dim, Title;
        public MenuItem[] Items = Array.Empty<MenuItem>();
    }

    private static MenuItem Item(int id, int x, int y, int w, int h, string key = null)
        => new() { Id = id, X = x, Y = y, W = w, H = h, Key = key };

    public static readonly Menu MainMenu = new()
    {
        Dim = 9, Title = 0x4000,
        Items = new[]
        {
            Item(0x4001, 16, 23, 176, 15), Item(0x4002, 16, 40, 176, 15), Item(0x4003, 16, 57, 176, 15),
            Item(0x4004, 16, 74, 176, 15), Item(0x42d9, 16, 91, 176, 15), Item(0x4006, 16, 108, 176, 15),
            Item(0x4005, 88, 127, 104, 15, "Escape"),
        },
    };

    public static readonly Menu DeathMenu = new()
    {
        Dim = 11, Title = 0x4013,
        Items = new[] { Item(0x4006, 8, 30, 104, 15), Item(0x4001, 176, 30, 104, 15) },
    };

    /// <summary>Screen::drawShadedBox - a panel's raised edge, as the menus and the scroll draw it.</summary>
    public void DrawShadedBox(int x1, int y1, int x2, int y2, byte color1, byte color2)
    {
        _screen.FillRect(x1, y1, x2, y1 + 1, color1);
        _screen.FillRect(x2 - 1, y1, x2, y2, color1);
        _screen.DrawClippedLine(x1, y1, x1, y2, color2);
        _screen.DrawClippedLine(x1 + 1, y1 + 1, x1 + 1, y2 - 1, color2);
        _screen.DrawClippedLine(x1, y2 - 1, x2 - 1, y2 - 1, color2);
        _screen.DrawClippedLine(x1, y2, x2, y2, color2);
    }

    /// <summary>Draws a menu with one item highlighted (-1 for none).</summary>
    public void DrawMenu(Menu menu, int highlighted)
    {
        var dim = _screen.Dims[menu.Dim];
        int x = dim.Sx << 3, y = dim.Sy, w = dim.W << 3, h = dim.H;
        _screen.FillRect(x + 2, y + 2, x + w - 3, y + h - 3, 225);
        DrawShadedBox(x, y, x + w - 1, y + h - 1, 223, 227);
        string of = _screen.SetFont("9");
        _screen.PrintString(LangString(menu.Title), x + (w >> 1), y + 6, 254, 0, 9);
        for (int i = 0; i < menu.Items.Length; i += 1)
        {
            var it = menu.Items[i];
            int x1 = x + it.X, y1 = y + it.Y;
            _screen.FillRect(x1, y1, x1 + it.W - 1, y1 + it.H - 1, 225);
            DrawShadedBox(x1, y1, x1 + it.W - 1, y1 + it.H - 1, 223, 227);
            _screen.PrintString(LangString(it.Id), x1 + (it.W >> 1), y1 + 3, (byte)(i == highlighted ? 254 : 204), 0, 9);
        }
        _screen.SetFont(of);
    }

    /// <summary>Which item of a menu a click at these coordinates landed on, or -1.</summary>
    public int MenuItemAt(Menu menu, int mx, int my)
    {
        var dim = _screen.Dims[menu.Dim];
        int x = dim.Sx << 3, y = dim.Sy;
        for (int i = 0; i < menu.Items.Length; i += 1)
        {
            var it = menu.Items[i];
            if (mx >= x + it.X && mx < x + it.X + it.W && my >= y + it.Y && my < y + it.Y + it.H) return i;
        }
        return -1;
    }
}
