// The engine's records, shared by every module. Each is the object literal a JS factory builds
// (named in the comment); field names and array types are the JS ones.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>What findObject returns: a monster (id | 0x8000) or an item (id), both hang in block chains.</summary>
    public abstract class BlockObject
    {
        public int nextAssignedObject, nextDrawObject, flyingHeight, block, x, y;
    }

    /// <summary>items.mjs makeEmptyItem</summary>
    public sealed class Item : BlockObject
    {
        public int level, itemPropertyIndex, shpCurFrame_flg;
        public int wear;   // host-ui.mjs weapon wear

        /// <summary>{ ...it }</summary>
        public Item Spread() => (Item)MemberwiseClone();
    }

    /// <summary>items.mjs makeFlyingObject</summary>
    public sealed class FlyingObject
    {
        public int enable, objectType, attackerId, item, x, y, flyingHeight, direction, distance, field_D, c, flags, wallFlags;

        /// <summary>{ ...f }</summary>
        public FlyingObject Spread() => (FlyingObject)MemberwiseClone();
    }

    /// <summary>monsters.mjs makeEmptyMonster</summary>
    public sealed class Monster : BlockObject
    {
        public int destDirection, shiftStep, destX, destY, hitOffsX, hitOffsY, currentSubFrame, mode = 0x10, fightCurTick, id,
            direction, facing, flags, damageReceived, hitPoints, speedTick, type;
        public MonsterProperty properties;
        public int numDistAttacks, curDistWeapon, distAttackTick, assignedItems;
        public int[] equipmentShapes = { 0, 0, 0, 0 };
        // set by monsters.mjs (drawing, fleeing, attack pacing) and by the pit / NG+ (dungeon.mjs, mods.mjs)
        public int fleeing, drawW, drawH, drawX, drawY, drawSerial, attackWait, ngplus, dungeonBoss;
        public double pitScale;

        /// <summary>{ ...m }: every field, shallow (equipmentShapes shared, as the spread shares it).</summary>
        public Monster Spread() => (Monster)MemberwiseClone();
    }

    /// <summary>monsters.mjs makeMonsterProperty</summary>
    public sealed class MonsterProperty
    {
        public int shapeIndex, maxWidth;
        public ushort[] fightingStats = new ushort[9];
        public ushort[] itemsMight = new ushort[8];
        public ushort[] protectionAgainstItems = new ushort[8];
        public int itemProtection, hitPoints, speedTotalWaitTicks = 1, skillLevel, flags, unk5, numDistAttacks, numDistWeapons;
        public int[] distWeapons = { 0, 0, 0 };
        public int attackSkillChance, attackSkillType, defenseSkillChance, defenseSkillType;
        public int[] sounds = { 0, 0, 0 };
    }

    /// <summary>engine.mjs makeEmptyCharacter</summary>
    public sealed class Character
    {
        public int flags;
        public string name = "";
        public int raceClassSex, id, curFaceFrame, tempFaceFrame, screamSfx;
        public int[] defaultModifiers;
        public ushort[] itemsMight = new ushort[8];
        public ushort[] protectionAgainstItems = new ushort[8];
        public int itemProtection, hitPointsCur, hitPointsMax, magicPointsCur, magicPointsMax, field_41, damageSuffered, weaponHit,
            totalMightModifier, totalProtectionModifier, might, protection, nextAnimUpdateCountdown;
        public ushort[] items = new ushort[11];
        // addCharacter replaces these with plain JS arrays (Array.from), so they hold any int: a delay
        // of 3600, a counter that goes to -1, Dawn's 254/177 modifiers. A loaded save writes into the
        // typed arrays of makeEmptyCharacter instead - loadState masks the values the same way.
        public int[] skillLevels = new int[3];
        public int[] skillModifiers = new int[3];
        public int[] experiencePts = new int[3];
        public int[] characterUpdateEvents = new int[5];
        public int[] characterUpdateDelay = new int[5];
        /// <summary>what a fighter/rogue/mage potion added, taken back when it runs out</summary>
        public byte[] potionSkillBonus = new byte[3];
        public int attackCooldownTotal;
    }

    /// <summary>scene.mjs initScene: levelBlockProperties[1024]</summary>
    public sealed class LevelBlock
    {
        public byte[] walls = new byte[4];
        public int assignedObjects, drawObjects, direction = 5, flags;
    }

    /// <summary>scene.mjs assignLevelDecorationShapes: levelDecorationProperties[]</summary>
    public sealed class DecorationProperty
    {
        public ushort[] shapeIndex;
        public byte[] scaleFlag;
        public short[] shapeX, shapeY;
        public int next, flags;
    }

    /// <summary>gui.mjs gui_initButton</summary>
    public sealed class Button
    {
        public int index, defIndex, keyCode, keyCode2, dimTableIndex, flags, flags2, arg, x, y, width, height, absX, absY;
        public Func<Button, Task<int>> callback;
    }

    /// <summary>engine.mjs addTimer</summary>
    public sealed class Timer
    {
        public int id;
        public Action<int> func;
        public int countdown, enabled;
        public double nextRun, lastUpdate, pauseStart;
    }

    /// <summary>formats/lol-shapes.mjs: { width, height, pixels, colorTable, colorCount } (+ key, set by loaders)</summary>
    public sealed class Shape
    {
        public int width, height;
        public byte[] pixels;
        public byte[] colorTable;
        public int colorCount;
        public string key;
        /// <summary>host-ui: artwork the port adds for its own items (a URL in the browser)</summary>
        public string art;
    }

    /// <summary>engine.mjs: this.events entries (a DOM event, reduced).</summary>
    public sealed class InputEvent
    {
        public string type;   // "key" | "mousedown" | "mouseup"
        public string key;    // KeyboardEvent.key: "Enter", " ", "ArrowUp", "a", ...
        public bool shift;
        public int x, y, button;
    }

    /// <summary>engine.mjs activeSpell</summary>
    public sealed class ActiveSpell
    {
        public int spell;
        public S_SpellProperties p;
        public int charNum, level, target;
    }
}
