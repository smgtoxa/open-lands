// Unity build only: what the pointer is over in the 3D view, for the cursor (a sword over a monster in reach, an
// open gauntlet over the floor when something lies there) and for click-to-attack: a left click on a monster on the
// block ahead makes the selected hero attack it (the F key's attack); one further away says it is out of reach.
using System.Linq;
using Lol;

namespace LolHost
{
    public sealed partial class Web
    {
        string sceneCursor = "default";
        bool sceneClickTaken;   // the click went to an attack: its mouseup is not the game's either

        // a monster drawn under the playfield point (the last scene drawn), the nearest first
        Monster monsterUnder(int x, int y)
        {
            if (engine == null || engine.monsters == null) return null;
            Monster best = null;
            foreach (var m in engine.monsters)
            {
                if (m == null || m.properties == null || m.hitPoints <= 0 || m.mode >= 13 || m.block == 0) continue;
                if (m.drawSerial != engine.sceneSerial || m.drawW <= 0) continue;
                if (x < m.drawX - m.drawW / 2 || x > m.drawX + m.drawW / 2 || y < m.drawY || y > m.drawY + m.drawH) continue;
                if (best == null || m.drawW > best.drawW) best = m;   // the bigger sprite is the nearer one
            }
            return best;
        }

        bool sceneMouseFree => engine != null && playing && engine.itemInHand == 0 && engine.needSceneRestore == 0
            && engine.tim?.currentTim == null && (engine.updateFlags & 3) == 0;

        int frontBlock => engine.calcNewBlockPosition(engine.currentBlock, engine.currentDirection);

        // mode 1: standing townsfolk and guards (clicking them is the game's: talk, scripts)
        bool attackable(Monster m) => m != null && m.mode != 1;

        void sceneHover(int x, int y)
        {
            string want = "default";
            if (sceneMouseFree && x >= SCENE[0] && x < SCENE[0] + SCENE[2] && y >= SCENE[1] && y < SCENE[1] + SCENE[3])
            {
                var m = monsterUnder(x, y);
                if (attackable(m) && m.block == frontBlock) want = "attack";
                else if (m == null && y >= 62 && engine.uiFloorItems().Count > 0) want = "grab";
            }
            if (want == sceneCursor) return;
            sceneCursor = want;
            cssCursorKey = "";   // updateCssCursor shows it on its next round
        }

        // true: the click was an attack (or a word about reach) and is not passed to the game
        bool sceneClick(int x, int y, int button)
        {
            if (button != 0 || !sceneMouseFree) return false;
            var m = monsterUnder(x, y);
            if (!attackable(m)) return false;
            if (m.block == frontBlock)
            {
                engine.queueAsync(() => engine.quickAttack());
                return true;
            }
            gameUi.message($"The {engine.monsterName(m).ToLowerInvariant()} is out of reach. Step up to it to strike.", "system");
            return true;
        }
    }
}
