// Items: the 400 slots the game keeps for everything that is not a monster, the property table the
// ONETIME.INF script fills in, and the block chains items share with monsters.
//
// Transliterated from src/game/items.mjs (initItems, makeItem, placeMoveLevelItem, setItemPosition,
// assignBlockItem/assignItemToBlock, addLevelItems, deleteItem, isItemMoveable).
//
// An item in a block is not simply "in a list": monsters come first in the chain and items after
// them, which is what the two insert functions differ about, and the draw order is a second chain
// sorted by distance. Both are reproduced here because the scene pass walks them.
namespace LolCore;

/// <summary>What the scene pass needs of anything standing in a block.</summary>
public interface IBlockObject
{
    int NextAssignedObject { get; set; }
    int NextDrawObject { get; set; }
    int X { get; }
    int Y { get; }
    int FlyingHeight { get; }
}

public sealed class ItemProperty
{
    public int NameStringId;
    public int ShpIndex;
    public int Flags;
    public int Type;
    public int ItemScriptFunc;
    public int Might;
    public int Skill;
    public int Protection;
    public int UnkB;
}

public sealed class Item : IBlockObject
{
    public int NextAssignedObject { get; set; }
    public int NextDrawObject { get; set; }
    public int FlyingHeight { get; set; }
    public int Block;
    public int X { get; set; }
    public int Y { get; set; }
    public int Level;
    public int ItemPropertyIndex;
    public int ShpCurFrameFlg;
    /// <summary>How worn a weapon is - a port addition, and the chest has to hand it back.</summary>
    public int Wear;

    int IBlockObject.X => X;
    int IBlockObject.Y => Y;
    int IBlockObject.FlyingHeight => FlyingHeight;
}

public sealed class ItemBoard
{
    public readonly Item[] InPlay = new Item[400];
    public ItemProperty[] Properties = Array.Empty<ItemProperty>();

    /// <summary>
    /// Every record still referenced by somebody: the party's hands, packs and worn gear, a
    /// monster's load, and whatever hangs on a block of the level that is loaded. The loader fills
    /// this in because an ItemBoard cannot see any of that itself.
    /// </summary>
    public Func<HashSet<int>> RecordsInUse;

    /// <summary>
    /// Last resort when the table is full and nothing on another level can be taken: a record that
    /// belongs to no level, sits on no block and is carried by nobody is rubbish, and the Imp's Pit
    /// makes them by the dozen - a floor is built and thrown away. They fall outside the 1..29 range
    /// the reclaim scan looks at, so without this the table could only ever fill up, and the next
    /// level to load would die with "Out of item slots".
    /// </summary>
    private int ReclaimOrphan()
    {
        var used = RecordsInUse?.Invoke() ?? new HashSet<int>(Inventory);
        for (int j = 1; j < 400; j += 1)
        {
            var it = InPlay[j];
            if (it == null || it.ItemPropertyIndex == 0 || used.Contains(j)) continue;
            if (it.Level >= 1 && it.Level <= 29) continue;   // still belongs to a level
            if (it.Block != 0) continue;                     // still lying somewhere
            Delete(j);
            return j;
        }
        return 400;
    }

    /// <summary>
    /// The nine-slot row along the bottom of the screen, and the 48 places behind it the row scrolls
    /// through. addItemToInventory scrolls the row so a new item lands in view.
    /// </summary>
    public readonly int[] Inventory = new int[48];
    public int InventoryCurItem;

    /// <summary>The money box: what the party carries and the five stacks it is drawn as.</summary>
    public int Credits;
    public readonly int[] MoneyColumnHeight = new int[5];

    /// <summary>addItemToInventory: the first free slot from the row onwards, scrolling to reach it.</summary>
    public bool AddToInventory(int itemIndex)
    {
        int pos = 0, i = 0;
        for (; i < Inventory.Length; i += 1)
        {
            pos = InventoryCurItem + i;
            if (pos >= Inventory.Length) pos -= Inventory.Length;
            if (Inventory[pos] == 0) break;
        }
        if (i == Inventory.Length) return false;
        while (InventoryCurItem > pos || InventoryCurItem + 9 <= pos)
            if (++InventoryCurItem >= Inventory.Length) InventoryCurItem -= Inventory.Length;
        Inventory[pos] = itemIndex;
        return true;
    }

