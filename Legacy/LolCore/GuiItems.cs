// Picking things up and putting them down: the hand, the inventory row and the eleven slots on a
// character sheet.
//
// Transliterated from src/game/gui.mjs (clickedInventorySlot, clickedCharInventorySlot,
// clickedInventoryScroll) and src/game/items.mjs (setHandItem).
//
// The hand is the engine's clipboard: every exchange goes through it, and every exchange that ends
// on a character re-runs that item's script so the character's numbers follow the kit.
namespace LolCore;

public sealed partial class Gui
{
    /// <summary>itemInHand: what the pointer is carrying.</summary>
    public int ItemInHand;

    /// <summary>What the last five stats came out as, for the sheet's rolling numbers.</summary>
    private readonly int[] _charStatsTemp = new int[5];

    public void RecalcCharacterStats(int charNum)
    {
        for (int i = 0; i < 5; i += 1) _charStatsTemp[i] = CalculateCharacterStats(charNum, i);
    }

    /// <summary>
    /// setHandItem: an item taken into the hand names itself in the message line, and an item whose
    /// script says so runs it first - that is how a trap springs when it is picked up.
    /// </summary>
    public void SetHandItem(int itemIndex)
    {
        if (itemIndex != 0 && (Items.Properties[Items.InPlay[itemIndex].ItemPropertyIndex].Flags & 0x80) != 0)
        {
            _loader.RunItemScript(-1, itemIndex, 0x400, 0, 0);
            if ((Items.InPlay[itemIndex].ShpCurFrameFlg & 0x8000) != 0) itemIndex = 0;
        }
        if (itemIndex != 0 && (_loader.Flags[31] & 0x02) == 0 && (CurrentControlMode == 0 || (_loader.Text?.TextEnabled ?? true)))
        {
            string name = LangString(Items.Properties[Items.InPlay[itemIndex].ItemPropertyIndex].NameStringId);
            _loader.Text?.PrintMessage(0, LangString(0x403e).Replace("%s", name));
        }
        ItemInHand = itemIndex;
    }

    /// <summary>clickedInventorySlot: the row along the bottom swaps with the hand.</summary>
    public int ClickedInventorySlot(GuiButton b)
    {
        int slot = Items.InventoryCurItem + b.Arg;
        if (slot >= Items.Inventory.Length) slot -= Items.Inventory.Length;
        int slotItem = Items.Inventory[slot];
        int hItem = ItemInHand;
        int Prop(int i) => i > 0 && i < Items.InPlay.Length ? Items.InPlay[i].ItemPropertyIndex : -1;
        if ((Prop(hItem) == 281 || Prop(slotItem) == 281) && (Prop(hItem) == 220 || Prop(slotItem) == 220))
        {
            Items.Inventory[slot] = 0;
            DrawInventoryItem(b.Arg);
            OnSoundEffect?.Invoke(99);
            PlayWsaInBox("TRUTH.WSA", b.X, b.Y - 3, 25, 27, 25, 7);
            Items.Delete(slotItem);
            Items.Delete(hItem);
            SetHandItem(0);
            Items.Inventory[slot] = Items.MakeItem(280, 0, 0, _loader.Level);
        }
        else
        {
            SetHandItem(slotItem);
            Items.Inventory[slot] = hItem;
        }
        DrawInventoryItem(b.Arg);
        return 1;
    }

    /// <summary>
    /// playWsaInBox: a small animation shown in a box on the interface, a frame every few ticks.
    /// The cup and the ruby coming together is the one the game uses it for.
    /// </summary>
    public void PlayWsaInBox(string name, int x, int y, int w, int h, int frames, int delayTicks)
    {
        var mov = _loader.OpenWsa(name, 0);
        if (mov == null) return;
        _screen.CopyRegion(x, y, x, y, w, h, 0, 2);
        for (int i = 0; i < frames; i += 1)
        {
            _screen.CopyRegion(x, y, 0, 0, w, h, 2, 2);
            mov.DisplayFrame(i, 2, 0, 0, 0x4000);
            _screen.CopyRegion(0, 0, x, y, w, h, 2, 0);
            Wait?.Invoke(delayTicks * LevelLoader.TickLength);
        }
    }

