# Porting the web page (host) to Unity: rules

The browser build's page code — `$LANDS/src/main.mjs` (the web project, `~/lands` by default) and `src/platform/*.mjs` — is
ported **1:1** to C# in `Assets/Scripts/Host/` (namespace `LolHost`). The page's HTML and CSS are
already converted: `index.html` → `Assets/Scripts/Host/PageMarkup.g.cs` (every element, id, class,
attribute), `styles.css` → `Assets/Resources/page.uss`. The engine is `Lol.LandsOfLore`
(`Engine/`, the 1:1 port of `src/game`, **same member names as the JS**; see docs/port/PORTING.md
and docs/port/index.md for its API). Keep every JS function, its name, its statement order and its
comments. Do not redesign or "improve". If the JS has a quirk, keep it.

## Unity constraints: C# 9

Unity compiles this code: **C# 9 only**. No file-scoped namespaces, no `global using`, no records,
no `init` accessors, no `required`, no raw string literals, no list patterns, no `static abstract`.
Wrap everything in `namespace LolHost { ... }`. `System.Text.Json` / `System.Text.Json.Nodes` are
available (for `JSON.parse` / `JSON.stringify` of free-form data use `JsonNode`).

## Files already written (read them first; do not change them)

- `Assets/Scripts/Dom/*.cs` — the DOM over UI Toolkit. **Read DomExt.cs's header**: the mapping
  table (`el.hidden = x` → `el.SetHidden(x)`, `textContent` → `SetText/GetText`, `classList.toggle`
  → `ClassToggle`, `addEventListener` → `On`, `querySelector` → `Q`, `dataset` → `Dataset()`,
  `style.x` → `SetStyle("x", v)`, `setAttribute` → `SetAttr` ...). `Dom.El(tag, className, text)` is
  `document.createElement` + class + text. `document.querySelector(sel)` → `Web.Q(sel)` /
  `Dom.Q(sel)`. Elements are `VisualElement`; cast when you need the specific kind:
  `(CanvasEl)` for `<canvas>` (`width`, `height`, `getContext("2d")` → `Ctx2D` with fillRect,
  arc, fill, stroke, drawImage, putImageData, createImageData, fillText, ...), `(DomInput)` for
  `<input>` (`value`, `@checked`), `(DomSelect)` for `<select>` (`value`, `AddOption`,
  `ClearOptions`, `SetOptionHidden`), `(SvgEl)` / `SvgEl.Node(tag, attrs)` for SVG. Events arrive
  as `DomEvent` (`button` 0/1/2 like MouseEvent, `shiftKey`, `key` like KeyboardEvent.key,
  `offsetX/Y`, `clientX/Y`, `deltaY`, `dataTransfer.setData/getData/types`, `preventDefault()`,
  `stopPropagation()`, `target`).
- `Assets/Scripts/Host/Web.cs` — `public sealed partial class Web` **is main.mjs's module**: the
  browser services it needs: `sched` (the event loop), `timers.setTimeout/setInterval/clearTimeout/
  clearInterval/requestAnimationFrame`, `localStorage.getItem/setItem/removeItem`, `Web.now()` for
  `Date.now()`, `fetchBytes(path)` (async, `Task<byte[]>`), `JsObj.Get(payload, "field")` for the
  engine's event payloads (anonymous objects / dictionaries).
- `Assets/Scripts/Host/WebAudio.cs` — the engine's audio host (`engine.audioHost = audio`).
- `Assets/Scripts/Host/IndexedScreen.cs` — `src/platform/indexed-screen.mjs`.
- `Assets/Scripts/Host/HostMain.cs` — Unity entry: builds the page, calls `web.Init()` then
  `web.start()`, routes document keydown to `web.documentKeydown(DomEvent)` and page close to
  `web.pagehide()`. **The port must provide `public System.Threading.Tasks.Task start()`,
  `public void documentKeydown(DomEvent)` and `public void pagehide()`.**

## How main.mjs maps