    /// <summary>giveCredits: coins go on one of five stacks, in the order StashSetup lays them out.</summary>
    public void GiveCredits(int credits)
    {
        int t = credits / 30;
        if (t == 0) t = 1;
        var stash = StaticData.Table("StashSetup");
        while (credits > 0)
        {
            if (t > credits) t = credits;
            if (Credits < 60 && t > 0)
            {
                int cnt = 0;
                do
                {
                    if (Credits < 60)
                    {
                        int d = stash[Credits % 12] - Credits / 12;
                        if (d < 0) d += 5;
                        MoneyColumnHeight[d] += 1;
                    }
                    Credits += 1;
                } while (++cnt < t);
            }
            else if (Credits >= 60) Credits += t;
            credits -= t;
        }
    }

    /// <summary>takeCredits: the same stacks, unwound.</summary>
    public void TakeCredits(int credits)
    {
        if (credits > Credits) credits = Credits;
        int t = credits / 30;
        if (t == 0) t = 1;
        var stash = StaticData.Table("StashSetup");
        while (credits > 0 && Credits > 0)
        {
            if (t > credits) t = credits;
            if (Credits - t < 60 && t > 0)
            {
                int cnt = 0;
                do
                {
                    if (--Credits < 60)
                    {
                        int d = stash[Credits % 12] - Credits / 12;
                        if (d < 0) d += 5;
                        MoneyColumnHeight[d] -= 1;
                    }
                } while (++cnt < t);
            }
            else if (Credits - t >= 60) Credits -= t;
            credits -= t;
        }
    }

    public ItemBoard()
    {
        for (int i = 0; i < InPlay.Length; i += 1) InPlay[i] = new Item { ShpCurFrameFlg = 0x8000 };
    }

    /// <summary>allocItemPropertiesBuffer.</summary>
    public void AllocProperties(int count)
    {
        Properties = new ItemProperty[count];
        for (int i = 0; i < count; i += 1) Properties[i] = new ItemProperty();
    }

    /// <summary>setItemProperty, with the one hard-coded fixup the engine carries.</summary>
    public void SetProperty(int[] args)
    {
        var p = Properties[args[0]];
        p.NameStringId = args[1] & 0xffff;
        p.ShpIndex = args[2] & 0xff;
        p.Type = args[3] & 0xffff;
        if (args[0] == 264 && p.Type == 5) p.Type = 0;
        p.ItemScriptFunc = args[4] & 0xff;
        p.Might = (sbyte)args[5];
        p.Skill = args[6] & 0xff;
        p.Protection = args[7] & 0xff;
        p.Flags = args[8] & 0xffff;
        p.UnkB = args[9] & 0xffff;
    }

    /// <summary>
    /// startupNew: the three quest items the party always carries and the chosen character's own
    /// kit. Nothing is drawn from them - they are in the party's hands - but they take the first
    /// item slots, and a slot number is part of where an item on the floor is drawn (SceneItemOffs),
    /// so a port that skips them puts every item in the game two pixels off.
    /// </summary>
    public void StartNewGame(int charSelection, Character[] party)
    {
        int[] selectIds = { -9, -1, -8, -5 };
        // startupNew: the purse and the three quest items the game begins with.
        GiveCredits(41);
        Inventory[0] = Make(216, 0, 0, 1);
        Inventory[1] = Make(217, 0, 0, 1);
        Inventory[2] = Make(218, 0, 0, 1);
        PartyRoster.AddCharacter(selectIds[charSelection], party, this);
    }

    public bool IsMoveable(int index)
    {
        if ((InPlay[index].ShpCurFrameFlg & 0x4000) == 0) return false;
        return (Properties[InPlay[index].ItemPropertyIndex].Flags & 4) == 0;
    }

    public void Delete(int index) => InPlay[index] = new Item { ShpCurFrameFlg = 0x8000 };