    /// <summary>clickedInventoryScroll: the row scrolls a slot at a time.</summary>
    public int ClickedInventoryScroll(GuiButton b)
    {
        int step = b.Arg != 0 ? 1 : -1;
        Items.InventoryCurItem += step;
        if (Items.InventoryCurItem >= Items.Inventory.Length) Items.InventoryCurItem = 0;
        if (Items.InventoryCurItem < 0) Items.InventoryCurItem = Items.Inventory.Length - 1;
        DrawInventory();
        return 1;
    }

    /// <summary>
    /// clickedCharInventorySlot: the eleven slots on the sheet. A slot refuses what does not belong
    /// in it and says so; anything else is swapped with the hand, and both items' scripts run - the
    /// one being taken off as well as the one being put on.
    /// </summary>
    public int ClickedCharInventorySlot(GuiButton b)
    {
        var inventoryDesc = StaticData.Table("InventoryDesc");
        if (ItemInHand != 0)
        {
            int sl = 1 << b.Arg;
            var prop = Items.Properties[Items.InPlay[ItemInHand].ItemPropertyIndex];
            if ((sl & prop.Type) == 0)
            {
                bool f = false;
                for (int i = 0; i < 11; i += 1)
                {
                    if ((prop.Type & (1 << i)) == 0) continue;
                    string format = LangString(i > 3 ? 0x418a : 0x418b);
                    _loader.Text?.PrintMessage(0, FormatString(format, LangString(prop.NameStringId), LangString(inventoryDesc[i])));
                    f = true;
                }
                if (!f) _loader.Text?.PrintMessage(Items.InPlay[ItemInHand].ItemPropertyIndex == 231 ? 2 : 0, LangString(0x418c));
                return 1;
            }
        }
        else if (Characters[SelectedCharacter].Items[b.Arg] == 0)
        {
            _loader.Text?.PrintMessage(0, LangString(inventoryDesc[b.Arg] + 8));
            return 1;
        }

        int ih = ItemInHand;
        SetHandItem(Characters[SelectedCharacter].Items[b.Arg]);
        Characters[SelectedCharacter].Items[b.Arg] = ih;
        DrawCharInventoryItem(b.Arg);
        RecalcCharacterStats(SelectedCharacter);
        if (ItemInHand != 0) _loader.RunItemScript(SelectedCharacter, ItemInHand, 0x100, 0, 0);
        if (ih != 0) _loader.RunItemScript(SelectedCharacter, ih, 0x80, 0, 0);
        DrawCharInventoryItem(b.Arg);
        DrawCharPortraitWithStats(SelectedCharacter);
        // gui_changeCharacterStats rolls the numbers up or down a frame at a time; where it lands is
        // what the sheet shows, and that is what is drawn here.
        for (int i = 0; i < 5; i += 1) PrintCharacterStats(i, false, CalculateCharacterStats(SelectedCharacter, i));
        return 1;
    }

    private static readonly int[] DropItemDirIndex = { 0, 1, 2, 3, 1, 3, 0, 2, 3, 2, 1, 0, 2, 0, 3, 1 };

    /// <summary>
    /// clickedSceneDropItem: the four corners of the square in front of the party. The far two go
    /// into the next square along, and only if the wall between them lets an item through.
    /// </summary>
    /// <summary>
    /// uiDropToFloor: the thing in a slot (or in hand) put on the floor - ahead if there is room, at
    /// the party's feet if not, and back where it came from if there is nowhere to put it. Returns
    /// what to tell the player.
    /// </summary>
    public string DropToFloor(int slot = -1)
    {
        if ((_loader.UpdateFlags & 1) != 0 || _loader.NeedSceneRestore || _loader.SysTimerPaused) return null;
        int held = ItemInHand;
        if (slot >= 0 && Items.Inventory[slot] == 0) return null;
        int item = slot >= 0 ? Items.Inventory[slot] : ItemInHand;
        if (item == 0) return null;
        string name = _loader.ItemName(item);
        if (slot >= 0)
        {
            Items.Inventory[slot] = 0;
            if (held != 0) SetHandItem(0);   // park what was held; the slot takes it back below
            SetHandItem(item);
        }
        ClickedSceneDropItem(new GuiButton { Arg = 3 });                        // ahead, as the Drop button does
        if (ItemInHand != 0) ClickedSceneDropItem(new GuiButton { Arg = 1 });   // blocked ahead: at their feet
        if (ItemInHand != 0)
        {
            if (slot >= 0) { Items.Inventory[slot] = ItemInHand; SetHandItem(held); }
            DrawInventory();
            return "There is no room to drop that here.";
        }
        if (held != 0 && slot >= 0) SetHandItem(held);
        DrawInventory();
        return $"{name} dropped.";
    }

