namespace LolCore;

/// <summary>
/// The page's inventory window talks to the engine through these, as the browser build does
/// (gui.mjs inventorySlotClick, host-ui.mjs uiUseInventorySlot / uiStashHand): any slot of the
/// bag, not only the nine the playfield's row shows.
/// </summary>
public sealed partial class Gui
{
    /// <summary>inventorySlotClick: the hand and a bag slot change places (bezel cup + ruby combine).</summary>
    public int InventorySlotClick(int slot)
    {
        if (slot < 0 || slot >= Items.Inventory.Length) return 0;
        int slotItem = Items.Inventory[slot];
        int hItem = ItemInHand;
        int Prop(int i) => i > 0 && i < Items.InPlay.Length && Items.InPlay[i] != null ? Items.InPlay[i].ItemPropertyIndex : -1;
        if ((Prop(hItem) == 281 || Prop(slotItem) == 281) && (Prop(hItem) == 220 || Prop(slotItem) == 220))
        {
            Items.Inventory[slot] = 0;
            OnSoundEffect?.Invoke(99);
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
        DrawInventory();
        return 1;
    }

    /// <summary>uiUseInventorySlot: a bag item used on a hero, as if taken into the hand and dropped on them.</summary>
    public int UseInventorySlot(int slot, int c)
    {
        if ((_loader.UpdateFlags & 3) != 0 || _loader.NeedSceneRestore || _loader.SysTimerPaused || WeaponsDisabled) return 0;
        if (slot < 0 || slot >= Items.Inventory.Length || Items.Inventory[slot] == 0 || ItemInHand != 0) return 0;
        int item = Items.Inventory[slot];
        Items.Inventory[slot] = 0;
        SetHandItem(item);
        ClickedPortraitEtcRight(new GuiButton { Arg = c });
        if (c == SelectionPinned) SelectionPinned = -1;
        if (ItemInHand == item)
        {
            // Not consumed: it goes back where it was.
            Items.Inventory[slot] = item;
            SetHandItem(0);
        }
        DrawInventory();
        return 1;
    }

    /// <summary>uiStashHand: the item in hand into the first free bag slot.</summary>
    public string StashHand()
    {
        if (ItemInHand == 0) return null;
        int slot = System.Array.IndexOf(Items.Inventory, 0);
        if (slot < 0) return "Your inventory is full.";
        InventorySlotClick(slot);
        return null;
    }
}
