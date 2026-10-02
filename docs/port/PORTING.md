# Porting the web engine to C#: rules

The browser build's engine (`$LANDS/src/game/*.mjs`, `src/formats/*.mjs`) is being
transliterated **1:1** into C# (`Engine/`, namespace `Lol`, netstandard2.1, `LangVersion latest`).
The web version is the reference: it works. The goal is that the C# engine does *exactly* what the
JS does, statement by statement, so a side-by-side trace (`tools/diff.sh`) comes out identical.
Do not redesign, simplify, "fix", or reorder. If the JS has a quirk or a bug, keep it.

## Files already written (read them; do not redefine their members)

- `Engine/Runtime/Scheduler.cs` – the event loop (awaits, `Sleep` = setTimeout).
- `Engine/Runtime/Js.cs` – JS arithmetic helpers (`Js.FloorDiv`, `Js.Round`, `Js.Ushr`, `Js.Fill`, `Js.Slice` ...).
- `Engine/Game/Engine.cs` – engine.mjs + lol.mjs: `LandsOfLore` core fields, timers, `delay`, `events`,
  `getLangString`, dice, `runLoop`, `gui_updateInput`, `preInit`, `startup`, `startupNew`, `update`.
- `Engine/Game/Types.cs` – shared records: `BlockObject`, `Item`, `Monster`, `MonsterProperty`,
  `FlyingObject`, `Character`, `LevelBlock`, `DecorationProperty`, `Button`, `Timer`, `Shape`,
  `InputEvent`, `ActiveSpell`.
- `Engine/Static/StaticData.g.cs` – generated `StaticData` (JS `this.static.X` is `@static.X`); record
  arrays are classes `S_<TableName>` (e.g. `S_ButtonDefs`, `S_SpellProperties`, `S_CharacterDefs`).
- `docs/port/index.md` – every JS method (module, params, **async or not**) and every `this.x` field
  with the module that owns it and its initial value. Use it to know whether a call you make into
  another module must be awaited, and the name/type of fields other modules own.

## Structure

- Every JS *mixin* module (`export const XMixin = {...}` merged into the engine) becomes a file
  `Engine/Game/<Module>.cs` containing `public sealed partial class LandsOfLore { ... }` in
  `namespace Lol`. `this` is the engine, as in JS.
- JS classes (`Screen`, `DosFont`, `TextDisplayer`, `TimInterpreter`, `TimAnimator`, `Gui` (menu.mjs),
  `WsaPlayer`, `PakArchive`, `EmcState`, `WsaMovie`, `LolScene`, `BinaryReader`) become C# classes of
  the same name. A JS class that keeps the engine as `this.vm` keeps a `public LandsOfLore vm` field.
- Module-level exported functions and constants: in a game module, `public static` members of
  `LandsOfLore` in that module's file (e.g. `KEY_CODES`, `decodeString1`). In a formats module, a
  `public static class` named after the file: `binary`->`Binary`, `cps`->`Cps`, `emc`->`Emc`,
  `lol-scene`->`LolSceneFile`, `lol-shapes`->`LolShapes`, `pak`->`Pak`, `wsa`->`WsaFile`.
  Game `wsa.mjs` functions go in `static class WsaGame`.
- Module-private helpers/constants: `static` members of the partial class, prefixed with the module
  name if the name is generic (e.g. `Scene_TABLE`), or `private` nested types.

## Names

- **Keep every JS identifier exactly** (camelCase methods and fields, `snd_playTrack`, `gui_drawScene`,
  `field_D`, `shpCurFrame_flg`...). It breaks C# style on purpose: the two sources must be diffable.
- A JS name that is a C# keyword gets `@`: `@static`, `@params`, `@object`, `@event`, `@base`, `@lock`,
  `@default`, `@string`, `@checked`, `@fixed`, `@operator`, `@ref`, `@out`, `@in`, `@is`, `@as`, `@new`.
- Record types you must invent (an object literal with a fixed shape that is not in Types.cs): a
  `public sealed class` with a PascalCase name derived from the JS factory/field (e.g. `TimAnimation`,
  `ScreenDim`, `WsaAnim`), fields named as the JS keys. Put it in your module's file.

## Types