    /// <summary>isItemMoveable: whether a slot may be taken for something new.</summary>
    public bool IsItemMoveable(int index)
    {
        if ((InPlay[index].ShpCurFrameFlg & 0x4000) == 0) return false;
        if ((Properties[InPlay[index].ItemPropertyIndex].Flags & 4) != 0) return false;
        return true;
    }

    /// <summary>
    /// makeItem: a free slot for a new item, or the most distant level's moveable item recycled.
    /// Transliterated from makeItem in src/game/items.mjs.
    /// </summary>
    public int MakeItem(int itemType, int curFrame, int flags, int currentLevel)
    {
        int cnt = 0, r = 0, i = 1;
        for (; i < 400; i += 1)
        {
            var it = InPlay[i];
            if ((it.ShpCurFrameFlg & 0x8000) != 0) { cnt = 0; break; }
            if (it.Level < 1 || it.Level > 29 || it.Level == currentLevel) continue;
            int diff = Math.Abs(currentLevel - it.Level);
            if (diff <= cnt) continue;
            bool moveable = false;
            for (int ii = i; ii != 0 && !moveable; ii = InPlay[ii].NextAssignedObject) moveable = IsItemMoveable(ii);
            if (moveable) { cnt = diff; r = i; }
        }
        int slot = i;
        if (cnt != 0)
        {
            slot = 0;
            if (IsItemMoveable(r))
            {
                if (InPlay[r].NextAssignedObject != 0) InPlay[InPlay[r].NextAssignedObject].Level = InPlay[r].Level;
                Delete(r);
                slot = r;
            }
            else
            {
                for (int ii = InPlay[r].NextAssignedObject; ii != 0; ii = InPlay[ii].NextAssignedObject)
                {
                    if (!IsItemMoveable(ii)) continue;
                    InPlay[r].NextAssignedObject = InPlay[ii].NextAssignedObject;
                    Delete(ii);
                    slot = ii;
                    break;
                }
            }
        }
        if (slot >= 400 || slot <= 0) return 0;
        InPlay[slot] = new Item
        {
            ItemPropertyIndex = itemType,
            ShpCurFrameFlg = (curFrame & 0x1fff) | flags,
            Level = -1,
        };
        return slot;
    }

    /// <summary>
    /// makeItem: the first free slot, or - when there is none - the moveable item furthest from this
    /// level, thrown away to make room. The search order decides which item disappears, so it is
    /// copied exactly.
    /// </summary>
    public int Make(int itemType, int curFrame, int flags, int currentLevel)
    {
        int cnt = 0, r = 0, i = 1;
        for (; i < 400; i += 1)
        {
            var it = InPlay[i];
            if ((it.ShpCurFrameFlg & 0x8000) != 0) { cnt = 0; break; }
            if (it.Level < 1 || it.Level > 29 || it.Level == currentLevel) continue;
            int diff = Math.Abs(currentLevel - it.Level);
            if (diff <= cnt) continue;
            bool moveable = false;
            for (int ii = i; ii != 0 && !moveable; ii = InPlay[ii].NextAssignedObject) moveable = IsMoveable(ii);
            if (moveable) { cnt = diff; r = i; }
        }
        int slot = i;
        if (cnt != 0)
        {
            slot = 0;
            if (IsMoveable(r))
            {
                if (InPlay[r].NextAssignedObject != 0) InPlay[InPlay[r].NextAssignedObject].Level = InPlay[r].Level;
                Delete(r);
                slot = r;
            }
            else
            {
                for (int ii = InPlay[r].NextAssignedObject; ii != 0; ii = InPlay[ii].NextAssignedObject)
                {
                    if (!IsMoveable(ii)) continue;
                    InPlay[r].NextAssignedObject = InPlay[ii].NextAssignedObject;
                    Delete(ii);
                    slot = ii;
                    break;
                }
            }
        }
        if (slot >= 400) slot = ReclaimOrphan();
        if (slot >= 400) throw new InvalidOperationException("Out of item slots");
        InPlay[slot] = new Item
        {
            ItemPropertyIndex = itemType,
            ShpCurFrameFlg = (curFrame & 0x1fff) | flags,
            Level = -1,
        };
        return slot;
    }
}
