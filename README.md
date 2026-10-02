# Open Lands

Open Lands plays **Lands of Lore: The Throne of Chaos** (Westwood Studios, 1993) from your own copy of the game,
on a C# port of an open game engine, with a new pixel-art interface and optional new features: a camp with
crafting, a stash and an imp trader, the Imp's Pit, a journal with a bestiary, item database, spell book and guide,
quick spells per hero, a full map, a textured world and more.

**The game's files are not included.** You need your own copy of Lands of Lore: The Throne of Chaos (for example
the GOG release). Open Lands asks where it is installed and reads its data from there.

## Download

Builds for Windows and Linux are on the [Releases](https://github.com/smgtoxa/open-lands/releases) page.

## Building

- Unity 6000.6.3f1 with Windows and Linux Build Support (Mono), and the .NET SDK for the engine.
- The web project this port follows (`LANDS`, `~/lands` by default) supplies a few pictures and the test tools.

```bash
bash build.sh        # the engine DLL, then the Windows player in Build/Windows
bash release.sh OUT  # the Windows and Linux release archives in OUT
bash tools/parity.sh # the C# engine against the web engine, scenario by scenario
```

The scripts were written for WSL on Windows; adjust their paths for your system.

## Layout

- `Engine/` - the game engine in C# (namespace `Lol`). `Engine/Audio/` holds the AdLib music driver and the
  Nuked OPL3 emulator port.
- `Assets/Scripts/Host/` - the game's page and interface on Unity UI Toolkit; `Assets/Scripts/Dom/` - the small
  DOM and CSS layer it runs on.
- `Assets/Resources/`, `Assets/StreamingAssets/` - interface art, fonts, the guide's text, camp pictures and
  textured-world materials (credited in [CREDITS.txt](CREDITS.txt)).
- `tools/` - art builders, the parity and interface test tools.

Not in the repository: the original game's data, the guide's screenshots (they show the original game; make
them locally with `tools/guide_shots.py`), and the downloaded art packs the art tools start from (each is listed
with its address and licence in CREDITS.txt).

## Licence

Open Lands is free software under the GNU General Public License, version 3 or later ([LICENSE](LICENSE)).
Its engine derives game behaviour and code from ScummVM's Kyra engine (GPLv3) and uses a port of the Nuked OPL3
emulator (LGPL 2.1 or later); see [NOTICE](NOTICE). Art and fonts made by others are listed with their licences
in [CREDITS.txt](CREDITS.txt).

The release builds run on the Unity player, which is proprietary software of Unity Technologies and not covered
by the GPL. Parts of Open Lands are derived from ScummVM, whose authors have not granted an exception for linking
with a proprietary engine. If you hold copyright in ScummVM's Kyra engine and object, please open an issue and the
builds will be withdrawn or changed.

Lands of Lore is a trademark of its respective owner. Open Lands is not affiliated with or endorsed by Electronic
Arts, Westwood Studios, GOG or ScummVM.
