// src/game/items.mjs: items and flying objects (items_lol.cpp).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>itemProperties[] entry (script.mjs allocItemPropertiesBuffer; extra-spells / host-ui add their own).</summary>
    public sealed class ItemProperty
    {
        public int nameStringId, shpIndex, flags, type, itemScriptFunc, might, skill, protection, unkB;
    }

    public sealed partial class LandsOfLore
    {
        // ---- items.mjs fields ----
        public Item[] itemsInPlay;
        public List<ItemProperty> itemProperties;
        public FlyingObject[] flyingObjects;
        public byte[] moneyColumnHeight;
        public int emcLastItem;
        public int lastCharInventory;
        /// <summary>the host shows its stats when a merchant prices it</summary>
        public int lastPickedItem;

        public void initItems()
        {
            itemsInPlay = new Item[400];
            for (int i = 0; i < 400; i += 1) itemsInPlay[i] = makeEmptyItem();
            foreach (var item in itemsInPlay) item.shpCurFrame_flg |= 0x8000;
            itemProperties = new List<ItemProperty>();
            flyingObjects = new FlyingObject[8];
            for (int i = 0; i < 8; i += 1) flyingObjects[i] = makeFlyingObject();
            moneyColumnHeight = new byte[5];
            emcLastItem = -1;
            lastCharInventory = -1;
        }

        public Item makeEmptyItem() => new Item();

        public FlyingObject makeFlyingObject() => new FlyingObject();

        /// <summary>JS: an index past the table gives undefined (null here).</summary>
        public BlockObject findObject(int index)
        {
            if ((index & 0x8000) != 0)
            {
                int m = index & 0x7fff;
                return m < monsters.Length ? monsters[m] : null;
            }
            return index >= 0 && index < itemsInPlay.Length ? itemsInPlay[index] : null;
        }

        public int calcObjectPosition(BlockObject obj, int direction)
        {
            var (_, y) = calcSpriteRelPosition(partyPosX, partyPosY, obj.x, obj.y, direction);
            if (y < 0) y = 0;
            return (obj.flyingHeight << 12) | (4095 - y);
        }

        public void removeAssignedObjectFromBlock(LevelBlock l, int id)
        {
            if (l.assignedObjects == id)
            {
                var i = findObject(id);
                l.assignedObjects = i.nextAssignedObject;
                i.nextAssignedObject = 0;
                return;
            }
            int cur = l.assignedObjects;
            while (cur != 0)
            {
                var obj = findObject(cur);
                if (obj.nextAssignedObject == id)
                {
                    var i = findObject(id);
                    obj.nextAssignedObject = i.nextAssignedObject;
                    i.nextAssignedObject = 0;
                    return;
                }
                cur = obj.nextAssignedObject;
            }
        }

        public void removeDrawObjectFromBlock(LevelBlock l, int id)
        {
            if (l.drawObjects == id)
            {
                var i = findObject(id);
                l.drawObjects = i.nextDrawObject;
                i.nextDrawObject = 0;
                return;
            }
            int cur = l.drawObjects;
            while (cur != 0)
            {
                var obj = findObject(cur);
                if (obj.nextDrawObject == id)
                {
                    var i = findObject(id);
                    obj.nextDrawObject = i.nextDrawObject;
                    i.nextDrawObject = 0;
                    return;
                }
                cur = obj.nextDrawObject;
            }
        }

        public void assignObjectToBlock(LevelBlock l, int id)
        {
            var t = findObject(id);
            t.nextAssignedObject = l.assignedObjects;
            l.assignedObjects = id;
        }

        public void assignItemToBlock(LevelBlock l, int id)
        {
            { // already in this list? take it out first, so the chain can never point at itself
                int cur = l.assignedObjects; int guard = 0;
                while (cur != 0 && guard++ < 64) { if (cur == id) { removeAssignedObjectFromBlock(l, id); break; } cur = findObject(cur).nextAssignedObject; }
            }
            // walk past monsters (0x8000 entries), insert item after them
            if ((l.assignedObjects & 0x8000) != 0)
            {
                var prev = findObject(l.assignedObjects);
                while ((prev.nextAssignedObject & 0x8000) != 0) prev = findObject(prev.nextAssignedObject);
                var newObject = findObject(id);
                newObject.nextAssignedObject = prev.nextAssignedObject;
                if (newObject is Item ni) ni.level = -1;   // `level` only exists on items
                prev.nextAssignedObject = id;
                return;
            }
            {
                var newObject = findObject(id);
                newObject.nextAssignedObject = l.assignedObjects;
                if (newObject is Item ni) ni.level = -1;
                l.assignedObjects = id;
            }
        }

        public void assignBlockItem(LevelBlock l, int item)
        {
            BlockObject prev = null;
            int index = l.assignedObjects;
            while ((index & 0x8000) != 0)
            {
                prev = findObject(index);
                index = prev.nextAssignedObject;
            }
            var tmp = findObject(item);
            if (tmp is Item ti) ti.level = -1;
            int ix = index;
            if (ix == item) return;
            if (prev != null) prev.nextAssignedObject = item;
            else l.assignedObjects = item;
            var last = tmp;
            while (last.nextAssignedObject != 0) last = findObject(last.nextAssignedObject);
            last.nextAssignedObject = ix;
        }

        public void addLevelItems()
        {
            for (int i = 0; i < 400; i += 1)
            {
                if (itemsInPlay[i].level != currentLevel) continue;
                assignBlockItem(levelBlockProperties[itemsInPlay[i].block], i);
                levelBlockProperties[itemsInPlay[i].block].direction = 5;
                itemsInPlay[i].nextDrawObject = 0;
            }
        }

        public async Task giveCredits(int credits, int redraw)
        {
            if (redraw != 0) snd_playSoundEffect(101, -1);
            int t = credits / 30 != 0 ? credits / 30 : 1;
            while (credits != 0)
            {
                if (t > credits) t = credits;
                if (this.credits < 60 && t > 0)
                {
                    int cnt = 0;
                    do
                    {
                        if (this.credits < 60)
                        {
                            int d = @static.StashSetup[this.credits % 12] - this.credits / 12;
                            if (d < 0) d += 5;
                            moneyColumnHeight[d] += 1;
                        }
                        this.credits += 1;
                    } while (++cnt < t);
                }
                else if (this.credits >= 60) this.credits += t;
                if (redraw != 0)
                {
                    gui_drawMoneyBox(6);
                    await delay(tickLength, true);
                }
                credits -= t;
            }
        }

        public async Task takeCredits(int credits, int redraw)
        {
            if (redraw != 0) snd_playSoundEffect(101, -1);
            if (credits > this.credits) credits = this.credits;
            int t = credits / 30 != 0 ? credits / 30 : 1;
            while (credits != 0 && this.credits > 0)
            {
                if (t > credits) t = credits;
                if (this.credits - t < 60 && t > 0)
                {
                    int cnt = 0;
                    do
                    {
                        if (--this.credits < 60)
                        {
                            int d = @static.StashSetup[this.credits % 12] - this.credits / 12;
                            if (d < 0) d += 5;
                            moneyColumnHeight[d] -= 1;
                        }
                    } while (++cnt < t);
                }
                else if (this.credits - t >= 60) this.credits -= t;
                if (redraw != 0)
                {
                    gui_drawMoneyBox(6);
                    if (credits != 0) await delay(tickLength, true);
                }
                credits -= t;
            }
        }

        // Every record still referenced by somebody: the party's hands, packs and worn gear, a monster's
        // load, and anything hanging on a block of the level that is loaded. Used to tell a genuinely
        // orphaned record from one that is simply not on this level.
        public HashSet<int> itemRecordsInUse()
        {
            var used = new HashSet<int> { itemInHand };
            foreach (int it in inventory) if (it != 0) used.Add(it);
            foreach (var ch in characters) { if ((ch.flags & 1) == 0) continue; foreach (int it in ch.items) if (it != 0) used.Add(it); }
            foreach (var m in monsters)
            {
                int a = m != null ? m.assignedItems : 0;
                int guard = 0;
                while (a != 0 && guard++ < 32) { used.Add(a); a = a < itemsInPlay.Length && itemsInPlay[a] != null ? itemsInPlay[a].nextAssignedObject : 0; }
            }
            for (int b = 0; b < 1024; b += 1)
            {
                int cur = levelBlockProperties[b].assignedObjects;
                int guard = 0;
                while (cur != 0 && guard++ < 64)
                {
                    if ((cur & 0x8000) == 0) used.Add(cur);
                    var obj = findObject(cur);
                    cur = obj != null ? obj.nextAssignedObject : 0;
                }
            }
            return used;
        }

        public int makeItem(int itemType, int curFrame, int flags)
        {
            int cnt = 0;
            int r = 0;
            int i = 1;
            for (; i < 400; i += 1)
            {
                var it = itemsInPlay[i];
                if ((it.shpCurFrame_flg & 0x8000) != 0) { cnt = 0; break; }
                if (it.level < 1 || it.level > 29 || it.level == currentLevel) continue;
                int diff = Math.Abs(currentLevel - it.level);
                if (diff <= cnt) continue;
                bool t = false;
                for (int ii = i; ii != 0 && !t; ii = itemsInPlay[ii].nextAssignedObject) t = isItemMoveable(ii);
                if (t) { cnt = diff; r = i; }
            }
            int slot = i;
            if (cnt != 0)
            {
                slot = 0;
                if (isItemMoveable(r))
                {
                    if (itemsInPlay[r].nextAssignedObject != 0) itemsInPlay[itemsInPlay[r].nextAssignedObject].level = itemsInPlay[r].level;
                    deleteItem(r);
                    slot = r;
                }
                else
                {
                    for (int ii = itemsInPlay[r].nextAssignedObject; ii != 0; ii = itemsInPlay[ii].nextAssignedObject)
                    {
                        if (!isItemMoveable(ii)) continue;
                        itemsInPlay[r].nextAssignedObject = itemsInPlay[ii].nextAssignedObject;
                        deleteItem(ii);
                        slot = ii;
                        break;
                    }
                }
            }
            if (slot >= 400)
            {
                // Nothing free, and nothing on another level worth taking. Before giving up, reclaim records
                // that belong to nobody: no level, no block, not carried, not on a monster. The Imp's Pit
                // makes these by the dozen - a floor is built and thrown away - and because they sit outside
                // the 1..29 range the scan above skips them, so the table could only ever fill up.
                var used = itemRecordsInUse();
                for (int j = 1; j < 400; j += 1)
                {
                    var it = itemsInPlay[j];
                    if (it == null || it.itemPropertyIndex == 0 || used.Contains(j)) continue;
                    if (it.level >= 1 && it.level <= 29) continue;   // still belongs to a level
                    if (it.block != 0) continue;                     // still lying somewhere
                    deleteItem(j);
                    slot = j;
                    break;
                }
            }
            if (slot >= 400) throw new Exception("Out of item slots");
            itemsInPlay[slot] = makeEmptyItem();
            itemsInPlay[slot].itemPropertyIndex = itemType;
            itemsInPlay[slot].shpCurFrame_flg = (curFrame & 0x1fff) | flags;
            itemsInPlay[slot].level = -1;
            return slot;
        }

        public async Task placeMoveLevelItem(int itemIndex, int level, int block, int xOffs, int yOffs, int flyingHeight)
        {
            var it = itemsInPlay[itemIndex];
            (it.x, it.y) = calcCoordinates(block, xOffs, yOffs);
            if (it.block != 0) await removeLevelItem(itemIndex, it.block);
            if (currentLevel == level) await setItemPosition(itemIndex, it.x, it.y, flyingHeight, 1);
            else
            {
                it.level = level;
                it.block = block;
                it.flyingHeight = flyingHeight;
                it.shpCurFrame_flg |= 0x4000;
            }
        }

        public bool addItemToInventory(int itemIndex)
        {
            int pos = 0;
            int i = 0;
            for (; i < inventory.Length; i += 1)
            {
                pos = inventoryCurItem + i;
                if (pos >= inventory.Length) pos -= inventory.Length;
                if (inventory[pos] == 0) break;
            }
            if (i == inventory.Length) return false;
            while (inventoryCurItem > pos || inventoryCurItem + 9 <= pos)
            {
                if (++inventoryCurItem >= inventory.Length) inventoryCurItem -= inventory.Length;
                gui_drawInventory();
            }
            inventory[pos] = (ushort)itemIndex;
            gui_drawInventory();
            return true;
        }

        public bool isItemMoveable(int itemIndex)
        {
            if ((itemsInPlay[itemIndex].shpCurFrame_flg & 0x4000) == 0) return false;
            if ((itemProperties[itemsInPlay[itemIndex].itemPropertyIndex].flags & 4) != 0) return false;
            return true;
        }

        public void deleteItem(int itemIndex)
        {
            itemsInPlay[itemIndex] = makeEmptyItem();
            itemsInPlay[itemIndex].shpCurFrame_flg |= 0x8000;
        }

        public async Task runItemScript(int charNum, int item, int flags, int next, int reg4)
        {
            int func = item != 0 ? itemProperties[itemsInPlay[item].itemPropertyIndex].itemScriptFunc : 3;
            if (func == 0xff || itemScript == null) return;
            await runScriptFunction(itemScript, func, state =>
            {
                state.regs[0] = (short)flags;
                state.regs[1] = (short)charNum;
                state.regs[2] = (short)item;
                state.regs[3] = (short)next;
                state.regs[4] = (short)reg4;
                if ((state.entryFlags() & flags) == 0) state.ip = -1;
            });
        }

        public async Task setHandItem(int itemIndex)
        {
            if (itemIndex != 0 && (itemProperties[itemsInPlay[itemIndex].itemPropertyIndex].flags & 0x80) != 0)
            {
                await runItemScript(-1, itemIndex, 0x400, 0, 0);
                if ((itemsInPlay[itemIndex].shpCurFrame_flg & 0x8000) != 0) itemIndex = 0;
            }
            int mouseOffs = 0;
            if (itemIndex != 0 && (flagsTable[31] & 0x02) == 0)
            {
                mouseOffs = 10;
                if (currentControlMode == 0 || textEnabled())
                {
                    // itemName, not the raw string id: the items the port adds have no entry in the language
                    // file, and the id 0 they carry reads back as "Illegal Item".
                    string name = itemName(itemIndex);
                    if (string.IsNullOrEmpty(name)) name = getLangString(itemProperties[itemsInPlay[itemIndex].itemPropertyIndex].nameStringId);
                    txt.printMessage(0, Party_replace(getLangString(0x403e), "%s", name));
                }
            }
            itemInHand = itemIndex;
            setMouseCursor(mouseOffs, mouseOffs, getItemIconShapePtr(itemIndex));
        }

        public bool itemEquipped(int charNum, int itemType)
        {
            if (charNum < 0 || charNum > 3) return false;
            if ((characters[charNum].flags & 1) == 0) return false;
            for (int i = 0; i < 11; i += 1)
            {
                int it = characters[charNum].items[i];
                if (it != 0 && itemsInPlay[it].itemPropertyIndex == itemType) return true;
            }
            return false;
        }

        public async Task setItemPosition(int item, int x, int y, int flyingHeight, int moveable)
        {
            if (flyingHeight == 0)
            {
                x = (x & 0xffc0) | 0x40;
                y = (y & 0xffc0) | 0x40;
            }
            int block = calcBlockIndex(x, y);
            var it = itemsInPlay[item];
            it.x = x;
            it.y = y;
            it.block = block;
            it.flyingHeight = flyingHeight;
            if (moveable != 0) it.shpCurFrame_flg |= 0x4000;
            else it.shpCurFrame_flg &= 0xbfff;
            assignItemToBlock(levelBlockProperties[block], item);
            reassignDrawObjects(currentDirection, item, levelBlockProperties[block], false);
            if (moveable != 0) await runLevelScriptCustom(block, 0x80, -1, item, 0, 0);
            checkSceneUpdateNeed(block);
        }

        public async Task removeLevelItem(int item, int block)
        {
            lastPickedItem = item; // the host shows its stats when a merchant prices it
            removeAssignedObjectFromBlock(levelBlockProperties[block], item);
            removeDrawObjectFromBlock(levelBlockProperties[block], item);
            await runLevelScriptCustom(block, 0x100, -1, item, 0, 0);
            itemsInPlay[item].block = 0;
            itemsInPlay[item].level = 0;
        }

        public async Task<bool> launchObject(int objectType, int item, int startX, int startY, int flyingHeight, int direction, int unused, int attackerId, int c)
        {
            int sp = checkDrawObjectSpace(partyPosX, partyPosY, startX, startY);
            int slot = -1;
            int i = 0;
            for (; i < 8; i += 1)
            {
                var ft = flyingObjects[i];
                if (ft.enable == 0) { sp = -1; break; }
                int csp = checkDrawObjectSpace(partyPosX, partyPosY, ft.x, ft.y);
                if (csp > sp) { sp = csp; slot = i; }
            }
            if (sp != -1 && slot != -1)
            {
                i = slot;
                await endObjectFlight(flyingObjects[i], startX, startY, 8);
            }
            if (i == 8) return false;
            var t = flyingObjects[i];
            t.enable = 1;
            t.objectType = objectType;
            t.item = item;
            t.x = startX;
            t.y = startY;
            t.flyingHeight = flyingHeight;
            t.direction = direction;
            t.distance = 255;
            t.attackerId = attackerId;
            t.flags = 7;
            t.wallFlags = 2;
            t.c = c;
            if (attackerId != -1)
            {
                if ((attackerId & 0x8000) != 0) t.flags &= 0xfd;
                else
                {
                    t.flags &= 0xfb;
                    increaseExperience(attackerId, 1, 2);
                }
            }
            await updateObjectFlightPosition(t);
            return true;
        }

        public async Task endObjectFlight(FlyingObject t, int x, int y, int collisionType)
        {
            int cx = x;
            int cy = y;
            int block = calcBlockIndex(t.x, t.y);
            removeAssignedObjectFromBlock(levelBlockProperties[block], t.item);
            removeDrawObjectFromBlock(levelBlockProperties[block], t.item);
            if (collisionType == 1) { cx = t.x; cy = t.y; }
            if (t.objectType == 0 || t.objectType == 1)
            {
                await objectFlightProcessHits(t, cx, cy, collisionType);
                t.x = (cx & 0xffc0) | 0x40;
                t.y = (cy & 0xffc0) | 0x40;
                t.flyingHeight = 0;
                await updateObjectFlightPosition(t);
            }
            t.enable = 0;
        }

        public async Task processObjectFlight(FlyingObject t, int x, int y)
        {
            int bl = calcBlockIndex(t.x, t.y);
            var l = levelBlockProperties[bl];
            removeAssignedObjectFromBlock(l, t.item);
            removeDrawObjectFromBlock(l, t.item);
            t.x = x;
            t.y = y;
            await updateObjectFlightPosition(t);
            checkSceneUpdateNeed(bl);
        }

        public async Task updateObjectFlightPosition(FlyingObject t)
        {
            if (t.objectType == 0) await setItemPosition(t.item, t.x, t.y, t.flyingHeight, t.flyingHeight == 0 ? 1 : 0);
            else if (t.objectType == 1)
            {
                if (t.flyingHeight == 0)
                {
                    deleteItem(t.item);
                    checkSceneUpdateNeed(calcBlockIndex(t.x, t.y));
                }
                else await setItemPosition(t.item, t.x, t.y, t.flyingHeight, 0);
            }
        }

        public async Task objectFlightProcessHits(FlyingObject t, int x, int y, int collisionType)
        {
            var it = itemsInPlay[t.item];
            if (collisionType == 1)
            {
                await runLevelScriptCustom(calcNewBlockPosition(it.block, t.direction >> 1), 0x8000, -1, t.item, 0, 0);
            }
            else if (collisionType == 2)
            {
                if ((itemProperties[it.itemPropertyIndex].flags & 0x4000) != 0)
                {
                    int obj = levelBlockProperties[it.block].assignedObjects;
                    while ((obj & 0x8000) != 0)
                    {
                        await runItemScript(t.attackerId, t.item, 0x8000, obj, 0);
                        obj = findObject(obj).nextAssignedObject;
                    }
                }
                else await runItemScript(t.attackerId, t.item, 0x8000, getNearestMonsterFromPos(x, y), 0);
            }
            else if (collisionType == 4)
            {
                partyAwake = true;
                if ((itemProperties[it.itemPropertyIndex].flags & 0x4000) != 0)
                {
                    for (int i = 0; i < 4; i += 1) if ((characters[i].flags & 1) != 0) await runItemScript(t.attackerId, t.item, 0x8000, i, 0);
                }
                else await runItemScript(t.attackerId, t.item, 0x8000, getNearestPartyMemberFromPos(x, y), 0);
            }
        }

        public async Task updateFlyingObject(FlyingObject t)
        {
            var (x, y) = getNextStepCoords(t.x, t.y, t.direction);
            int collisionType = checkBlockBeforeObjectPlacement(x, y, 63, t.flags, t.wallFlags);
            if (collisionType != 0) await endObjectFlight(t, x, y, collisionType);
            else if (--t.distance != 0) await processObjectFlight(t, x, y);
            else await endObjectFlight(t, x, y, 8);
        }

        public int checkDrawObjectSpace(int x1, int y1, int x2, int y2)
        {
            return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
        }

        public int checkSceneForItems(LevelBlock l, int color)
        {
            int cur = l.drawObjects;
            while (cur != 0)
            {
                if ((cur & 0x8000) == 0)
                {
                    if (--color == 0) return cur;
                }
                cur = findObject(cur).nextDrawObject;
            }
            return -1;
        }
    }
}