- Module-level `const`/`let` → **fields** of `Web` (declared in the section file that declares them in
  the JS). A `const x = document.querySelector(...)` is a field assigned in that section's init method.
- Top-level statements (event listener registration, `setInterval`, code blocks `{ ... }` at module
  level) → put them, **in file order**, into methods `void init_<Section><NN>()` in your section file
  (e.g. `init_A01`, `init_A02`). `Web.Init()` runs all `init_*` methods ordered by name, so section
  A's run before B's, and so on — the same order as the JS module.
- Function declarations → methods with the same name (JS names, camelCase). Arrow-function consts
  → methods too. `async function` → `async Task` / `async Task<T>`; await exactly where the JS awaits;
  a JS call to an async function without await stays without await (`_ = f();`).
- Objects with a fixed shape → small classes (PascalCase names) in your file; free-form JSON that is
  stored in localStorage → `JsonNode` or a class with public fields serialised with
  `System.Text.Json` (`IncludeFields = true`), producing the same JSON keys the JS writes, so browser
  saves/exports stay compatible.
- `window.*` debugging handles, URL query parameters (`?nospeech`), `console.*`: skip (comment).
- WebGL filters (`FilterScreen`, `filter-screen.mjs`) and HD packs (`HdPack`, `HdScene`, `hd-scene.mjs`)
  are **not ported yet**: keep the calls but guard them with `null` objects (`filterScreen` stays null,
  `hdScene` null) so the rest works; say so in a comment.
- `engine` is `Lol.LandsOfLore` (a new one per game, as runGame does). Engine calls use the JS names.
  The engine's `ui` is `Action<string, object[]>`: `engine.ui = (name, args) => { switch (name) { case "message": ... } }`.
  The JS `wrapCount(name, fn)` monkeypatches become `engine.onCall = (name, args) => { ... }`.
  `engine.opcodes[i]` can be wrapped like the JS does (it is a mutable `Func<EmcState, Task<int>>[]`).
  `engine.openTalkArchive` is `Func<string, Task<ITalkArchive>>` — read the TLK file from the data
  folder into a `PakArchive` (no RemotePak needed).
- Global settings object `settings` → class `WebSettings` with **public fields named exactly as the JS
  keys** (voice, sfx, music, scroll, minimap, minimapRadius, difficulty, hd, autosave (string),
  autosaveLevel, keys (Dictionary<string,string>), uiFont, colorblind, mods, intro, hints, craft, flee,
  wear, respawn, inventorySize (string), randomizer, seed, ngplus (string)); it is declared by the
  section that owns `let settings` (Section E) — other sections just use `settings.x`.

## Platform modules

Each `src/platform/<name>.mjs` becomes a C# file `Assets/Scripts/Host/<PascalName>.cs`:
exported classes keep their names (`GameUi`, `CharScreen`, `FullMap`, `Minimap`, `NpcMemory`,
`IndexedScreen`), exported functions/consts become `public static` members of a static class named
after the file (`Saves`, `Store`, `CampStoreUi` (camp-store), `CraftingUi` (crafting), `DungeonRun`,
`ErrandItems`, `SpellWidget`, `Xbr`). A constructor taking an options object `{ a, b }` takes the
same values as C# parameters in that order. main.mjs calls them by those names.

## Check your work

Your files must at least compile together with everything already in `Assets/Scripts/Dom`,
`Assets/Scripts/Host/{Web,WebAudio,WebPlatform,IndexedScreen,HostMain,PageMarkup.g,CssInfo.g}.cs`
and the engine. Build in a scratch project: netstandard2.1, `LangVersion 9.0`, referencing
`Engine/Engine.csproj`, the Unity assemblies at
`/mnt/c/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll`,
`UnityEngine.UIElementsModule.dll`, `UnityEngine.AudioModule.dll`, `UnityEngine.ImageConversionModule.dll`,
`UnityEngine.TextRenderingModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.InputLegacyModule.dll`
(Private=false), plus stubs **in the scratch dir** for sections/modules other agents are writing.
Do not edit files you were not assigned.
