// src/game/extra-spells.mjs
// Spells the port adds on top of the game's own. Nothing in the original data knows about them:
// they are appended to SpellProperties and spellProcs (both plain arrays) at start-up, carry their
// own name, and are built only out of engine primitives that already exist.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>extra-spells.mjs EXTRA_SPELLS entries (and extraSpells[spell] = { ...def, spell })</summary>
    public sealed class ExtraSpellDef
    {
        public string id, name, icon;
        public int[] mp;
        public int flags;
        public int[] damage;
        public double[] share;
        public int[] heal, seconds;
        public double[] bonus;
        public int price;
        public string about;
        public int spell;
        /// <summary>a slot of the game's own spell table this spell fills (-1: appended after it)</summary>
        public int fixedSpell = -1;
        public int[] push;

        public ExtraSpellDef Copy() => (ExtraSpellDef)MemberwiseClone();
    }

    /// <summary>extra-spells.mjs castThorns: thornBlocks entries</summary>
    public sealed class ThornBlock
    {
        public int block, level, ticks, damage, charNum, wait, item;
        public int stage;   // 0..2: the briars grow out of the floor and shrink back as they wither
    }

    public sealed partial class LandsOfLore
    {
        public static readonly ExtraSpellDef[] EXTRA_SPELLS =
        {
            new ExtraSpellDef
            {
                id = "drain",
                name = "Drain",
                icon = "drain",
                mp = new[] { 12, 22, 38, 60 },
                flags = 0x100, // 0x100: refused when a wall is in the way, like the other forward spells
                // Mist of Doom, the game's own block spell, is 30/70/110/200 for 30/60/90/150 mana. Drain is
                // deliberately well under that: it is cheap, and it gives health back, so it must not also hit
                // as hard as the strongest spell in the book.
                damage = new[] { 8, 14, 24, 38 },
                share = new[] { 0.35, 0.35, 0.4, 0.4 }, // of the damage actually dealt, returned as health
                heal = new[] { 10, 18, 28, 40 }, // and never more than this in one cast
                price = 300,
                about = "Tears life out of what stands ahead and gives part of it to the caster.",
            },
            new ExtraSpellDef
            {
                id = "thorns",
                name = "Wall of Thorns",
                icon = "thorns",
                mp = new[] { 15, 28, 45, 70 },
                flags = 0x100,
                damage = new[] { 4, 7, 11, 17 }, // every three seconds, to whatever stands in the thorns
                seconds = new[] { 10, 14, 18, 24 },
                price = 350,
                about = "Briars burst from the floor ahead and tear at anything that stands in them.",
            },
            new ExtraSpellDef
            {
                id = "viper",
                name = "Viper",
                icon = "viper",
                mp = new[] { 14, 25, 40, 64 },
                flags = 0x100,
                // Mist of Doom, the strongest thing in the book, is 200 might for 150 mana. Everything here
                // sits near two thirds of a might per mana, because each of these carries something else as
                // well - reach, healing, or lasting on the floor.
                damage = new[] { 9, 16, 26, 42 },
                price = 320,
                about = "Sends a serpent down the passage ahead; it strikes the first creature it reaches, up to three blocks away.",
            },
            new ExtraSpellDef
            {
                id = "backstab",
                name = "Backstab",
                icon = "backstab",
                mp = new[] { 20, 30, 45, 65 },
                flags = 0x100,
                bonus = new[] { 1, 1.25, 1.5, 2 }, // damage multiplier of the free strike
                price = 400,
                about = "The party slips behind the creature ahead and the chosen hero strikes, never missing. Needs open floor behind it.",
            },
            // Restored: the game's spell 7, "Vortex", has its name, mana and sound in the data but was cut before
            // release (no spell proc, no scroll anywhere). It fills its own slot, so the spells above keep theirs.
            new ExtraSpellDef
            {
                id = "vortex",
                name = "Vortex",
                icon = "vortex",
                fixedSpell = 7,
                mp = new[] { 10, 20, 35, 55 },
                flags = 0x100,
                damage = new[] { 10, 18, 30, 46 },
                push = new[] { 1, 1, 2, 3 },   // blocks the survivors are thrown back
                price = 450,
                about = "A whirlwind tears through the block ahead and hurls whatever survives it back down the passage.",
            },
        };

        // ---- the briars themselves ----
        // Wall of Thorns has to be visible, so it puts a real object on the block: the engine already
        // draws objects at the right place and size, and one marked "not moveable" cannot be picked up.
        // The sprite is drawn here against the level's own palette, since the original art has no briars.
        static int nearestIndex(byte[] palette, int r, int g, int b, Dictionary<int, int> cache)
        {
            int key = (r << 16) | (g << 8) | b;
            if (cache.TryGetValue(key, out int hit)) return hit;
            int pr = r >> 2; int pg = g >> 2; int pb = b >> 2;
            int best = 1; double bestDist = double.PositiveInfinity;
            for (int i = 1; i < 256; i += 1)
            {
                int dr = palette[i * 3] - pr; int dg = palette[i * 3 + 1] - pg; int db = palette[i * 3 + 2] - pb;
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            cache[key] = best;
            return best;
        }

        static Shape briarShape(byte[] palette)
        {
            const int W = 30; const int H = 22;
            var pixels = new byte[W * H];
            var cache = new Dictionary<int, int>();
            Func<int, int, int, int> ix = (r, g, b) => nearestIndex(palette, r, g, b, cache);
            Action<double, double, int> dot = (xd, yd, c) => { int x = Js.Round(xd); int y = Js.Round(yd); if (x >= 0 && y >= 0 && x < W && y < H) pixels[y * W + x] = (byte)c; };
            Action<int, int, int, int, int> line = (x0, y0, x1, y1, c) =>
            {
                int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                for (int i = 0; i <= n; i += 1) dot(x0 + ((double)(x1 - x0) * i) / n, y0 + ((double)(y1 - y0) * i) / n, c);
            };
            int dark = ix(20, 44, 18); int mid = ix(38, 74, 30); int lit = ix(74, 112, 48);
            int thorn = ix(150, 150, 120);
            for (int i = 0; i < 7; i += 1)
            { // a tangle of stems leaning out of the floor
                int x0 = 3 + i * 4;
                int top = 6 + ((i * 5) % 7);
                line(x0, H - 2, x0 + (i % 2 != 0 ? 3 : -3), top, i % 3 != 0 ? mid : dark);
                line(x0 + (i % 2 != 0 ? 3 : -3), top, x0 + (i % 2 != 0 ? 6 : -1), top + 4, dark);
                dot(x0 + (i % 2 != 0 ? 4 : -4), top - 1, thorn); // the spikes catch the light
                dot(x0 + (i % 2 != 0 ? 2 : -2), top + 3, thorn);
            }
            for (int i = 0; i < 5; i += 1) line(2 + i * 6, H - 2, 6 + i * 6, H - 6, lit);
            for (int x = 1; x < W - 1; x += 1) dot(x, H - 1, dark);
            return new Shape { width = W, height = H, pixels = pixels, colorTable = null, colorCount = 256, key = "camp:briars" };
        }

        // props.extraSpellBase: a property on the shared static SpellProperties array in JS (decided once
        // for every engine instance).
        static int? ExtraSpells_extraSpellBase;

        // ---- ExtraSpellsMixin ----
        public int extraSpellBase;
        public Dictionary<int, ExtraSpellDef> extraSpells;
        public bool extraSpellsReady;
        public bool thornTimer;
        public List<ThornBlock> thornBlocks;
        /// <summary>the briars a loaded save had, taken over on the thorns' next tick</summary>
        public List<ThornBlock> thornRestore;
        public int briarProp;
        public int briarLevel;
        /// <summary>items the port adds, by item property (host-ui.mjs uiRegisterExtraItems fills it too)</summary>
        public Dictionary<int, ExtraItemDef> extraItems;

        // Appends the extra spells to the engine's tables. Idempotent, and deterministic, so a spell
        // learned in one session is the same spell after a save and load.
        public Dictionary<string, int> uiRegisterExtraSpells(ExtraSpellDef[] defs = null)
        {
            defs = defs ?? EXTRA_SPELLS;
            var props = @static.SpellProperties;
            if (props == null || props.Length == 0) return null;
            // SpellProperties is shared between engine instances, so the indexes have to be decided once
            // and reused: a save written by one instance must mean the same spells in the next.
            if (ExtraSpells_extraSpellBase == null) ExtraSpells_extraSpellBase = props.Length;
            extraSpellBase = ExtraSpells_extraSpellBase.Value;
            if (extraSpells != null && extraSpellsReady)
            {
                return defs.Select((d, i) => (d.id, spellIndexOf(defs, i))).ToDictionary(e => e.id, e => e.Item2);
            }
            extraSpellsReady = true;
            extraSpells = new Dictionary<int, ExtraSpellDef>();
            var @out = new Dictionary<string, int>();
            for (int i = 0; i < defs.Length; i += 1)
            {
                var def = defs[i];
                int spell = spellIndexOf(defs, i);
                if (spell >= props.Length) Array.Resize(ref @static.SpellProperties, spell + 1);   // props[spell] = ... grows the JS array
                props = @static.SpellProperties;
                props[spell] = new S_SpellProperties
                {
                    spellNameCode = 0,
                    mpRequired = def.mp.ToArray(),
                    hpRequired = new[] { 0, 0, 0, 0 },
                    flags = def.flags,
                };
                while (spellProcs.Count <= spell) spellProcs.Add(null);
                spellProcs[spell] = (a) => castExtraSpell(def, a);
                var copy = def.Copy();
                copy.spell = spell;
                extraSpells[spell] = copy;
                @out[def.id] = spell;
            }
            // Anything the party knows must resolve: a save from a build with different extras would
            // otherwise leave a hole in the table for the first reader to fall into.
            foreach (int spell in availableSpells)
            {
                if (spell >= 0 && (spell >= props.Length || props[spell] == null))
                {
                    if (spell >= props.Length) { Array.Resize(ref @static.SpellProperties, spell + 1); props = @static.SpellProperties; }
                    props[spell] = new S_SpellProperties { spellNameCode = 0, mpRequired = new[] { 0, 0, 0, 0 }, hpRequired = new[] { 0, 0, 0, 0 }, flags = 0 };
                }
            }
            if (!thornTimer)
            { // the thorns tick on their own timer (id 12 is unused by the game)
                thornTimer = true;
                thornBlocks = new List<ThornBlock>();
                addTimer(12, _ => processThorns(), 15, true);
            }
            return @out;
        }

        // a spell's index: its own slot of the game's table, or the next one after the table
        int spellIndexOf(ExtraSpellDef[] defs, int i) =>
            defs[i].fixedSpell >= 0 ? defs[i].fixedSpell : extraSpellBase + defs.Take(i).Count(d => d.fixedSpell < 0);

        // Teaching the party a spell: the first free slot of availableSpells (the game's own scrolls do
        // the same). Returns the slot, or -1 when they know it already or the book is full.
        public int uiLearnSpell(int spell)
        {
            if (availableSpells.Any(s => s == spell)) return -1;
            for (int i = 0; i < availableSpells.Length; i += 1)
            {
                if (availableSpells[i] != -1) continue;
                availableSpells[i] = (sbyte)spell;
                gui_drawScroll();
                return i;
            }
            return -1;
        }

        public bool uiKnowsSpell(int spell)
        {
            return availableSpells.Any(s => s == spell);
        }

        public ExtraSpellDef extraSpellAt(int spell)
        {
            return extraSpells != null && extraSpells.TryGetValue(spell, out var e) ? e : null;
        }

        // Spell name, with the port's own spells resolved (the language files cannot be extended).
        public string spellName(int spell)
        {
            var extra = extraSpellAt(spell);
            if (extra != null) return extra.name;
            var p = spell >= 0 && spell < @static.SpellProperties.Length ? @static.SpellProperties[spell] : null;
            string s = p != null ? getLangString(p.spellNameCode) : null;
            return !string.IsNullOrEmpty(s) ? s : $"Spell {spell}";
        }

        public async Task<int> castExtraSpell(ExtraSpellDef def, ActiveSpell a)
        {
            if (def.id == "drain") return await castDrain(def, a);
            if (def.id == "thorns") return await castThorns(def, a);
            if (def.id == "backstab") return await castBackstab(def, a);
            if (def.id == "viper") return await castViper(def, a);
            if (def.id == "vortex") return await castVortex(def, a);
            return 0;
        }

        // Damage what stands ahead, and give the caster part of what was actually taken.
        public async Task<int> castDrain(ExtraSpellDef def, ActiveSpell a)
        {
            int ahead = calcNewBlockPosition(currentBlock, currentDirection);
            int before = monstersOnBlockHp(ahead);
            if (before == 0) { txt.printMessage(2, "There is nothing there to drain."); return 1; }
            // a sickly cloud tears at what stands ahead (PGAS.WSA, in the game's files but never used)
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            snd_playSoundEffect(155, -1);
            try
            {
                var gas = openWsa("PGAS.WSA", 0);
                await playSpellAnimation(gas, 0, gas.numFrames, 5, 200 - gas.width / 2, 100 - gas.height, null, true);
            }
            catch (QuitException) { throw; }
            catch (Exception) { /* no animation: the spell still works */ }
            screen.curPage = cp;
            envSfxUseQueue = true;
            inflictMagicalDamageForBlock(ahead, a.charNum, def.damage[a.level], 0x80);
            envSfxUseQueue = false;
            int taken = Math.Max(0, before - monstersOnBlockHp(ahead));
            int back = Math.Min(def.heal[a.level], Js.Round(taken * def.share[a.level]));
            if (back > 0)
            {
                await drainOrb(a.charNum);
                increaseCharacterHitpoints(a.charNum, back, false);
                gui_drawCharPortraitWithStats(a.charNum);
                txt.printMessage(0, $"{characters[a.charNum].name} drains {back} health.");
            }
            sceneUpdateRequired = true;
            return 1;
        }

        // the life taken flies down to the caster's portrait: the glowing orb of a spell being learnt (GETSPELL.WSA)
        async Task drainOrb(int charNum)
        {
            WsaPlayer orb;
            try { orb = openWsa("GETSPELL.WSA"); } catch (QuitException) { throw; } catch (Exception) { return; }
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            int fromX = 200, fromY = 50, toX = activeCharsXpos[Math.Max(0, Math.Min(activeCharsXpos.Length - 1, charNum))] + 33, toY = 160;
            snd_playSoundEffect(128, -1);
            for (int i = 0; i <= 14; i += 1)
            {
                double until = getMillis() + tickLength;
                screen.copyPage(12, 2);
                int x = fromX + (toX - fromX) * i / 14 - 16, y = fromY + (toY - fromY) * i / 14 - 16;
                orb.displayFrame(51, 2, x, y, 0x5000, transparencyTable1, transparencyTable2);
                screen.copyRegion(x - 24, y - 24, x - 24, y - 24, orb.width + 48, orb.height + 48, 2, 0, true);
                await delayUntil(until);
            }
            screen.copyPage(12, 2);
            screen.copyPage(2, 0);
            screen.curPage = cp;
        }

        // The serpent the wraiths send: the same animation, thrown the other way. It runs up to three
        // blocks down the passage and strikes whatever it reaches first.
        public async Task<int> castViper(ExtraSpellDef def, ActiveSpell a)
        {
            int block = currentBlock;
            int d = 0;
            for (; d < 3; d += 1)
            {
                if ((levelBlockProperties[block].assignedObjects & 0x8000) != 0) break;
                int next = calcNewBlockPosition(block, currentDirection);
                if ((wllWallFlags[levelBlockProperties[next].walls[currentDirection ^ 2]] & 7) != 0) break;
                block = next;
            }
            var screen = this.screen;
            screen.copyPage(0, 12);
            snd_playSoundEffect(148, -1);
            WsaPlayer mov = null;
            try { mov = openWsa("VIPER.WSA", 1); } catch (QuitException) { throw; } catch (Exception) { mov = null; }
            if (mov != null)
            {
                var frames = Js.Slice(new[] { 15, 25, 20, 10, 25, 20, 5, 25, 20, 0, 25, 20 }, d * 3, d * 3 + 3);
                int frm = frames[0];
                for (bool running = true; running;)
                {
                    double etime = getMillis() + 5 * tickLength;
                    screen.copyPage(12, 2);
                    if (frm == frames[2]) snd_playSoundEffect(172, -1);
                    mov.displayFrame(frm++ % mov.numFrames, 2, 112, 0, 0x5000, transparencyTable1, transparencyTable2);
                    screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
                    await delayUntil(etime);
                    if (frm > frames[1]) running = false;
                }
                screen.copyPage(12, 0);
                screen.copyPage(12, 2);
            }
            int before = monstersOnBlockHp(block);
            envSfxUseQueue = true;
            inflictMagicalDamageForBlock(block, a.charNum, def.damage[a.level], 0x80);
            envSfxUseQueue = false;
            int dealt = Math.Max(0, before - monstersOnBlockHp(block));
            txt.printMessage(0, dealt != 0 ? $"The serpent strikes for {dealt}." : "The serpent finds nothing to strike.");
            updateDrawPage2();
            snd_playQueuedEffects();
            sceneUpdateRequired = true;
            return 1;
        }

        // Vortex: the game's own spell swirl, then a burst on the block ahead; every monster there takes the damage,
        // and the survivors are thrown back one block at a time while the floor behind them is open.
        public async Task<int> castVortex(ExtraSpellDef def, ActiveSpell a)
        {
            int block = calcNewBlockPosition(currentBlock, currentDirection);
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            snd_playSoundEffect(157, -1);   // VORTEX1, the cut spell's own sound (where the data has it)
            try
            {
                var swirl = openWsa("GETSPELL.WSA");
                await playSpellAnimation(swirl, 0, 25, 3, 200 - swirl.width / 2, 50 - swirl.height / 2, null, true);
                var burst = openWsa("SPELLEXP.WSA");
                snd_playSoundEffect(168, -1);
                await playSpellAnimation(burst, 0, 8, 3, 200 - burst.width / 2, 56 - burst.height / 2, null, true);
            }
            catch (QuitException) { throw; }
            catch (Exception) { /* no animation: the spell still works */ }
            screen.curPage = cp;
            screen.copyPage(12, 2);
            gui_drawScene(2);
            if ((levelBlockProperties[block].assignedObjects & 0x8000) == 0)
            {
                txt.printMessage(0, "The vortex howls through an empty passage.");
                updateDrawPage2();
                return 1;
            }
            int before = monstersOnBlockHp(block);
            envSfxUseQueue = true;
            inflictMagicalDamageForBlock(block, a.charNum, def.damage[a.level], 0x80);
            envSfxUseQueue = false;
            int dealt = Math.Max(0, before - monstersOnBlockHp(block));
            // the survivors fly back (as Hand of Fate's push, a block per step)
            int thrown = 0;
            int dir = currentDirection << 1;
            for (int step = 0; step < def.push[a.level]; step += 1)
            {
                if ((levelBlockProperties[block].assignedObjects & 0x8000) == 0) break;
                int next = calcNewBlockPosition(block, currentDirection);
                if (testWallFlag(next, 0, 4) || (levelBlockProperties[next].assignedObjects & 0x8000) != 0) break;
                checkSceneUpdateNeed(block);
                int o = levelBlockProperties[block].assignedObjects;
                while ((o & 0x8000) != 0)
                {
                    int o2 = o;
                    var m = monsters[o & 0x7fff];
                    o = findObject(o).nextAssignedObject;
                    var (nX, nY) = getNextStepCoords(m.x, m.y, dir);
                    for (int k = 0; k < 7; k += 1) (nX, nY) = getNextStepCoords(nX, nY, dir);
                    // on to the far side of that block (a monster at its block's edge would be back in one step)
                    for (int k = 0; k < 6; k += 1)
                    {
                        var (fx, fy) = getNextStepCoords(nX, nY, dir);
                        if (calcBlockIndex(fx, fy) != next) break;
                        (nX, nY) = (fx, fy);
                    }
                    placeMonster(m, nX, nY);
                    m.destX = nX; m.destY = nY;   // its walk target too, or it snaps back to where it stood
                    m.speedTick = -(6 + 4 * a.level);   // and it reels for a moment before it can move again
                    await runLevelScriptCustom(next, 0x800, -1, o2, 0, 0);
                }
                block = next;
                thrown += 1;
            }
            txt.printMessage(0, $"The vortex strikes for {dealt}{(thrown > 0 ? $" and hurls them back {thrown} block{(thrown > 1 ? "s" : "")}" : "")}.");
            updateDrawPage2();
            snd_playQueuedEffects();
            sceneUpdateRequired = true;
            return 1;
        }

        public int monstersOnBlockHp(int block)
        {
            int hp = 0;
            int o = levelBlockProperties[block].assignedObjects;
            int guard = 0;
            while ((o & 0x8000) != 0 && guard++ < 32)
            {
                var m = monsters[o & 0x7fff];
                if (m.hitPoints > 0) hp += m.hitPoints;
                o = m.nextAssignedObject;
            }
            return hp;
        }

        // The item property the briars are drawn as, made once per level (the palette changes with it).
        // The briars: the forest's leafy bush (FOREST1.SHP:84, read from the game files whatever level the party is
        // on) darkened to a bramble and wound with thorny stems, in the current level's palette; three sizes, so the
        // bush can grow out of the floor. Without the forest files: thorny stems alone.
        public int[] briarProps;
        static (int w, int h, double[] lum)? briarArt;
        static bool briarArtTried;

        (int w, int h, double[] lum)? forestBush()
        {
            if (briarArtTried) return briarArt;
            briarArtTried = true;
            try
            {
                var t = res.fetchBytes("FOREST1.PAK");
                if (!t.IsCompleted || t.IsFaulted) return null;
                var pak = new PakArchive(t.Result);
                var shp = pak.get("FOREST1.SHP");
                var vcn = LolSceneFile.decodeVcn(pak.get("FOREST1.VCN"));
                int count = shp[0] | (shp[1] << 8);
                if (count <= 84) return null;
                int offs = (shp[84 * 4 + 2] | (shp[84 * 4 + 3] << 8) | (shp[84 * 4 + 4] << 16) | (shp[84 * 4 + 5] << 24)) + 2;
                var shape = LolShapes.decodeShape(shp, offs);
                var pal = screen.getPalette(0);
                var lum = new double[shape.width * shape.height];
                for (int i = 0; i < lum.Length; i += 1)
                {
                    int raw = shape.pixels[i];
                    if (raw == 0) { lum[i] = -1; continue; }
                    int c = shape.colorTable != null ? shape.colorTable[raw] : raw;
                    var p6 = c < 128 ? vcn.rawPalette : pal;
                    lum[i] = (p6[c * 3] * 0.3 + p6[c * 3 + 1] * 0.6 + p6[c * 3 + 2] * 0.1) / 63.0;
                }
                briarArt = (shape.width, shape.height, lum);
            }
            catch (Exception) { briarArt = null; }
            return briarArt;
        }

        /// <summary>The bramble colours of a palette: rind, dark, mid, lit stem, thorn, leaf dark, leaf lit.</summary>
        int[] thornColors(byte[] palette)
        {
            var cache = new Dictionary<int, int>();
            return new[] { (36, 30, 14), (82, 74, 34), (118, 110, 52), (166, 156, 82), (244, 236, 196), (44, 96, 32), (96, 158, 62) }
                .Select(c => nearestIndex(palette, c.Item1, c.Item2, c.Item3, cache)).ToArray();
        }

        /// <summary>Thorny stems growing up from the bottom of a w x h area: t (0..1) of each stem is drawn. Each stem is
        /// a curve that leans and curls over, thick at the root and thin at the tip, with pale thorns along it.</summary>
        static void drawVines(Action<int, int, int> put, int w, int h, double t, int seed, int stems, int[] col, double thick)
        {
            var rnd = new Random(seed);
            for (int v = 0; v < stems; v += 1)
            {
                double x0 = w * (0.08 + 0.84 * (v + rnd.NextDouble() * 0.6) / stems);
                double lean = (rnd.NextDouble() - 0.5) * w * 0.5;
                double height = h * (0.55 + rnd.NextDouble() * 0.42);
                double curl = (rnd.NextDouble() < 0.5 ? -1 : 1) * w * (0.08 + rnd.NextDouble() * 0.12);
                double delay = rnd.NextDouble() * 0.3;            // the stems do not all start at once
                double grow = Math.Max(0, Math.Min(1, (t - delay) / (1 - delay)));
                grow = 1 - (1 - grow) * (1 - grow);              // fast out of the ground, slowing as it reaches up
                int steps = (int)(height * 1.4);
                int drawn = (int)(steps * grow);
                double lastThorn = -99;
                for (int i = 0; i <= drawn; i += 1)
                {
                    double u = (double)i / steps;
                    // a quadratic lean plus a curl near the tip
                    double x = x0 + lean * u * u + curl * Math.Max(0, u - 0.6) * Math.Max(0, u - 0.6) * 6;
                    double y = h - 1 - height * u;
                    double r = Math.Max(0.6, thick * (1 - u * 0.8));
                    int ri = (int)Math.Ceiling(r);
                    for (int dy = -ri; dy <= ri; dy += 1)
                        for (int dx = -ri - 1; dx <= ri + 1; dx += 1)
                        {
                            double d = Math.Sqrt(dx * dx + dy * dy);
                            if (d > r + 1) continue;
                            int c = d > r ? col[0] : dx < -r * 0.3 ? col[1] : dx > r * 0.4 ? col[3] : col[2];
                            put((int)Math.Round(x) + dx, (int)Math.Round(y) + dy, c);
                        }
                    // thorns, alternate sides, every few pixels of stem
                    if (u * height - lastThorn > 4.5 + rnd.NextDouble() * 2 && i > 3)
                    {
                        lastThorn = u * height;
                        int side = ((int)lastThorn & 1) == 0 ? 1 : -1;
                        int len = (int)Math.Round(3 + r * 1.2);
                        for (int k = 0; k <= len; k += 1)
                        {
                            int tx = (int)Math.Round(x + side * (r + k)), ty = (int)Math.Round(y - k * 0.7);
                            put(tx, ty, k >= len - 1 ? col[4] : col[3]);
                            if (k < len / 2) put(tx, ty + 1, col[0]);      // a broad dark base, a pale point
                        }
                    }
                    // the odd leaf
                    if (i % 13 == 7 && u < 0.85)
                    {
                        int side = (i / 13) % 2 == 0 ? 1 : -1;
                        for (int k = 1; k <= 3; k += 1)
                        {
                            put((int)Math.Round(x + side * (r + k)), (int)Math.Round(y + 1), col[5]);
                            put((int)Math.Round(x + side * (r + k)), (int)Math.Round(y), k == 2 ? col[6] : col[5]);
                        }
                    }
                }
            }
        }

        Shape thornShape(byte[] palette, double size)
        {
            var col = thornColors(palette);
            var cache = new Dictionary<int, int>();
            var bush = forestBush();
            int bw = bush?.w ?? 56, bh = bush?.h ?? 50;
            int W = bw + 16, H = bh + 8;                     // room for stems reaching past the leaves
            var full = new byte[W * H];
            if (bush != null)
            {
                var (w, h, lum) = bush.Value;
                // the bush, darkened to an olive bramble (its own light kept)
                for (int y = 0; y < h; y += 1)
                    for (int x = 0; x < w; x += 1)
                    {
                        double l = lum[y * w + x];
                        if (l < 0) continue;
                        double k = Math.Min(1, l * 1.15);
                        full[(y + 8) * W + x + 8] = (byte)nearestIndex(palette, (int)(16 + 54 * k), (int)(30 + 70 * k), (int)(10 + 26 * k), cache);
                    }
            }
            drawVines((x, y, c) => { if (x >= 0 && y >= 0 && x < W && y < H) full[y * W + x] = (byte)c; }, W, H, 1, 7, 7, col, 1.6);
            if (size >= 0.999) return new Shape { width = W, height = H, pixels = full, colorTable = null, colorCount = 256, key = "camp:thorns100" };
            int sw = Math.Max(4, (int)Math.Round(W * size)), sh = Math.Max(4, (int)Math.Round(H * size));
            var small = new byte[sw * sh];
            for (int y = 0; y < sh; y += 1) for (int x = 0; x < sw; x += 1) small[y * sw + x] = full[(y * H / sh) * W + x * W / sw];
            return new Shape { width = sw, height = sh, pixels = small, colorTable = null, colorCount = 256, key = $"camp:thorns{(int)(size * 100)}" };
        }

        // The cast: thorny stems whip up out of the floor across the scene window and curl over (drawn frame by frame
        // over the scene, as the game's own spells play their animations), then sink into the briars left on the block.
        async Task thornsAnimation(int seed)
        {
            var screen = this.screen;
            int cp = screen.curPage;
            screen.curPage = 2;
            screen.copyPage(0, 12);
            var col = thornColors(screen.getPalette(0));
            snd_playSoundEffect(18, -1);
            const int frames = 12;
            for (int f = 1; f <= frames + 3; f += 1)
            {
                double until = getMillis() + 3 * tickLength;
                screen.copyPage(12, 2);
                double t = Math.Min(1.0, (double)f / frames);
                drawVines((x, y, c) => { if (x >= 0 && y >= 0 && x < 176 && y < 120) screen.setPagePixel(2, 112 + x, y, c); }, 176, 120, t, seed, 9, col, 3.2);
                screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0, true);
                if (f == frames / 2) snd_playSoundEffect(18, -1);
                await delayUntil(until);
            }
            screen.copyPage(12, 2);
            screen.copyPage(2, 0);
            screen.curPage = cp;
        }

        public int uiBriarProperty(int stage = 2)
        {
            if (briarProps == null || briarLevel != currentLevel)
            {
                briarProps = new int[3];
                var palette = screen.getPalette(0);
                double[] sizes = { 0.4, 0.7, 1.0 };
                for (int i = 0; i < 3; i += 1)
                {
                    var shape = thornShape(palette, sizes[i]);
                    // Objects lying on a block are drawn from gameShapes when the property carries flag 0x40, and
                    // otherwise through GameShapeMap into itemShapes. The direct path is the one an added sprite
                    // can use: there is no room in GameShapeMap for an index the game never had.
                    int shpIndex = gameShapes.Length;
                    Array.Resize(ref gameShapes, shpIndex + 1);
                    gameShapes[shpIndex] = shape;
                    if (itemIconShapes.Length <= shpIndex) Array.Resize(ref itemIconShapes, shpIndex + 1);
                    itemIconShapes[shpIndex] = shape;
                    int prop = itemProperties.Count;
                    itemProperties.Add(new ItemProperty
                    {
                        nameStringId = 0, shpIndex = shpIndex, flags = 0x44, type = 0, // 0x40: drawn from gameShapes; 4: cannot be picked up
                        itemScriptFunc = 0xff, might = 0, skill = 0, protection = 0, unkB = 0,
                    });
                    extraItems = extraItems ?? new Dictionary<int, ExtraItemDef>();
                    extraItems[prop] = new ExtraItemDef { id = "briars", name = "Briars", effect = "none" };
                    briarProps[i] = prop;
                }
                briarProp = briarProps[2];
                briarLevel = currentLevel;
            }
            return briarProps[Math.Max(0, Math.Min(2, stage))];
        }

        // Briars on the block ahead: they hurt whatever stands in them until they wither.
        public async Task<int> castThorns(ExtraSpellDef def, ActiveSpell a)
        {
            // castSpell already refused the cast if a wall is in the way (flag 0x100). Passability is the
            // wrong test here: a monster standing on the block makes it "impassable", and that block is
            // exactly where the briars belong.
            int ahead = calcNewBlockPosition(currentBlock, currentDirection);
            int ticks = Js.Round((def.seconds[a.level] * 1000) / (15 * tickLength));
            var existing = thornBlocks.FirstOrDefault(t => t.block == ahead && t.level == currentLevel);
            if (existing != null) { existing.ticks = Math.Max(existing.ticks, ticks); existing.damage = def.damage[a.level]; }
            else
            {
                int item = 0;
                int prop = uiBriarProperty(0);
                item = makeItem(prop, 0, 0);
                if (item != -1)
                {
                    var (x, y) = calcCoordinates(ahead, 0x70, 0x70);   // near the middle of the block (dead centre is drawn twice)
                    int it = item;
                    queueAsync(() => setItemPosition(it, x, y, 0, 1));
                }
                else item = 0;
                thornBlocks.Add(new ThornBlock { block = ahead, level = currentLevel, ticks = ticks, damage = def.damage[a.level], charNum = a.charNum, wait = 0, item = item, stage = 0 });
            }
            await thornsAnimation(ahead * 31 + currentDirection);
            txt.printMessage(0, "Briars burst from the floor.");
            sceneUpdateRequired = true;
            return 1;
        }

        public void processThorns()
        {
            if (thornRestore != null && thornBlocks != null) { thornBlocks.AddRange(thornRestore); thornRestore = null; }
            if (thornBlocks == null || thornBlocks.Count == 0) return;
            foreach (var t in thornBlocks)
            {
                if (t.level != currentLevel) continue;
                // the sprites are made per level (its palette): briars met again after a load or a level change
                // point at this level's set
                if (t.item != 0 && t.item < itemsInPlay.Length)
                {
                    int prop = uiBriarProperty(t.stage);
                    if (itemsInPlay[t.item].itemPropertyIndex != prop) { itemsInPlay[t.item].itemPropertyIndex = prop; sceneUpdateRequired = true; }
                }
                // growing in its first moments, shrinking in its last
                int want = t.ticks <= 1 ? 0 : t.ticks <= 2 ? 1 : 2;
                int grown = Math.Min(t.stage + 1, want);
                if (t.item != 0 && grown != t.stage && briarProps != null && briarLevel == currentLevel)
                {
                    t.stage = grown;
                    itemsInPlay[t.item].itemPropertyIndex = briarProps[grown];
                    levelBlockProperties[t.block].direction = 5;
                    sceneUpdateRequired = true;
                }
                t.ticks -= 1;
                if (t.wait > 0) { t.wait -= 1; continue; }
                if (monstersOnBlockHp(t.block) != 0)
                {
                    inflictMagicalDamageForBlock(t.block, t.charNum, t.damage, 0x80);
                    t.wait = 12; // about three seconds between bites
                    sceneUpdateRequired = true;
                }
            }
            foreach (var t in thornBlocks)
            {
                if (t.ticks > 0 || t.item == 0) continue;
                deleteItem(t.item); // the briars wither away
                levelBlockProperties[t.block].direction = 5;
                t.item = 0;
                sceneUpdateRequired = true;
            }
            thornBlocks = thornBlocks.Where(t => t.ticks > 0).ToList();
        }

        // Slip behind the creature ahead and strike it without a to-hit roll.
        public async Task<int> castBackstab(ExtraSpellDef def, ActiveSpell a)
        {
            int dir = currentDirection;
            int ahead = calcNewBlockPosition(currentBlock, dir);
            int target = 0;
            int o = levelBlockProperties[ahead].assignedObjects;
            int guard = 0;
            while ((o & 0x8000) != 0 && guard++ < 32)
            {
                if (monsters[o & 0x7fff].hitPoints > 0) { target = o; break; }
                o = monsters[o & 0x7fff].nextAssignedObject;
            }
            if (target == 0) { txt.printMessage(2, "There is nobody ahead to creep behind."); return 1; }
            // "no wall behind the monster": the far side of its block has to be open floor. Monsters
            // standing there do not count as a wall, so the wall flags are what is tested.
            int behind = calcNewBlockPosition(ahead, dir);
            if (behind == ahead || testWallFlag(behind, dir, 1) || !levelBlockProperties[behind].walls.Any())
            {
                txt.printMessage(2, "There is no room behind it.");
                return 1;
            }
            currentBlock = behind;
            currentDirection = dir ^ 2; // turn to face the creature's back
            (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
            sceneDefaultUpdate = 1;
            snd_playSoundEffect(18, -1);
            gui_drawScene(0);
            sceneUpdateRequired = true;
            // the strike: Guardian's spectral blade, its rise and its slash (GUARDIAN.WSA)
            try
            {
                var screen = this.screen;
                int cp = screen.curPage;
                screen.curPage = 2;
                screen.copyPage(0, 2);
                screen.copyPage(2, 12);
                var blade = openWsa("GUARDIAN.WSA", 0);
                snd_playSoundEffect(156, -1);
                await playSpellAnimation(blade, 14, 37, 2, 112, 0, null, false);
                snd_playSoundEffect(176, -1);
                await playSpellAnimation(blade, 38, 48, 4, 112, 0, null, true);
                screen.curPage = cp;
                updateDrawPage2();
            }
            catch (QuitException) { throw; }
            catch (Exception) { /* no animation: the strike still lands */ }
            // inflictDamage takes a might value, not a final number, so the hit is measured rather than
            // guessed: the message must not report something other than what the creature lost.
            int might = Js.Round(calcInflictableDamage(a.charNum, target, 1) * def.bonus[a.level]);
            int before = monsters[target & 0x7fff].hitPoints;
            inflictDamage(target, Math.Max(2, might), a.charNum, 0, 0);
            int dealt = Math.Max(0, before - monsters[target & 0x7fff].hitPoints);
            txt.printMessage(0, $"{characters[a.charNum].name} strikes from behind for {dealt}.");
            gui_drawCharPortraitWithStats(a.charNum);
            return 1;
        }
    }
}
