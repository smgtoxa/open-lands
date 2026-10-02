// src/game/menu.mjs: In-game menus (GUI_LoL subset): main menu and death menu.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>menu.mjs item(id, x, y, w, h, key)</summary>
    public sealed class MenuItem
    {
        public int id, x, y, w, h;
        public string key;
    }

    /// <summary>menu.mjs mainMenu / deathMenu: { dim, title, items }</summary>
    public sealed class Menu
    {
        public ScreenDim dim;
        public int title;
        public MenuItem[] items;
    }

    public sealed partial class LandsOfLore
    {
        // Host hooks the page sets (main.mjs: engine.deathHook / saveHook / loadHook); read by menu.mjs.
        public Func<Task> deathHook;
        public Func<bool> saveHook;
        public Action loadHook;
    }

    public sealed class Gui
    {
        public LandsOfLore vm;
        public Screen screen;
        public Menu mainMenu;
        public Menu deathMenu;
        public Menu activeMenu;

        public Gui(LandsOfLore vm)
        {
            this.vm = vm;
            this.screen = vm.screen;
            var dim9 = vm.screen.getScreenDim(9);
            var dim11 = vm.screen.getScreenDim(11);
            MenuItem item(int id, int x, int y, int w, int h, string key = null) => new MenuItem { id = id, x = x, y = y, w = w, h = h, key = key };
            this.mainMenu = new Menu
            {
                dim = dim9, title = 0x4000,
                items = new[] { item(0x4001, 16, 23, 176, 15), item(0x4002, 16, 40, 176, 15), item(0x4003, 16, 57, 176, 15), item(0x4004, 16, 74, 176, 15),
                    item(0x42d9, 16, 91, 176, 15), item(0x4006, 16, 108, 176, 15), item(0x4005, 88, 127, 104, 15, "Escape") },
            };
            this.deathMenu = new Menu { dim = dim11, title = 0x4013, items = new[] { item(0x4006, 8, 30, 104, 15), item(0x4001, 176, 30, 104, 15) } };
        }

        public void drawMenu(Menu menu, int highlighted)
        {
            var s = this.screen;
            int x = menu.dim.sx << 3;
            int y = menu.dim.sy;
            int w = menu.dim.w << 3;
            int h = menu.dim.h;
            s.fillRect(x + 2, y + 2, x + w - 3, y + h - 3, 225);
            s.drawShadedBox(x, y, x + w - 1, y + h - 1, 223, 227);
            var of = s.setFont("9");
            string title = this.vm.getLangString(menu.title) ?? "";
            s.fprintString(title, x + (w >> 1), y + 6, 254, 0, 9);
            for (int i = 0; i < menu.items.Length; i += 1)
            {
                var it = menu.items[i];
                int x1 = x + it.x;
                int y1 = y + it.y;
                s.fillRect(x1, y1, x1 + it.w - 1, y1 + it.h - 1, 225);
                s.drawShadedBox(x1, y1, x1 + it.w - 1, y1 + it.h - 1, 223, 227);
                string label = this.vm.getLangString(it.id) ?? "";
                s.fprintString(label, x1 + (it.w >> 1), y1 + 3, i == highlighted ? 254 : 204, 0, 9);
            }
            s.setFont(of);
        }

        /// <summary>Returns the chosen item id, or null (JS null/undefined).</summary>
        public async Task<int?> runMenu(Menu menu)
        {
            var vm = this.vm;
            // The host page draws its own death screen when it provides one.
            if (menu == this.deathMenu && vm.deathHook != null)
            {
                await vm.deathHook();
                vm.restartRequested = true;
                return null;
            }
            var s = this.screen;
            var backup = s.copyRegionToBuffer(0, 0, 0, 320, 200);
            s.curPage = 0;
            int highlighted = -1;
            int? result = null;
            this.drawMenu(menu, highlighted);
            vm.removeInputTop();
            this.activeMenu = menu; // the host shows the whole 320x200 screen while a menu is up
            while (result == null && !vm.quit)
            {
                var ev = await vm.waitForInputEvent();
                int x = menu.dim.sx << 3;
                int y = menu.dim.sy;
                if (ev.type == "mousedown" || ev.type == "mouseup")
                {
                    int hit = Array.FindIndex(menu.items, (it) => ev.x >= x + it.x && ev.x < x + it.x + it.w && ev.y >= y + it.y && ev.y < y + it.y + it.h);
                    if (ev.type == "mousedown" && hit >= 0)
                    {
                        highlighted = hit;
                        this.drawMenu(menu, highlighted);
                    }
                    else if (ev.type == "mouseup" && hit >= 0 && hit == highlighted) result = menu.items[hit].id;
                }
                else if (ev.type == "key")
                {
                    var keyed = Array.Find(menu.items, (it) => it.key == ev.key);
                    if (keyed != null) result = keyed.id;
                }
            }
            this.activeMenu = null;
            switch (result)
            {
                case 0x4006:
                    if (menu == this.deathMenu) { vm.restartRequested = true; break; }
                    vm.txt.printMessage(0, "Quit is not available in the browser; reload the page instead.");
                    break;
                case 0x4001: // Load a game: the host restores the browser autosave
                    if (menu == this.deathMenu) vm.restartRequested = true;
                    else if (vm.loadHook != null) vm.loadHook();
                    else vm.txt.printMessage(0, "No saved game available.");
                    break;
                case 0x4002: // Save this game
                    if (vm.saveHook != null) vm.txt.printMessage(0, vm.saveHook() ? "Game saved." : "Saving is not available.");
                    else vm.txt.printMessage(0, "Saving is not available.");
                    break;
                case 0x4003: case 0x4004: case 0x42d9:
                    vm.txt.printMessage(0, "This menu is not implemented yet.");
                    break;
                default:
                    break;
            }
            s.copyBlockToPage(0, 0, 0, 320, 200, backup);
            vm.removeInputTop();
            return result;
        }
    }
}