    public int ClickedSceneDropItem(GuiButton b)
    {
        int[] offsX = { 0x40, 0xc0, 0x40, 0xc0 };
        int[] offsY = { 0x40, 0x40, 0xc0, 0xc0 };
        if ((_loader.UpdateFlags & 1) != 0 || ItemInHand == 0) return 0;
        int block = _loader.Party.Block;
        if (b.Arg > 1)
        {
            block = Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction);
            int f = _loader.Walls.WallFlags[_loader.Map.Walls[block, _loader.Party.Direction ^ 2]];
            if ((f & 0x80) == 0 || (f & 2) != 0) return 1;
        }
        int i = DropItemDirIndex[(_loader.Party.Direction << 2) + b.Arg];
        var (x, y) = Party.CalcCoordinates(block, offsX[i], offsY[i]);
        _loader.Board.SetItemPosition(ItemInHand, x, y, 0, true);
        SetHandItem(0);
        return 1;
    }

    /// <summary>Where the pointer is, in screen pixels: a pickup reads what is under it.</summary>
    public int MouseX, MouseY;

    /// <summary>
    /// clickedScenePickupItem. The engine redraws the two reachable squares with every item as a
    /// flat colour, then looks at the pixel under the pointer (and twenty around it, in a spiral)
    /// to find out which item was clicked. Colour 1-128 is the square ahead, above that the one
    /// the party stands on.
    /// </summary>
    public int ClickedScenePickupItem(GuiButton b)
    {
        int[] checkX = { 0, 0, 1, 0, -1, -1, 1, 1, -1, 0, 2, 0, -2, -1, 1, 2, 2, 1, -1, -2, -2 };
        int[] checkY = { 0, -1, 0, 1, 0, -1, -1, 1, 1, -2, 0, 2, 0, -2, -2, -1, 1, 2, 2, 1, -1 };
        if ((_loader.UpdateFlags & 1) != 0 || ItemInHand != 0) return 0;
        int cp = _screen.CurPage;
        int scenePage = _scene.ScenePage;
        _screen.CurPage = scenePage;
        _scene.RedrawSceneItem(_loader.Party.Block, _loader.Party.Direction, _loader.Party.PosX, _loader.Party.PosY);
        var dim = _screen.Dims[b.DimTableIndex];
        int clipLeft = (dim.Sx << 3) + b.X;
        int clipTop = dim.Sy + b.Y;
        int clipRight = clipLeft + b.Width - 1;
        int clipBottom = clipTop + b.Height - 1;
        int p = 0;
        var page = _screen.Page(scenePage);
        for (int i = 0; i < checkX.Length; i += 1)
        {
            int px = Math.Max(clipLeft, Math.Min(clipRight, MouseX + checkX[i]));
            int py = Math.Max(clipTop, Math.Min(clipBottom, MouseY + checkY[i]));
            p = page[py * 320 + px];
            if (p != 0) break;
        }
        _screen.CurPage = cp;
        if (p == 0) return 0;
        int block = p <= 128 ? Party.CalcNewBlockPosition(_loader.Party.Block, _loader.Party.Direction) : _loader.Party.Block;
        int found = _loader.Board.CheckSceneForItems(block, p & 0x7f);
        if (found != -1)
        {
            _loader.Board.RemoveLevelItem(found, block);
            SetHandItem(found);
        }
        return 1;
    }

    /// <summary>formatString: the engine's own %s / %d substitution, in order.</summary>
    public static string FormatString(string format, params object[] args)
    {
        if (string.IsNullOrEmpty(format)) return "";
        var text = new System.Text.StringBuilder();
        int at = 0;
        for (int i = 0; i < format.Length; i += 1)
        {
            if (format[i] != '%' || i + 1 >= format.Length) { text.Append(format[i]); continue; }
            char kind = format[i + 1];
            if (kind == 's' || kind == 'd' || kind == 'u') { text.Append(at < args.Length ? args[at++] : ""); i += 1; }
            else if (kind == '%') { text.Append('%'); i += 1; }
            else text.Append(format[i]);
        }
        return text.ToString();
    }
}
