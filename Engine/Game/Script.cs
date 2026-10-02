// src/game/script.mjs (+ src/game/opcode-names.mjs): level script runners and the LoL EMC opcode table (script_lol.cpp).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lol
{
    // ItemProperty (allocItemPropertiesBuffer entries) is declared in Items.cs.

    public sealed partial class LandsOfLore
    {
        // Script opcode names in table order (script_lol.cpp), used for diagnostics. (opcode-names.mjs)
        public static readonly string[] OPCODE_NAMES =
        {
            "setWallType", "getWallType", "drawScene", "rollDice", "moveParty", "", "delay", "setGameFlag",
            "testGameFlag", "loadLevelGraphics", "loadBlockProperties", "loadMonsterShapes", "deleteHandItem",
            "allocItemPropertiesBuffer", "setItemProperty", "makeItem", "placeMoveLevelItem", "createLevelItem",
            "getItemPara", "getCharacterStat", "setCharacterStat", "loadLevelShapes", "closeLevelShapeFile", "",
            "loadDoorShapes", "initAnimStruct", "playAnimationPart", "freeAnimStruct", "getDirection",
            "characterSurpriseFeedback", "setMusicTrack", "setSequenceButtons", "setDefaultButtonState",
            "checkRectForMousePointer", "clearDialogueField", "setupBackgroundAnimationPart",
            "startBackgroundAnimation", "o1_hideMouse", "o1_showMouse", "fadeToBlack",
            "fadePalette", "loadBitmap", "stopBackgroundAnimation", "", "", "getGlobalScriptVar", "setGlobalScriptVar",
            "getGlobalVar", "setGlobalVar", "triggerDoorSwitch", "checkEquippedItemScriptFlags", "setDoorState",
            "updateBlockAnimations", "assignLevelDecorationShape", "resetBlockShapeAssignment", "copyRegion",
            "initMonster", "fadeClearSceneWindow", "fadeSequencePalette", "redrawPlayfield", "loadNewLevel",
            "getNearestMonsterFromCharacter", "dummy0", "loadMonsterProperties", "battleHitSkillTest", "inflictDamage",
            "", "", "moveMonster", "setupDialogueButtons", "giveTakeMoney", "checkMoney", "setScriptTimer",
            "createHandItem", "playAttackSound", "addRemoveCharacter", "giveItem", "", "loadTimScript", "runTimScript",
            "releaseTimScript", "initSceneWindowDialogue", "restoreAfterSceneWindowDialogue", "getItemInHand",
            "checkMagic", "giveItemToMonster", "loadLangFile", "playSoundEffect", "processDialogue", "stopTimScript",
            "getWallFlags", "changeMonsterStat", "getMonsterStat", "releaseMonsterShapes", "playCharacterScriptChat",
            "update", "playEnvironmentalSfx", "healCharacter", "drawExitButton", "loadSoundFile", "playMusicTrack",
            "deleteMonstersFromBlock", "countBlockItems", "characterSkillTest", "countAllMonsters", "playEndSequence",
            "stopPortraitSpeechAnim", "setPaletteBrightness", "calcInflictableDamage", "getInflictedDamage",
            "checkForCertainPartyMember", "printMessage", "deleteLevelItem", "calcInflictableDamagePerItem",
            "distanceAttack", "removeCharacterEffects", "checkInventoryFull", "moveBlockObjects", "", "",
            "addSpellToScroll", "playDialogueText", "playDialogueTalkText", "checkMonsterTypeHostility", "setNextFunc",
            "dummy1", "", "suspendMonster", "setScriptTextParameter", "triggerEventOnMouseButtonClick",
            "printWindowText", "countSpecificMonsters", "updateBlockAnimations2", "checkPartyForItemType", "blockDoor",
            "resetTimDialogueState", "getItemOnPos", "removeLevelItem", "savePage5", "restorePage5",
            "initDialogueSequence", "restoreAfterDialogueSequence", "setSpecialSceneButtons",
            "restoreButtonsAfterSpecialScene", "", "", "prepareSpecialScene", "restoreAfterSpecialScene",
            "assignCustomSfx", "", "findAssignedMonster", "checkBlockForMonster", "crossFadeRegion",
            "calcCoordinatesAddDirectionOffset", "resetPortraitsAndDisableSysTimer", "enableSysTimer",
            "checkNeedSceneRestore", "getNextActiveCharacter", "paralyzePoisonCharacter", "drawCharPortrait",
            "removeInventoryItem", "", "", "getAnimationLastPart", "assignSpecialGuiShape", "findInventoryItem",
            "restoreFadePalette", "calcNewBlockPosition", "getSelectedCharacter", "setHandItem", "drinkBezelCup",
            "changeItemTypeOrFlag", "placeInventoryItemInHand", "castSpell", "pitDrop", "increaseSkill",
            "paletteFlash", "restoreMagicShroud", "dummy1", "disableControls", "enableControls", "shakeScene",
            "gasExplosion", "calcNewBlockPosition", "crossFadeScene", "updateDrawPage2", "setMouseCursor",
            "characterSays", "queueSpeech", "getItemPrice", "getLanguage", "dummy0",
        };

        public TimScript[] activeTim;
        public int scriptCharacterCycle;
        public Func<TimScript, Span16, Task<int>>[] timIngameOpcodes;
        public Func<EmcState, Task<int>>[] opcodes;
        public bool scriptWaitsForDialogue;
        public bool gameFinished;
        // nextSpeechId / nextSpeaker: Sound.cs; specialGuiShapeY / specialGuiShapeMirrorFlag: Scene.cs.

        public void initScript()
        {
            opcodeNames = OPCODE_NAMES;
            activeTim = new TimScript[10];
            scriptCharacterCycle = 0;
            timIngameOpcodes = makeTimIngameOpcodes();
            opcodes = makeOpcodeTable();
        }

        // ---- runners ----
        public async Task runInitScript(string filename, int optionalFunc)
        {
            suspendScript = true;
            var script = loadScript(filename);
            await runScriptFunction(script, 0);
            if (optionalFunc != 0) await runScriptFunction(script, optionalFunc);
            suspendScript = false;
        }

        public async Task runInfScript(string filename)
        {
            scriptData = loadScript(filename);
            await runLevelScript(0x400, -1);
        }

        public async Task runLevelScript(int block, int flags)
        {
            await runLevelScriptCustom(block, flags, -1, 0, 0, 0);
        }

        /// <summary>Diagnostics (the harness's LOL_TRACE_OPS): every level script call, as headless_walk prints it.</summary>
        public Action<int, int> traceLevelScript;

        public async Task runLevelScriptCustom(int block, int flags, int charNum, int item, int reg3, int reg4)
        {
            traceLevelScript?.Invoke(block, flags);
            if (!suspendScript && scriptData != null)
            {
                await runScriptFunction(scriptData, block, (state) =>
                {
                    state.regs[0] = (short)flags;
                    state.regs[1] = (short)charNum;
                    state.regs[2] = (short)item;
                    state.regs[3] = (short)reg3;
                    state.regs[4] = (short)reg4;
                    state.regs[5] = (short)block;
                    state.regs[6] = (short)scriptDirection;
                    if ((state.entryFlags() & flags) == 0) state.ip = -1;
                });
            }
            checkSceneUpdateNeed(block);
        }

        public Func<EmcState, Task<int>>[] makeOpcodeTable()
        {
            var S = @static;
            int A(EmcState s, int i) => s.arg(i);
            string STR(EmcState s, int i) => s.argString(i);
            // a sync opcode, in the one delegate type the table holds
            Func<EmcState, Task<int>> Sync(Func<EmcState, int> f) => s => Task.FromResult(f(s));
            var impl = new Dictionary<string, Func<EmcState, Task<int>>>
            {
                ["setWallType"] = Sync((s) =>
                {
                    if (A(s, 2) != -1 && (wllWallFlags[A(s, 2)] & 4) != 0) deleteMonstersFromBlock(A(s, 0));
                    setWallType(A(s, 0), A(s, 1), A(s, 2));
                    return 1;
                }),
                ["getWallType"] = Sync((s) => (sbyte)levelBlockProperties[A(s, 0)].walls[A(s, 1) & 3]),
                ["drawScene"] = Sync((s) => { drawScene(A(s, 0)); return 1; }),
                ["rollDice"] = Sync((s) => rollDice(A(s, 0), A(s, 1))),
                ["moveParty"] = async (s) =>
                {
                    int mode = A(s, 0);
                    if (mode > 5 && mode < 10) mode = (mode - 6 - currentDirection) & 3;
                    var b = new Button { arg = 0, flags2 = 0 };
                    switch (mode)
                    {
                        case 0: await clickedUpArrow(b); break;
                        case 1: await clickedRightArrow(b); break;
                        case 2: await clickedDownArrow(b); break;
                        case 3: await clickedLeftArrow(b); break;
                        case 4: await clickedTurnLeftArrow(b); break;
                        case 5: await clickedTurnRightArrow(b); break;
                        case 10: case 11: case 12: case 13:
                        {
                            mode = Math.Abs(mode - 10 - currentDirection);
                            if (mode > 2) mode = (mode ^ 2) * -1;
                            while (mode != 0)
                            {
                                if (mode > 0) { await clickedTurnRightArrow(b); mode -= 1; }
                                else { await clickedTurnLeftArrow(b); mode += 1; }
                            }
                            break;
                        }
                        default: break;
                    }
                    return 1;
                },
                ["delay"] = async (s) => { await delay(A(s, 0) * tickLength, true); return 1; },
                ["setGameFlag"] = Sync((s) => { if (A(s, 1) != 0) setGameFlag(A(s, 0)); else resetGameFlag(A(s, 0)); return 1; }),
                ["testGameFlag"] = Sync((s) => (A(s, 0) < 0 ? 0 : queryGameFlag(A(s, 0)))),
                ["loadLevelGraphics"] = async (s) =>
                {
                    await loadLevelGraphics(STR(s, 0), A(s, 1), A(s, 2), A(s, 3) == -1 ? -1 : A(s, 3) & 0xffff, A(s, 4) == -1 ? -1 : A(s, 4) & 0xffff, A(s, 5) == -1 ? null : STR(s, 5));
                    return 1;
                },
                ["loadBlockProperties"] = Sync((s) => { loadBlockProperties(STR(s, 0)); return 1; }),
                ["loadMonsterShapes"] = Sync((s) => { loadMonsterShapes(STR(s, 0), A(s, 1), A(s, 2)); return 1; }),
                ["deleteHandItem"] = async (s) => { int r = itemInHand; deleteItem(itemInHand); await setHandItem(0); return r; },
                ["allocItemPropertiesBuffer"] = Sync((s) => { itemProperties = Enumerable.Range(0, A(s, 0)).Select(_ => new ItemProperty { nameStringId = 0, shpIndex = 0, flags = 0, type = 0, itemScriptFunc = 0, might = 0, skill = 0, protection = 0, unkB = 0 }).ToList(); return 1; }),
                ["setItemProperty"] = Sync((s) =>
                {
                    var tmp = itemProperties[A(s, 0)];
                    tmp.nameStringId = A(s, 1) & 0xffff;
                    tmp.shpIndex = A(s, 2) & 0xff;
                    tmp.type = A(s, 3) & 0xffff;
                    if (A(s, 0) == 264 && tmp.type == 5) tmp.type = 0;
                    tmp.itemScriptFunc = A(s, 4) & 0xff;
                    tmp.might = (sbyte)A(s, 5);
                    tmp.skill = A(s, 6) & 0xff;
                    tmp.protection = A(s, 7) & 0xff;
                    tmp.flags = A(s, 8) & 0xffff;
                    tmp.unkB = A(s, 9) & 0xffff;
                    return 1;
                }),
                ["makeItem"] = Sync((s) => makeItem(A(s, 0), A(s, 1), A(s, 2))),
                ["placeMoveLevelItem"] = async (s) => { await placeMoveLevelItem(A(s, 0), A(s, 1), A(s, 2), A(s, 3) & 0xff, A(s, 4) & 0xff, A(s, 5)); return 1; },
                ["createLevelItem"] = async (s) =>
                {
                    int item = makeItem(A(s, 0), A(s, 1), A(s, 2));
                    if (item == -1) return item;
                    await placeMoveLevelItem(item, A(s, 3), A(s, 4), A(s, 5), A(s, 6), A(s, 7));
                    return item;
                },
                ["getItemPara"] = Sync((s) =>
                {
                    if (A(s, 0) == 0) return 0;
                    var i = itemsInPlay[A(s, 0)];
                    var p = itemProperties[i.itemPropertyIndex];
                    switch (A(s, 1))
                    {
                        case 0: return i.block; case 1: return i.x; case 2: return i.y; case 3: return i.level; case 4: return i.itemPropertyIndex;
                        case 5: return i.shpCurFrame_flg; case 6: return p.nameStringId; case 8: return p.shpIndex; case 9: return p.type;
                        case 10: return p.itemScriptFunc; case 11: return p.might; case 12: return p.skill; case 13: return p.protection; case 14: return p.unkB;
                        case 15: return i.shpCurFrame_flg & 0x1fff; case 16: return p.flags; case 17: return (p.skill << 8) | (p.might & 0xff);
                        default: return -1;
                    }
                }),
                ["getCharacterStat"] = Sync((s) =>
                {
                    var c = characters[A(s, 0)];
                    int d = A(s, 2);
                    switch (A(s, 1))
                    {
                        case 0: return c.flags; case 1: return c.raceClassSex; case 5: return c.hitPointsCur; case 6: return c.hitPointsMax;
                        case 7: return c.magicPointsCur; case 8: return c.magicPointsMax; case 9: return c.itemProtection; case 10: return c.items[d];
                        case 11: return c.skillLevels[d] + c.skillModifiers[d]; case 12: return c.protectionAgainstItems[d];
                        case 13: return (d & 0x80) != 0 ? c.itemsMight[7] : c.itemsMight[d]; case 14: return c.skillModifiers[d]; case 15: return c.id;
                        default: return 0;
                    }
                }),
                ["setCharacterStat"] = Sync((s) =>
                {
                    var c = characters[A(s, 0)];
                    int d = A(s, 2);
                    int e = A(s, 3);
                    switch (A(s, 1))
                    {
                        case 0: c.flags = e; break; case 1: c.raceClassSex = e & 0x0f; break;
                        case 5: setCharacterMagicOrHitPoints(A(s, 0), 0, e, 0); break; case 6: c.hitPointsMax = e; break;
                        case 7: setCharacterMagicOrHitPoints(A(s, 0), 1, e, 0); break; case 8: c.magicPointsMax = e; break;
                        case 9: c.itemProtection = e; break; case 10: c.items[d] = 0; break; case 11: c.skillLevels[d] = (byte)e; break;
                        case 12: c.protectionAgainstItems[d] = (ushort)e; break; case 13: if ((d & 0x80) != 0) c.itemsMight[7] = (ushort)e; else c.itemsMight[d] = (ushort)e; break;
                        case 14: c.skillModifiers[d] = (sbyte)e; break; default: break;
                    }
                    return 0;
                }),
                ["loadLevelShapes"] = async (s) => { await loadLevelShpDat(STR(s, 0), STR(s, 1), true); return 1; },
                ["closeLevelShapeFile"] = Sync((s) => 1),
                ["loadDoorShapes"] = Sync((s) =>
                {
                    var shapes = loadShapeFile(STR(s, 0));
                    // shapes[i] past the end is undefined in JS
                    doorShapes[0] = A(s, 1) >= 0 && A(s, 1) < shapes.Length ? shapes[A(s, 1)] : null;
                    doorShapes[1] = A(s, 2) >= 0 && A(s, 2) < shapes.Length ? shapes[A(s, 2)] : null;
                    for (int i = 0; i < 20; i += 1)
                    {
                        wllWallFlags[i + 3] |= 7;
                        int t = i % 5;
                        if (t == 4) wllWallFlags[i + 3] &= 0xf8;
                        if (t == 3) wllWallFlags[i + 3] &= 0xfd;
                    }
                    if (A(s, 3) != 0) for (int i = 3; i < 13; i += 1) wllWallFlags[i] &= 0xfd;
                    if (A(s, 4) != 0) for (int i = 13; i < 23; i += 1) wllWallFlags[i] &= 0xfd;
                    return 1;
                }),
                ["initAnimStruct"] = async (s) => ((await tim.initAnimStruct(A(s, 1), STR(s, 0), A(s, 2), A(s, 3), A(s, 4), 0, A(s, 5))) != 0 ? 1 : 0),
                ["playAnimationPart"] = async (s) => { await tim.animator.playPart(A(s, 0), A(s, 1), A(s, 2), A(s, 3)); return 1; },
                ["freeAnimStruct"] = Sync((s) => (tim.freeAnimStruct(A(s, 0)) != 0 ? 1 : 0)),
                ["getDirection"] = Sync((s) => currentDirection),
                ["characterSurpriseFeedback"] = Sync((s) =>
                {
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((characters[i].flags & 1) == 0 || characters[i].id >= 0) continue;
                        int sid = -characters[i].id;
                        int sfx = sid == 1 ? 136 : sid == 5 ? 50 : sid == 8 ? 49 : sid == 9 ? 48 : 0;
                        if (sfx != 0) snd_playSoundEffect(sfx, -1);
                        return 1;
                    }
                    return 1;
                }),
                ["setMusicTrack"] = Sync((s) => { curMusicTheme = A(s, 0); return 1; }),
                ["setSequenceButtons"] = Sync((s) => { setSequenceButtons(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4)); return 1; }),
                ["setDefaultButtonState"] = Sync((s) => { setDefaultButtonState(); return 1; }),
                ["checkRectForMousePointer"] = Sync((s) => (mouseX >= A(s, 0) && mouseX <= A(s, 2) && mouseY >= A(s, 1) && mouseY <= A(s, 3) ? 1 : 0)),
                ["clearDialogueField"] = Sync((s) =>
                {
                    if (currentControlMode != 0 && !textEnabled()) return 1;
                    screen.setScreenDim(5);
                    var d = screen.getScreenDim(5);
                    screen.fillRect(d.sx, d.sy, d.sx + d.w - 2, d.sy + d.h - 2, d.col2);
                    txt.clearDim(4);
                    txt.resetDimTextPositions(4);
                    return 1;
                }),
                ["setupBackgroundAnimationPart"] = Sync((s) => { tim.animator.setupPart(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4), A(s, 5), A(s, 6), A(s, 7), A(s, 8), A(s, 9)); return 0; }),
                ["startBackgroundAnimation"] = Sync((s) => { tim.animator.start(A(s, 0), A(s, 1)); return 1; }),
                ["o1_hideMouse"] = Sync((s) => 1),
                ["o1_showMouse"] = Sync((s) => 1),
                ["fadeToBlack"] = async (s) => { await screen.fadeToBlack(10); return 1; },
                ["fadePalette"] = async (s) => { await screen.fadePalette(screen.getPalette(3), 10); screen.fadeFlag = 0; return 1; },
                ["loadBitmap"] = Sync((s) =>
                {
                    screen.loadBitmap(res.get(STR(s, 0)), 3, screen.getPalette(3));
                    if (A(s, 1) != 2) screen.copyPage(3, A(s, 1));
                    else screen.copyPage(3, 2);
                    return 1;
                }),
                ["stopBackgroundAnimation"] = Sync((s) => { tim.animator.stop(A(s, 0)); return 1; }),
                ["getGlobalScriptVar"] = Sync((s) => globalScriptVars[A(s, 0)]),
                ["setGlobalScriptVar"] = Sync((s) => { globalScriptVars[A(s, 0)] = (short)A(s, 1); return 1; }),
                ["getGlobalVar"] = Sync((s) =>
                {
                    switch (A(s, 0))
                    {
                        case 0: return currentBlock; case 1: return currentDirection; case 2: return currentLevel; case 3: return itemInHand;
                        case 4: return brightness; case 5: return credits; case 6: return globalScriptVars2[A(s, 1)]; case 8: return updateFlags;
                        case 9: return lampOilStatus; case 10: return sceneDefaultUpdate; case 11: return compassBroken; case 12: return drainMagic;
                        case 13: return speechEnabled() ? (textEnabled() ? 2 : 1) : 0; case 14: return tim.abortFlag; default: return 0;
                    }
                }),
                ["setGlobalVar"] = async (s) =>
                {
                    int a = A(s, 1) & 0xffff;
                    int b = A(s, 2) & 0xffff;
                    switch (A(s, 0))
                    {
                        case 0:
                            currentBlock = b;
                            (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
                            updateAutoMap(currentBlock);
                            break;
                        case 1: currentDirection = b; break;
                        case 2: currentLevel = b & 0xff; break;
                        case 3: await setHandItem(b); break;
                        case 4: brightness = b & 0xff; break;
                        case 5: credits = b; break;
                        case 6: globalScriptVars2[a] = (ushort)b; break;
                        case 8:
                            updateFlags = b;
                            if (b == 1)
                            {
                                if (!textEnabled() || (currentControlMode & 2) == 0) timerUpdatePortraitAnimations(1);
                                disableSysTimer(2);
                            }
                            else enableSysTimer(2);
                            break;
                        case 9: lampOilStatus = b & 0xff; break;
                        case 10: sceneDefaultUpdate = b & 0xff; gui_toggleButtonDisplayMode(0, 0); break;
                        case 11: compassBroken = a & 0xff; break;
                        case 12: drainMagic = a & 0xff; break;
                        default: break;
                    }
                    return 1;
                },
                ["triggerDoorSwitch"] = Sync((s) => { processDoorSwitch(A(s, 0), A(s, 1)); return 1; }),
                ["checkEquippedItemScriptFlags"] = Sync((s) =>
                {
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((characters[i].flags & 1) == 0) continue;
                        for (int ii = 0; ii < 4; ii += 1)
                        {
                            int f = itemProperties[itemsInPlay[characters[i].items[ii]].itemPropertyIndex].itemScriptFunc;
                            if (f == 0 || f == 2) return 1;
                        }
                    }
                    return 0;
                }),
                ["setDoorState"] = Sync((s) =>
                {
                    var l = levelBlockProperties[A(s, 0)];
                    if (A(s, 1) != 0) l.flags = (l.flags & 0xef) | 0x20;
                    else l.flags &= 0xdf;
                    return 1;
                }),
                ["updateBlockAnimations"] = Sync((s) =>
                {
                    int block = A(s, 0);
                    int wall = A(s, 1);
                    setWallType(block, wall, levelBlockProperties[block].walls[wall == -1 ? 0 : wall] == A(s, 2) ? A(s, 3) : A(s, 2));
                    return 0;
                }),
                ["assignLevelDecorationShape"] = Sync((s) => assignLevelDecorationShapes(A(s, 0))),
                ["resetBlockShapeAssignment"] = Sync((s) =>
                {
                    sbyte v = (sbyte)A(s, 0);
                    for (int i = 3; i < 8; i += 1) wllShapeMap[i] = v;
                    for (int i = 13; i < 18; i += 1) wllShapeMap[i] = v;
                    return 1;
                }),
                ["copyRegion"] = Sync((s) => { screen.copyRegion(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4), A(s, 5), A(s, 6), A(s, 7), true); return 1; }),
                ["initMonster"] = Sync((s) => initMonster(Enumerable.Range(0, 11).Select(i => s.sp + i < 100 ? A(s, i) : 0).ToArray())),
                ["fadeClearSceneWindow"] = async (s) => { await screen.fadeClearSceneWindow(10); return 1; },
                ["fadeSequencePalette"] = async (s) =>
                {
                    var p3 = screen.getPalette(3);
                    Js.Set(p3, Js.Slice(screen.getPalette(0), 128 * 3), 128 * 3);
                    screen.loadSpecialColors(p3);
                    await screen.fadePalette(p3, 10);
                    screen.fadeFlag = 0;
                    return 1;
                },
                ["redrawPlayfield"] = async (s) =>
                {
                    if (screen.fadeFlag != 2) await screen.fadeClearSceneWindow(10);
                    gui_drawPlayField();
                    await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
                    screen.fadeFlag = 0;
                    return 1;
                },
                ["loadNewLevel"] = async (s) =>
                {
                    await screen.fadeClearSceneWindow(10);
                    screen.fillRect(112, 0, 288, 120, 0);
                    disableSysTimer(2);
                    for (int i = 0; i < 8; i += 1)
                    {
                        var f = flyingObjects[i];
                        if (f.enable == 0 || f.objectType != 0) continue;
                        await endObjectFlight(f, f.x, f.y, 1);
                    }
                    completeDoorOperations();
                    generateTempData();
                    currentBlock = A(s, 1);
                    currentDirection = A(s, 2);
                    (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
                    await loadLevel(A(s, 0));
                    enableSysTimer(2);
                    s.ip = -1;
                    return 1;
                },
                ["getNearestMonsterFromCharacter"] = Sync((s) => getNearestMonsterFromCharacter(A(s, 0))),
                ["dummy0"] = Sync((s) => 0),
                ["loadMonsterProperties"] = Sync((s) => { loadMonsterProperties(Enumerable.Range(0, 42).Select(i => A(s, i)).ToArray()); return 1; }),
                ["battleHitSkillTest"] = Sync((s) => battleHitSkillTest(A(s, 0), A(s, 1), A(s, 2))),
                ["inflictDamage"] = Sync((s) =>
                {
                    if (A(s, 0) == -1) for (int i = 0; i < 4; i += 1) inflictDamage(i, A(s, 1), A(s, 2), A(s, 3), A(s, 4));
                    else inflictDamage(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4));
                    return 1;
                }),
                ["moveMonster"] = Sync((s) =>
                {
                    var m = monsters[A(s, 0)];
                    if (m.mode == 1 || m.mode == 2)
                    {
                        (m.destX, m.destY) = calcCoordinates(A(s, 1), A(s, 2), A(s, 3));
                        m.destDirection = A(s, 4) << 1;
                        if (m.x != m.destX || m.y != m.destY) setMonsterDirection(m, calcMonsterDirection(m.x, m.y, m.destX, m.destY));
                    }
                    return 1;
                }),
                ["setupDialogueButtons"] = Sync((s) => { setupDialogueButtons(A(s, 0), getLangString(A(s, 1)), getLangString(A(s, 2)), getLangString(A(s, 3))); return 1; }),
                ["giveTakeMoney"] = async (s) => { int c = A(s, 0); if (c >= 0) await giveCredits(c, 1); else await takeCredits(-c, 1); return 1; },
                ["checkMoney"] = Sync((s) => (A(s, 0) > credits ? 0 : 1)),
                ["setScriptTimer"] = Sync((s) =>
                {
                    int id = 0x50 + A(s, 0);
                    if (A(s, 1) != 0) { timerEnable(id); timerSetCountdown(id, A(s, 1)); }
                    else timerDisable(id);
                    return 1;
                }),
                ["createHandItem"] = async (s) => { if (itemInHand != 0) return 0; await setHandItem(makeItem(A(s, 0), A(s, 1), A(s, 2))); return 1; },
                ["playAttackSound"] = Sync((s) =>
                {
                    int[] sounds = { 12, 62, 63 };
                    int d = A(s, 0);
                    if ((d < 70 || d > 74) && (d < 81 || d > 89) && (d < 93 || d > 97) && (d < 102 || d > 106)) snd_playSoundEffect(sounds[itemProperties[d].skill & 3], -1);
                    else snd_playSoundEffect(12, -1);
                    return 1;
                }),
                ["addRemoveCharacter"] = Sync((s) =>
                {
                    int id = A(s, 0);
                    if (id < 0)
                    {
                        id = -id;
                        for (int i = 0; i < 4; i += 1)
                        {
                            if ((characters[i].flags & 1) == 0 || characters[i].id != id) continue;
                            characters[i].flags &= ~1;
                            calcCharPortraitXpos();
                            if (selectedCharacter == i) selectedCharacter = 0;
                            break;
                        }
                    }
                    else addCharacter(id);
                    if (updateFlags == 0)
                    {
                        gui_enableDefaultPlayfieldButtons();
                        gui_drawPlayField();
                    }
                    return 1;
                }),
                ["giveItem"] = Sync((s) =>
                {
                    int item = makeItem(A(s, 0), A(s, 1), A(s, 2));
                    if (addItemToInventory(item)) return 1;
                    deleteItem(item);
                    return 0;
                }),
                ["loadTimScript"] = Sync((s) =>
                {
                    if (activeTim[A(s, 0)] != null) return 1;
                    activeTim[A(s, 0)] = tim.load($"{STR(s, 1)}.TIM", timIngameOpcodes);
                    return 1;
                }),
                ["runTimScript"] = async (s) => await tim.exec(activeTim[A(s, 0)], A(s, 1) != 0),
                ["releaseTimScript"] = Sync((s) => { activeTim[A(s, 0)] = null; return 1; }),
                ["initSceneWindowDialogue"] = async (s) => { await initSceneWindowDialogue(A(s, 0)); return 1; },
                ["restoreAfterSceneWindowDialogue"] = async (s) => { await restoreAfterSceneWindowDialogue(A(s, 0)); return 1; },
                ["getItemInHand"] = Sync((s) => itemInHand),
                ["checkMagic"] = Sync((s) => checkMagic(A(s, 0), A(s, 1), A(s, 2))),
                ["giveItemToMonster"] = Sync((s) => { if (A(s, 0) == -1) return 0; giveItemToMonster(monsters[A(s, 0)], A(s, 1)); return 1; }),
                ["loadLangFile"] = Sync((s) => { levelLangFile = res.get($"{STR(s, 0)}.{languageExt}"); return 1; }),
                ["playSoundEffect"] = Sync((s) => { snd_playSoundEffect(A(s, 0), -1); return 1; }),
                // Scripts spin on this until a button is chosen; yield a frame so input can arrive.
                ["processDialogue"] = async (s) => { int r = await processDialogue(); scriptWaitsForDialogue = r == 0; if (r == 0) { timerUpdate(); update(); await delay(tickLength); } return r; },
                ["stopTimScript"] = Sync((s) => { tim.stopAllFuncs(activeTim[A(s, 0)]); return 1; }),
                ["getWallFlags"] = Sync((s) => wllWallFlags[levelBlockProperties[A(s, 0)].walls[A(s, 1) & 3]]),
                ["changeMonsterStat"] = Sync((s) =>
                {
                    if (A(s, 0) == -1) return 1;
                    var m = monsters[A(s, 0) & 0x7fff];
                    int d = A(s, 2);
                    switch (A(s, 1))
                    {
                        case 0: setMonsterMode(m, d); break;
                        case 1: m.hitPoints = d; break;
                        case 2:
                        {
                            var (x, y) = calcCoordinates(d, m.x & 0xff, m.y & 0xff);
                            if (walkMonsterCheckDest(x, y, m, 7) == 0) placeMonster(m, x, y);
                            break;
                        }
                        case 3: setMonsterDirection(m, d << 1); break;
                        case 6: m.flags |= d; break;
                        default: break;
                    }
                    return 1;
                }),
                ["getMonsterStat"] = Sync((s) =>
                {
                    if (A(s, 0) == -1) return 0;
                    var m = monsters[A(s, 0) & 0x7fff];
                    switch (A(s, 1))
                    {
                        case 0: return m.mode; case 1: return m.hitPoints; case 2: return m.block; case 3: return m.facing; case 4: return m.type;
                        case 5: return m.properties != null ? m.properties.hitPoints : 0; case 6: return m.flags; case 7: return m.properties != null ? m.properties.flags : 0;
                        case 8: return m.properties != null ? monsterAnimType[m.properties.shapeIndex] : 0; default: return 0;
                    }
                }),
                ["releaseMonsterShapes"] = Sync((s) => { for (int i = 0; i < 3; i += 1) releaseMonsterShapes(i); return 0; }),
                ["playCharacterScriptChat"] = async (s) =>
                {
                    snd_stopSpeech(true);
                    stopPortraitSpeechAnim();
                    return await playCharacterScriptChat(A(s, 0), A(s, 1), 1, getLangString(A(s, 2)) ?? "", s, null, 3);
                },
                ["playEnvironmentalSfx"] = Sync((s) => { snd_processEnvironmentalSoundEffect(A(s, 0), A(s, 1) == -1 ? currentBlock : A(s, 1)); return 1; }),
                // Scripts poll input in tight update loops; yield a frame so the host can deliver events.
                ["update"] = async (s) => { update(); await delay(0); return 1; },
                ["healCharacter"] = async (s) =>
                {
                    if (A(s, 3) != 0) await processMagicHeal(A(s, 0), A(s, 1));
                    else
                    {
                        increaseCharacterHitpoints(A(s, 0), A(s, 1), true);
                        if (A(s, 2) != 0) gui_drawCharPortraitWithStats(A(s, 0));
                    }
                    return 1;
                },
                ["drawExitButton"] = Sync((s) =>
                {
                    int[] printPara = { 0x90, 0x78, 0x0c, 0x9f, 0x80, 0x1e };
                    int cp = screen.curPage;
                    screen.curPage = 0;
                    var cf = screen.setFont("6");
                    int x = printPara[3 * A(s, 0)] << 1;
                    int y = printPara[3 * A(s, 0) + 1];
                    int offs = printPara[3 * A(s, 0) + 2];
                    string str = getLangString(0x4033);
                    int w = screen.textWidth(str);
                    int hButton = screen.fontHeight() + 3;
                    gui_drawBox(x - offs - w, y - hButton, w + offs, hButton, 136, 251, 252);
                    screen.printText(str, x - (offs >> 1) - w, y - hButton + 2, 144, 0);
                    if (A(s, 1) != 0) screen.drawGridBox(x - offs - w + 1, y - hButton + 1, w + offs - 2, hButton - 2, 1);
                    screen.setFont(cf);
                    screen.curPage = cp;
                    return 1;
                }),
                ["loadSoundFile"] = Sync((s) => { snd_loadSoundFile(A(s, 0)); return 1; }),
                ["playMusicTrack"] = Sync((s) => snd_playTrack(A(s, 0))),
                ["deleteMonstersFromBlock"] = Sync((s) => { deleteMonstersFromBlock(A(s, 0)); return 1; }),
                ["countBlockItems"] = Sync((s) =>
                {
                    int o = levelBlockProperties[A(s, 0)].assignedObjects;
                    int res = 0;
                    while (o != 0)
                    {
                        if ((o & 0x8000) == 0) res += 1;
                        o = findObject(o).nextAssignedObject;
                    }
                    return res;
                }),
                ["characterSkillTest"] = Sync((s) =>
                {
                    int skill = A(s, 0);
                    int n = countActiveCharacters();
                    int m = 0;
                    int c = 0;
                    for (int i = 0; i < n; i += 1)
                    {
                        int v = characters[i].skillModifiers[skill] + characters[i].skillLevels[skill] + 25;
                        if (v > m) { m = v; c = i; }
                    }
                    return rollDice(1, 100) > m ? -1 : c;
                }),
                ["countAllMonsters"] = Sync((s) => monsters.Count((m) => m.hitPoints > 0 && m.mode != 13)),
                ["playEndSequence"] = async (s) =>
                {
                    // olol_playEndSequence: the ending cinematic for the lead character, then the game is over.
                    int id = characters[0].id;
                    int c = id == -9 ? 1 : id == -5 ? 3 : id == -1 ? 2 : 0;
                    while (snd_updateCharacterSpeech() != 0) await delay(tickLength);
                    events.Clear();
                    Js.Fill(screen.getPalette(1), (byte)0);
                    try { await showOutro(c); }
                    catch (QuitException) { throw; }
                    catch (Exception error) { log($"outro: {error.Message}"); }
                    gameFinished = true;
                    quit = true;
                    return 0;
                },
                ["stopPortraitSpeechAnim"] = Sync((s) => { snd_stopSpeech(true); stopPortraitSpeechAnim(); return 1; }),
                ["setPaletteBrightness"] = async (s) =>
                {
                    int old = brightness;
                    brightness = A(s, 0);
                    if (A(s, 1) == 1) await setPaletteBrightness(screen.getPalette(0), A(s, 0), lampEffect);
                    return old;
                },
                ["calcInflictableDamage"] = Sync((s) => calcInflictableDamage(A(s, 0), A(s, 1), A(s, 2))),
                ["getInflictedDamage"] = Sync((s) => rollDice(2, A(s, 0))),
                ["checkForCertainPartyMember"] = Sync((s) => (characters.Any((c) => (c.flags & 9) != 0 && c.id == A(s, 0)) ? 1 : 0)),
                ["printMessage"] = Sync((s) =>
                {
                    int safe(int i) => s.sp + i < 100 ? A(s, i) : 0;
                    int snd = safe(2);
                    txt.printMessage(safe(0), formatString(getLangString(safe(1)), safe(3), safe(4), safe(5), safe(6), safe(7), safe(8), safe(9)));
                    if (snd >= 0) snd_playSoundEffect(snd, -1);
                    return 1;
                }),
                ["deleteLevelItem"] = async (s) =>
                {
                    if (itemsInPlay[A(s, 0)].block != 0) await removeLevelItem(A(s, 0), itemsInPlay[A(s, 0)].block);
                    deleteItem(A(s, 0));
                    return 1;
                },
                ["calcInflictableDamagePerItem"] = Sync((s) => calcInflictableDamagePerItem(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4))),
                ["distanceAttack"] = async (s) =>
                {
                    int fX = A(s, 3);
                    int fY = A(s, 4);
                    if ((A(s, 8) & 0x8000) == 0) fX = fY = 0x80;
                    var (x, y) = calcCoordinates(A(s, 2), fX, fY);
                    if (await launchObject(A(s, 0), A(s, 1), x, y, A(s, 5), A(s, 6) << 1, A(s, 7), A(s, 8), 0x3f)) return 1;
                    deleteItem(A(s, 1));
                    return 0;
                },
                ["removeCharacterEffects"] = Sync((s) => { removeCharacterEffects(characters[A(s, 0)], A(s, 1), A(s, 2)); return 1; }),
                ["checkInventoryFull"] = Sync((s) => (inventory.Any((i) => i != 0) ? 0 : 1)),
                ["moveBlockObjects"] = async (s) =>
                {
                    int o = levelBlockProperties[A(s, 0)].assignedObjects;
                    int res = 0;
                    int level = A(s, 2);
                    int destBlock = A(s, 1);
                    int runScript = A(s, 4);
                    int includeMonsters = A(s, 3);
                    int includeItems = A(s, 5);
                    if (currentLevel == 21 && level == 21 && destBlock == 0x3e0) { level = 20; destBlock = 0x0247; }
                    while (o != 0)
                    {
                        int l = o;
                        o = findObject(o).nextAssignedObject;
                        if ((l & 0x8000) != 0)
                        {
                            if (includeMonsters == 0) continue;
                            l &= 0x7fff;
                            var m = monsters[l];
                            setMonsterMode(m, 14);
                            checkSceneUpdateNeed(m.block);
                            placeMonster(m, 0, 0);
                            res = 1;
                        }
                        else
                        {
                            if ((itemsInPlay[l].shpCurFrame_flg & 0x4000) == 0 || includeItems == 0) continue;
                            await placeMoveLevelItem(l, level, destBlock, itemsInPlay[l].x & 0xff, itemsInPlay[l].y & 0xff, itemsInPlay[l].flyingHeight);
                            res = 1;
                            if (runScript == 0 || level != currentLevel) continue;
                            await runLevelScriptCustom(destBlock, 0x80, -1, l, 0, 0);
                        }
                    }
                    return res;
                },
                ["addSpellToScroll"] = async (s) => { await addSpellToScroll(A(s, 0), A(s, 1)); return 1; },
                ["playDialogueText"] = async (s) => { await txt.printDialogueText2(3, getLangString(A(s, 0)) ?? "", s, null, 1); return 1; },
                ["playDialogueTalkText"] = async (s) =>
                {
                    int track = A(s, 0);
                    // JS passes a third argument (0) that snd_playCharacterSpeech(id, speaker) ignores
                    if (!snd_playCharacterSpeech(track, 0) || textEnabled()) await txt.printDialogueText2(4, getLangString(track) ?? "", s, null, 1);
                    return 1;
                },
                ["checkMonsterTypeHostility"] = Sync((s) =>
                {
                    for (int i = 0; i < 30; i += 1)
                    {
                        if (A(s, 0) != monsters[i].type && A(s, 0) != -1) continue;
                        return monsters[i].mode == 1 ? 0 : 1;
                    }
                    return 1;
                }),
                ["setNextFunc"] = Sync((s) => { nextScriptFunc = A(s, 0); return 1; }),
                ["dummy1"] = Sync((s) => 1),
                ["suspendMonster"] = Sync((s) =>
                {
                    var m = monsters[A(s, 0) & 0x7fff];
                    setMonsterMode(m, 14);
                    checkSceneUpdateNeed(m.block);
                    placeMonster(m, 0, 0);
                    return 1;
                }),
                ["setScriptTextParameter"] = Sync((s) => { txt.scriptTextParameter = A(s, 0); return 1; }),
                // Scripts spin on this without calling update(); yield a frame when nothing was clicked yet.
                ["triggerEventOnMouseButtonClick"] = async (s) =>
                {
                    gui_notifyButtonListChanged();
                    snd_updateCharacterSpeech();
                    var ev = events.Find((e) => e.type == "mousedown" || e.type == "key");
                    removeInputTop();
                    if (ev == null) { timerUpdate(); update(); await delay(tickLength); return 0; }
                    int evt = A(s, 0);
                    if (evt != 0)
                    {
                        events.Add(evt == 65 ? new InputEvent { type = "mousedown", x = mouseX, y = mouseY, button = 1 } : evt == 66 ? new InputEvent { type = "mousedown", x = mouseX, y = mouseY, button = 2 } : new InputEvent { type = "key", key = "Enter" });
                        preserveEvents = true;
                        seqTrigger = 1;
                    }
                    return 1;
                },
                ["printWindowText"] = async (s) =>
                {
                    int dim = A(s, 0);
                    int flg = A(s, 1);
                    screen.setScreenDim(dim);
                    if ((flg & 1) != 0) txt.clearCurDim();
                    if ((flg & 3) != 0) txt.resetDimTextPositions(dim);
                    await txt.printDialogueText2(dim, getLangString(A(s, 2)) ?? "", s, null, 3);
                    return 1;
                },
                ["countSpecificMonsters"] = Sync((s) =>
                {
                    int types = 0;
                    int cnt = 0;
                    while (A(s, cnt) != -1 && cnt < 16) types |= 1 << A(s, cnt++);
                    return monsters.Count((m) => ((1 << m.type) & types) != 0 && m.mode < 14);
                }),
                ["updateBlockAnimations2"] = Sync((s) =>
                {
                    int numFrames = A(s, 3);
                    int curFrame = A(s, 2) % numFrames;
                    setWallType(A(s, 0), A(s, 1), A(s, 4 + curFrame));
                    return 0;
                }),
                ["checkPartyForItemType"] = Sync((s) =>
                {
                    int p = A(s, 1);
                    if (A(s, 2) == 0)
                    {
                        for (int i = 0; i < inventory.Length; i += 1) if (inventory[i] != 0 && itemsInPlay[inventory[i]].itemPropertyIndex == p) return 1;
                        if (itemsInPlay[itemInHand].itemPropertyIndex == p) return 1;
                    }
                    int last = A(s, 0) == -1 ? 3 : A(s, 0);
                    int first = A(s, 0) == -1 ? 0 : A(s, 0);
                    for (int i = first; i <= last; i += 1) if (itemEquipped(i, p)) return 1;
                    return 0;
                }),
                ["blockDoor"] = Sync((s) => { blockDoor = A(s, 0); return blockDoor; }),
                ["resetTimDialogueState"] = Sync((s) => { tim.resetDialogueState(activeTim[A(s, 0)]); return 1; }),
                ["getItemOnPos"] = Sync((s) =>
                {
                    int pX = A(s, 1);
                    if (pX != -1) pX &= 0xff;
                    int pY = A(s, 2);
                    if (pY != -1) pY &= 0xff;
                    int o = A(s, 3) != 0 || emcLastItem == -1 ? A(s, 0) : emcLastItem;
                    emcLastItem = levelBlockProperties[o].assignedObjects;
                    while (emcLastItem != 0)
                    {
                        if ((emcLastItem & 0x8000) != 0 || (pX != -1 && (itemsInPlay[emcLastItem].x & 0xff) != pX) || (pY != -1 && (itemsInPlay[emcLastItem].y & 0xff) != pY))
                        {
                            o = emcLastItem & 0x7fff;
                            emcLastItem = levelBlockProperties[o].assignedObjects;
                            continue;
                        }
                        return emcLastItem;
                    }
                    return 0;
                }),
                ["removeLevelItem"] = async (s) => { await removeLevelItem(A(s, 0), A(s, 1)); return 1; },
                ["savePage5"] = Sync((s) => 1),
                ["restorePage5"] = Sync((s) => { for (int i = 0; i < 6; i += 1) tim.freeAnimStruct(i); return 1; }),
                ["initDialogueSequence"] = Sync((s) => { initDialogueSequence(A(s, 0), A(s, 1)); return 1; }),
                ["restoreAfterDialogueSequence"] = Sync((s) => { restoreAfterDialogueSequence(A(s, 0)); return 1; }),
                ["setSpecialSceneButtons"] = Sync((s) => { setSpecialSceneButtons(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4)); return 1; }),
                ["restoreButtonsAfterSpecialScene"] = Sync((s) => { gui_specialSceneRestoreButtons(); return 1; }),
                ["prepareSpecialScene"] = async (s) => { await prepareSpecialScene(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4), A(s, 5)); return 1; },
                ["restoreAfterSpecialScene"] = async (s) => await restoreAfterSpecialScene(A(s, 0), A(s, 1), A(s, 2), A(s, 3)),
                ["assignCustomSfx"] = Sync((s) =>
                {
                    string c = STR(s, 0);
                    int i = A(s, 1);
                    if (string.IsNullOrEmpty(c) || i > 250) return 0;
                    int t = S.IngameSfxIndex[i << 1];
                    if (t == 0xffff) return 0;
                    ingameSoundList[t] = c;
                    return 0;
                }),
                ["findAssignedMonster"] = Sync((s) =>
                {
                    int o = A(s, 1) == -1 ? levelBlockProperties[A(s, 0)].assignedObjects : findObject(A(s, 1)).nextAssignedObject;
                    while (o != 0)
                    {
                        if ((o & 0x8000) != 0) return o & 0x7fff;
                        o = findObject(o).nextAssignedObject;
                    }
                    return -1;
                }),
                ["checkBlockForMonster"] = Sync((s) =>
                {
                    int id = A(s, 1) | 0x8000;
                    int o = levelBlockProperties[A(s, 0)].assignedObjects;
                    while ((o & 0x8000) != 0)
                    {
                        if (id == 0xffff || (id & 0xffff) == o) return o & 0x7fff;
                        o = findObject(o).nextAssignedObject;
                    }
                    return -1;
                }),
                ["crossFadeRegion"] = async (s) => { await screen.crossFadeRegion(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4), A(s, 5), A(s, 6), A(s, 7)); return 1; },
                ["calcCoordinatesAddDirectionOffset"] = Sync((s) =>
                {
                    var (x, y) = calcCoordinatesAddDirectionOffset(A(s, 0) & 0xffff, A(s, 1) & 0xffff, A(s, 2));
                    return A(s, 3) != 0 ? x : y;
                }),
                ["resetPortraitsAndDisableSysTimer"] = Sync((s) => { resetPortraitsAndDisableSysTimer(); return 1; }),
                ["enableSysTimer"] = Sync((s) => { needSceneRestore = 0; enableSysTimer(2); return 1; }),
                ["checkNeedSceneRestore"] = Sync((s) => needSceneRestore),
                ["getNextActiveCharacter"] = Sync((s) =>
                {
                    if (A(s, 0) != 0) scriptCharacterCycle = 0;
                    else scriptCharacterCycle += 1;
                    while (scriptCharacterCycle < 4)
                    {
                        if ((characters[scriptCharacterCycle].flags & 1) != 0) return scriptCharacterCycle;
                        scriptCharacterCycle += 1;
                    }
                    return -1;
                }),
                ["paralyzePoisonCharacter"] = Sync((s) => paralyzePoisonCharacter(A(s, 0), A(s, 1), A(s, 2), A(s, 3), A(s, 4))),
                ["drawCharPortrait"] = Sync((s) => { if (A(s, 0) == -1) gui_drawAllCharPortraitsWithStats(); else gui_drawCharPortraitWithStats(A(s, 0)); return 1; }),
                ["removeInventoryItem"] = Sync((s) =>
                {
                    int itemType = A(s, 0);
                    for (int i = 0; i < inventory.Length; i += 1)
                    {
                        if (inventory[i] == 0 || itemsInPlay[inventory[i]].itemPropertyIndex != itemType) continue;
                        inventory[i] = 0;
                        gui_drawInventory();
                        return 1;
                    }
                    for (int i = 0; i < 4; i += 1)
                    {
                        if ((characters[i].flags & 1) == 0) continue;
                        for (int ii = 0; ii < 11; ii += 1)
                        {
                            if (characters[i].items[ii] == 0 || itemsInPlay[characters[i].items[ii]].itemPropertyIndex != itemType) continue;
                            characters[i].items[ii] = 0;
                            return 1;
                        }
                    }
                    return 0;
                }),
                ["getAnimationLastPart"] = Sync((s) => tim.animator.resetLastPart(A(s, 0))),
                ["assignSpecialGuiShape"] = Sync((s) =>
                {
                    if (A(s, 0) != 0)
                    {
                        specialGuiShape = levelDecorationShapes[levelDecorationProperties[wllShapeMap[A(s, 0)]].shapeIndex[A(s, 1)]];
                        specialGuiShapeX = A(s, 2);
                        specialGuiShapeY = A(s, 3);
                        specialGuiShapeMirrorFlag = A(s, 4);
                    }
                    else
                    {
                        specialGuiShape = null;
                        specialGuiShapeX = specialGuiShapeY = specialGuiShapeMirrorFlag = 0;
                    }
                    return 1;
                }),
                ["findInventoryItem"] = Sync((s) =>
                {
                    if (A(s, 0) == 0)
                    {
                        for (int i = 0; i < inventory.Length; i += 1) if (inventory[i] != 0 && itemsInPlay[inventory[i]].itemPropertyIndex == A(s, 2)) return 0;
                    }
                    int cur = A(s, 1);
                    int last = cur;
                    if (A(s, 1) == -1) { cur = 0; last = 4; }
                    for (; cur < last; cur += 1)
                    {
                        if ((characters[cur].flags & 1) == 0) continue;
                        for (int i = 0; i < 11; i += 1)
                        {
                            if (characters[cur].items[i] != 0 && itemsInPlay[characters[cur].items[i]].itemPropertyIndex == A(s, 2)) return cur;
                        }
                    }
                    return -1;
                }),
                ["restoreFadePalette"] = async (s) =>
                {
                    Js.Set(screen.getPalette(0), Js.Slice(screen.getPalette(1), 0, 128 * 3), 0);
                    await screen.fadePalette(screen.getPalette(0), 10);
                    screen.fadeFlag = 0;
                    return 1;
                },
                ["getSelectedCharacter"] = Sync((s) => selectedCharacter),
                ["setHandItem"] = async (s) => { await setHandItem(A(s, 0)); return 1; },
                ["drinkBezelCup"] = async (s) => { await drinkBezelCup(3 - A(s, 0), A(s, 1)); return 1; },
                ["changeItemTypeOrFlag"] = Sync((s) =>
                {
                    if (A(s, 0) < 1) return 0;
                    var i = itemsInPlay[A(s, 0)];
                    int val = A(s, 2);
                    if (A(s, 1) == 4) i.itemPropertyIndex = val;
                    else if (A(s, 1) == 15) i.shpCurFrame_flg = (i.shpCurFrame_flg & 0xe000) | (val & 0x1fff);
                    else val = -1;
                    return val;
                }),
                ["placeInventoryItemInHand"] = async (s) =>
                {
                    int itemType = A(s, 0);
                    int i = 0;
                    for (; i < inventory.Length; i += 1) if (inventory[i] != 0 && itemsInPlay[inventory[i]].itemPropertyIndex == itemType) break;
                    if (i == inventory.Length) return -1;
                    inventoryCurItem = i;
                    int r = itemInHand;
                    await setHandItem(inventory[i]);
                    inventory[i] = (ushort)r;
                    if (A(s, 1) != 0) gui_drawInventory();
                    return r;
                },
                ["castSpell"] = async (s) => await castSpell(A(s, 0), A(s, 1), A(s, 2)),
                ["pitDrop"] = async (s) =>
                {
                    if (A(s, 0) != 0)
                    {
                        gui_drawScene(2);
                        await pitDropScroll(9);
                        snd_playSoundEffect(-1, -1);
                        await shakeScene(30, 4, 0, 1);
                    }
                    else
                    {
                        int t = -1;
                        for (int i = 0; i < 4; i += 1)
                        {
                            var c = characters[i];
                            if ((c.flags & 1) == 0 || c.id >= 0) continue;
                            t = c.id == -1 ? 54 : c.id == -5 ? 53 : c.id == -8 ? 52 : c.id == -9 ? 51 : t;
                        }
                        screen.fillRect(112, 0, 288, 120, 0, 2);
                        snd_playSoundEffect(t, -1);
                        await pitDropScroll(12);
                    }
                    return 1;
                },
                ["increaseSkill"] = Sync((s) =>
                {
                    var c = characters[A(s, 0)];
                    int sk = A(s, 1);
                    int l = c.skillLevels[sk];
                    increaseExperience(A(s, 0), sk, S.ExpRequirements[l] - c.experiencePts[sk]);
                    return c.skillLevels[sk] - l;
                }),
                ["paletteFlash"] = async (s) =>
                {
                    var p1 = screen.getPalette(1);
                    var p2 = screen.getPalette(3);
                    generateFlashPalette(p1, p2, A(s, 0));
                    screen.loadSpecialColors(p1);
                    screen.loadSpecialColors(p2);
                    if (smoothScrollModeNormal != 0)
                    {
                        var ovl = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
                        ovl[1] = 6;
                        screen.copyRegion(112, 0, 112, 0, 176, 120, 0, 2);
                        screen.applyOverlay(112, 0, 176, 120, 0, ovl);
                    }
                    screen.setScreenPalette(p2);
                    await delay(2 * tickLength);
                    screen.setScreenPalette(p1);
                    if (smoothScrollModeNormal != 0) screen.copyRegion(112, 0, 112, 0, 176, 120, 2, 0);
                    return 0;
                },
                ["restoreMagicShroud"] = async (s) => { await restoreMagicShroud(); return 1; },
                ["disableControls"] = Sync((s) => gui_disableControls(A(s, 0))),
                ["enableControls"] = Sync((s) => gui_enableControls()),
                ["shakeScene"] = async (s) => { await shakeScene(A(s, 0), A(s, 1), A(s, 2), 1); return 1; },
                ["gasExplosion"] = async (s) => { await processGasExplosion(A(s, 0)); return 1; },
                ["calcNewBlockPosition"] = Sync((s) => calcNewBlockPosition(A(s, 0), A(s, 1))),
                ["crossFadeScene"] = async (s) =>
                {
                    gui_drawScene(2);
                    await screen.crossFadeRegion(112, 0, 112, 0, 176, 120, 2, 0);
                    updateDrawPage2();
                    return 1;
                },
                ["updateDrawPage2"] = Sync((s) => { updateDrawPage2(); return 1; }),
                ["setMouseCursor"] = Sync((s) => { if (A(s, 0) == 1) setMouseCursorToIcon(133); else setMouseCursorToItemInHand(); return 1; }),
                ["characterSays"] = Sync((s) =>
                {
                    if (A(s, 0) == -1) { snd_stopSpeech(true); return 1; }
                    if (A(s, 0) != -2) return characterSays(A(s, 0), A(s, 1), A(s, 2) != 0);
                    return snd_updateCharacterSpeech();
                }),
                ["queueSpeech"] = Sync((s) => { if (A(s, 0) != 0 && A(s, 1) != 0) { nextSpeechId = A(s, 0) + 1000; nextSpeaker = A(s, 1); } return 1; }),
                ["getItemPrice"] = Sync((s) =>
                {
                    int c = A(s, 0);
                    if (c < 0)
                    {
                        c = -c;
                        if (c < 50) return 50;
                        return (int)((c + 99) / 100.0) * 100;
                    }
                    for (int i = 0; i < 46; i += 1) if (S.ItemPrices[i] >= c) return S.ItemPrices[i];
                    return 0;
                }),
                ["getLanguage"] = Sync((s) => lang),
            };
            return OPCODE_NAMES.Select((name) => impl.TryGetValue(name, out var f) ? f : null).ToArray();
        }

        public Func<TimScript, Span16, Task<int>>[] makeTimIngameOpcodes()
        {
            // p is the TIM's parameter list (tim.mjs: param.subarray(1) of the Uint16Array avtl)
            Func<TimScript, Span16, Task<int>> Sync(Func<TimScript, Span16, int> f) => (t, p) => Task.FromResult(f(t, p));
            return new Func<TimScript, Span16, Task<int>>[]
            {
                async (tim, p) => { await initSceneWindowDialogue(p[0]); return 1; },
                async (tim, p) => { await restoreAfterSceneWindowDialogue(p[0]); return 1; },
                null,
                Sync((tim, p) =>
                {
                    int item = makeItem(p[0], p[1], p[2]);
                    if (addItemToInventory(item)) return 1;
                    deleteItem(item);
                    return 0;
                }),
                Sync((tim, p) =>
                {
                    if (p[0] == 1) currentDirection = p[1];
                    else if (p[0] == 0)
                    {
                        currentBlock = p[1];
                        (partyPosX, partyPosY) = calcCoordinates(currentBlock, 0x80, 0x80);
                    }
                    return 1;
                }),
                async (tim, p) =>
                {
                    var screen = this.screen;
                    switch (p[0])
                    {
                        case 0: await screen.fadeClearSceneWindow(10); break;
                        case 1:
                        {
                            var p3 = screen.getPalette(3);
                            Js.Set(p3, Js.Slice(screen.getPalette(0), 128 * 3), 128 * 3);
                            screen.loadSpecialColors(p3);
                            await screen.fadePalette(p3, 10);
                            screen.fadeFlag = 0;
                            break;
                        }
                        case 2: await screen.fadeToBlack(10); break;
                        case 3: screen.loadSpecialColors(screen.getPalette(3)); await screen.fadePalette(screen.getPalette(3), 10); screen.fadeFlag = 0; break;
                        case 4:
                            if (screen.fadeFlag != 2) await screen.fadeClearSceneWindow(10);
                            gui_drawPlayField();
                            await setPaletteBrightness(screen.getPalette(0), brightness, lampEffect);
                            screen.fadeFlag = 0;
                            break;
                        case 5: screen.loadSpecialColors(screen.getPalette(3)); await screen.fadePalette(screen.getPalette(1), 10); screen.fadeFlag = 0; break;
                        default: break;
                    }
                    return 1;
                },
                Sync((tim, p) => { screen.copyRegion(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], true); return 1; }),
                async (tim, p) => { await playCharacterScriptChat((short)p[0], p[1], 1, getLangString(p[2]) ?? "", null, p, 3); return 1; },
                Sync((tim, p) => { gui_drawScene(p[0]); return 1; }),
                Sync((tim, p) => { update(); return 1; }),
                Sync((tim, p) =>
                {
                    if (currentControlMode != 0 && !textEnabled()) return 1;
                    screen.setScreenDim(5);
                    var d = screen.curDim;
                    screen.fillRect(d.sx, d.sy, d.sx + d.w - 2, d.sy + d.h - 2, d.col2);
                    txt.clearDim(4);
                    txt.resetDimTextPositions(4);
                    return 1;
                }),
                Sync((tim, p) => { snd_loadSoundFile(p[0]); return 1; }),
                Sync((tim, p) => { snd_playTrack(p[0]); return 1; }),
                async (tim, p) =>
                {
                    // JS passes a third argument (0) that snd_playCharacterSpeech(id, speaker) ignores
                    if (!snd_playCharacterSpeech(p[0], 0) || textEnabled()) await txt.printDialogueText2(4, getLangString(p[0]) ?? "", null, p, 1);
                    return 1;
                },
                Sync((tim, p) => { snd_playSoundEffect(p[0], -1); return 1; }),
                Sync((tim, p) => { this.tim.animator.start(p[0], p[1]); return 1; }),
                Sync((tim, p) => { this.tim.animator.stop(p[0]); return 1; }),
            };
        }
    }
}