| JS | C# |
|---|---|
| integer number (the default) | `int` |
| times, `getMillis()`, `nextRun`, `tickLength`, any value that can be fractional | `double` |
| `true`/`false` | `bool`; but a field the JS sets to 0/1 *and* tests for truthiness stays `int` |
| `Uint8Array` / `Int8Array` / `Uint16Array` / `Int16Array` / `Int32Array` / `Uint32Array` | `byte[]` / `sbyte[]` / `ushort[]` / `short[]` / `int[]` / `uint[]` |
| array of numbers, fixed length | `int[]`; if `push`/`splice`/`length =` are used: `List<int>` |
| array of objects | `T[]` or `List<T>` (same rule) |
| `Map` / `Set` / plain object used as a dictionary | `Dictionary<K,V>` / `HashSet<T>` / `Dictionary<string,V>` |
| `null` | `null` (reference types) or a sentinel the JS itself tests |
| `string` | `string` |

- Storing into a typed array needs a cast: `bytes[i] = (byte)v;` – C# wraps like the JS typed array
  (unchecked). Reading gives the element type; promote with `(int)` where the JS math relies on it.
- `x | 0`, `Math.trunc` -> `(int)`; `Math.floor(a / b)` -> `Js.FloorDiv(a, b)` (differs for negatives);
  `Math.round` -> `Js.Round`; `>>>` -> `Js.Ushr`; `(x << 16) >> 16` -> `(short)x`; `(x << 24) >> 24` -> `(sbyte)x`.
  JS `/` is float division: if the JS does `a / b` and then floors/truncates, reproduce exactly that.
- JS truthiness: `if (n)` for a number -> `if (n != 0)`; for an object -> `!= null`; `a || b` on numbers
  -> `a != 0 ? a : b`; on objects `a ?? b`.
- `undefined` parameters -> C# optional parameters with the JS default; `arr[i]` out of range in JS
  gives `undefined`: guard it the same way the JS behaves (usually treat as 0/null) instead of throwing.

## Async (most important)

- A JS `async` method -> `async Task` (result unused / none) or `async Task<T>`. Non-async stays
  non-async. Keep the exact set of async methods from `docs/port/index.md`.
- `await` exactly where the JS awaits, nowhere else. A JS call to an async function *without* await
  (fire and forget) stays without await: `_ = foo();` (it runs synchronously up to its first await,
  exactly like JS).
- `await this.delay(ms)` -> `await delay(ms)`; `new Promise(r => setTimeout(r, ms))` -> `await sched.Sleep(ms)`.
- Callback tables that mix sync and async entries (script opcodes, TIM opcodes, button callbacks) use
  one delegate type returning a task: `Func<EmcState, Task<int>>`, `Func<Button, Task<int>>`, and wrap
  sync entries: `s => Task.FromResult(syncCall(s))`.
- `if (r && typeof r.then === "function") r = await r` -> the delegate is async; just `await`.
- `throw new Error("quit")` / the quit path -> `throw new QuitException()`; always rethrow
  `QuitException` from any `catch`.

## Host boundaries (the only non-literal parts)

- `this.uiEmit(name, ...args)` -> `ui?.Invoke(name, new object[] { ... })` (the host handles it).
- `this.log(msg)`, `this.present()`, `this.presentHook`: as in Engine.cs.
- `localStorage` (meta.mjs): a host delegate `public Func<string, string> storageGet; public Action<string, string> storageSet;`.
- Audio (sound.mjs: AudioContext, AudioWorklet, buffers): keep all the *decisions* (which file, which
  track, volumes, queues, speech file names, timings) literally; replace the WebAudio calls with calls
  on `public IAudioHost audioHost` (define the interface in Sound.cs: e.g. `PlaySamples(float[] samples,
  int rate, double volume) -> object handle`, `StopSamples(handle)`, `AdlibPost(AdlibMessage)`,
  `bool Available`). The AdLib worklet itself is host side.
- Saves (savegame.mjs): the snapshot is JSON, the same shape the browser stores, via
  `System.Text.Json.Nodes` (`JsonObject`, `JsonArray`).

## Style

- Keep the JS comments (they explain the why). Keep statement order. One C# method per JS method.
- No new behaviour, no defensive rewrites. Where C# forces a choice (a type, a cast), add a short
  comment only if it is not obvious.
- Do not touch files you were not assigned. Do not build the whole solution expecting it to compile:
  other modules are being written at the same time. Do make your own files syntactically valid C#.
