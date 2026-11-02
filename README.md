# Calamity Music for Risk of Rain 2

Turns every teleporter event and boss fight into a Calamity boss fight.

- When you hit the teleporter and it goes red, a bass-heavy buildup kicks in. The moment the boss spawns it drops straight into that boss's Calamity theme.
- Every Risk of Rain 2 boss has its own matching theme. Mithrix gets Supreme Calamitas, the Wandering Vagrant (a jellyfish) gets the aquatic Leviathan and Anahita theme, the Overloading Worm gets the Storm Weaver, and so on. Bosses can have several themes and one is picked at random each fight.
- Calamity, Infernum and Mixed modes. Infernum mode uses the Infernum version of a theme when you have one and the normal Calamity song when you don't, just like the real mod. Set it to Vanilla to get the normal Risk of Rain music back.
- A "Now Playing" banner in the style of the Calamity/Infernum music display (white text, blue outline, teal glow, music note) slides in when a theme starts and pulses with the bass.
- Live bass boost with a limiter so it hits hard without clipping.
- Starstorm 2 support (Wayfarer, Nemesis Commando, Nemesis Mercenary, Empyrean elites), plus automatic theme guesses for any other modded boss.
- Works in multiplayer. It runs on your own game only, nobody else needs it installed.

**The mod does not include any music.** Calamity and Infernum songs belong to their composers (DM DOKURO, PinpinNeon, ENNWAY! and others), so you add your own copies. There is a checklist below of which song goes where.

## Install

With r2modman or Thunderstore Mod Manager: Settings > Import local mod, pick `CalamityMusic-1.0.0.zip`. It needs **BepInExPack** and **Risk Of Options**; the manager installs both.

By hand: copy the `plugins/CalamityMusic` folder from the zip into `BepInEx/plugins/`, and install Risk Of Options the same way.

Start the game once. The mod creates its music folders and a guide.

## Adding songs

**Easiest:** follow [SONGS.md](SONGS.md). Subscribe to the free Calamity music mods on Steam Workshop (and/or buy DM DOKURO's album on Bandcamp), then run `python tools/get_songs.py`. It finds those songs on your PC and files every one into the right boss folder with the right name.

**By hand:** go to **Settings > Mod Options > Calamity Music > Open Music Folder**, or browse to

```
BepInEx/config/CalamityMusic/Music/
```

(In r2modman, use "Browse profile folder" to find `BepInEx`.) Songs live in the config folder on purpose so updating the mod never deletes them.

```
Music/
  Calamity/<Boss>/      songs for Calamity mode
  Infernum/<Boss>/      Infernum versions (Infernum mode falls back to Calamity for everything else)
  Extensions/<Boss>/    songs from Calamity add-on mods like Wrath of the Gods
  Buildups/             optional custom teleporter buildups
  HOW_TO_ADD_SONGS.txt  the full checklist
```

- Formats: `.ogg`, `.mp3`, `.wav`.
- Name files `Artist - Title`, for example `DM DOKURO - Stained, Brutal Calamity.ogg`. The banner shows exactly that. If a file has no dash in its name the mod reads the artist and title tags instead, and it strips YouTube leftovers like "(Official Audio)" or "[HQ]".
- Put more than one song in a folder and it picks a different one each fight.
- Optional tags in the file name, hidden from the banner:
  - `[start=0:42]` starts 42 seconds in, to skip a slow intro
  - `[loop=1:10]` when the song loops, it goes back to 1:10 instead of the very start
  - `[gain=-3]` makes that file 3 dB quieter (or `+` to boost it)
- New songs are picked up at the start of each stage, or press **Rescan Music Folder**.

If a boss's folder is empty the mod tries the `Generic` folder, then any song you have, so you get Calamity music even with only a few songs. You can turn that off.

### Song checklist

| Folder | Calamity boss | Song |
|---|---|---|
| SupremeCalamitas | Supreme Witch, Calamitas | DM DOKURO - Stained, Brutal Calamity |
| CalamitasClone | Calamitas Clone | DM DOKURO - Raw, Unfiltered Calamity |
| DevourerOfGods | The Devourer of Gods | DM DOKURO - Scourge of The Universe, DM DOKURO - UNIVERSAL COLLAPSE |
| Yharon | Yharon | DM DOKURO - Infernal Catharsis (Rebirth), DM DOKURO - Roar of the Jungle Dragon |
| ExoMechs | Exo Mechs | PinpinNeon - Inferior Fabrications |
| Providence | Providence | DM DOKURO - Unholy Insurgency |
| ProfanedGuardians | Profaned Guardians | DM DOKURO - Unholy Ambush |
| Polterghast | Polterghast | DM DOKURO - Feral Amalgamation |
| StormWeaver | Storm Weaver | DM DOKURO - Servants of the Scourge (Type W) |
| CeaselessVoid | Ceaseless Void | DM DOKURO - Servants of the Scourge (Type V) |
| Signus | Signus | DM DOKURO - Servants of the Scourge (Type S) |
| LeviathanAnahita | Leviathan and Anahita | DM DOKURO - Forbidden Lullaby, DM DOKURO - Siren's War Cry |
| AquaticScourge | Aquatic Scourge | DM DOKURO - Outcast of the Sulphurous Seas |
| PlaguebringerGoliath | The Plaguebringer Goliath | DM DOKURO - Fly of Beelzebub |
| HiveMind | The Hive Mind | DM DOKURO & SixteenInMono - The Filthy Mind |
| Ravager | Ravager | DM DOKURO - Open Frenzy |
| AstrumAureus | Astrum Aureus | DM DOKURO - Interstellar Stomper |
| AstrumDeus | Astrum Deus | DM DOKURO - Pest of the Cosmos |
| BrimstoneElemental | Brimstone Elemental | DM DOKURO - Left Alone |
| Dragonfolly | The Dragonfolly | DM DOKURO - Murderswarm |
| DesertScourge | Desert Scourge | DM DOKURO - Guardian of the Former Seas |
| SlimeGod | The Slime God | DM DOKURO & SixteenInMono - Return to Slime |
| Crabulon | Crabulon | DM DOKURO - Infestation (in game: 1NF3$+@+!0N) |
| Perforators | The Perforators | DM DOKURO & SixteenInMono - Blood Coagulant |
| Cryogen | Cryogen | DM DOKURO - Antarctic Reinforcement |
| OldDuke | The Old Duke | DM DOKURO - Toxic Wisdom |
| PrimordialWyrm | Primordial Wyrm | DM DOKURO - Threats of the Ocean Floor |
| BossRush | Boss Rush | CDMusic - Boss Rush tier tracks |
| MoonEscape | (moon escape after Mithrix, optional) | anything intense, e.g. UNIVERSAL COLLAPSE |
| Generic | anything without a better match | whatever you like |

**Infernum folder:** Infernum only has its own tracks for a few bosses, all by PinpinNeon: `SupremeCalamitas` (Scars of Calamity), `ExoMechs` (Catastrophic Fabrications, Zenith Fabrications) and `PrimordialWyrm` (Vocitus Terminus). Everything else uses the Calamity song.

**Extensions folder (Calamity add-on mods):** `AvatarOfEmptiness` (Wrath of the Gods, ENNWAY! - PARADISE PARASITE), `NamelessDeity` (Wrath of the Gods, Heart Plus Up! & ENNWAY! - TWISTED GARDEN), `Mars` (Wrath of the Gods, Moonburn - RAMifications), `Goozma` (Hunt of the Old God), `Astrageldon` (Catalyst). ENNWAY! - Edenic Whispers is the calm Wrath of the Gods Eternal Garden theme, so it goes in `Generic` rather than a boss folder.

## Which boss gets which theme

| Risk of Rain 2 boss | Theme folder(s) | Why |
|---|---|---|
| Mithrix (all phases) | SupremeCalamitas | the final boss gets the final boss theme |
| Voidling | AvatarOfEmptiness, DevourerOfGods | cosmic void horror |
| False Son | CalamitasClone, NamelessDeity | a false copy, like the Calamitas Clone |
| Aurelionite | Providence, ProfanedGuardians | golden and holy |
| Twisted Scavengers | AstrumDeus, Astrageldon | lunar / astral |
| Artifact Reliquary | CeaselessVoid | a floating orb that doesn't move |
| Wandering Vagrant | LeviathanAnahita, AquaticScourge | it's a jellyfish |
| Overloading Worm | StormWeaver | a lightning worm |
| Magma Worm | BrimstoneElemental | fire |
| Beetle Queen | PlaguebringerGoliath, HiveMind | insect queen |
| Clay Dunestrider | DesertScourge, SlimeGod, Goozma | dunes and tar |
| Stone Titan | Ravager | giant golem |
| Grovetender | Polterghast, Crabulon | wisps and spirits |
| Imp Overlord | Signus | teleporting assassin |
| Grandparent | Yharon, ProfanedGuardians | summons suns |
| Solus Control Unit | AstrumAureus | walking machine |
| Alloy Worship Unit, Xi Construct | ExoMechs (+ Mars) | machines |
| Scavenger | Ravager, Dragonfolly | big brute |
| Void Devastator, Void Jailer | CeaselessVoid, Signus | void |
| Starstorm 2 Wayfarer | ProfanedGuardians, Polterghast | ghostly lantern bearer, "Usher of Light" |
| Starstorm 2 Nemesis Commando | CalamitasClone | dark doppelganger |
| Starstorm 2 Nemesis Mercenary | Signus, CalamitasClone | vengeful blade duelist |
| Starstorm 2 Empyrean elites | BossRush | late-run gauntlet |

Bosses the mod has never seen (new DLC, other mods, "horde of many" teleporter events) get a theme guessed from their name the first time they appear: anything with "fire" in its name gets Brimstone Elemental, a "Solus" machine gets Exo Mechs, and so on. Every mapping, including those, is written to `BepInEx/config/CalamityMusic.BossThemes.cfg`. Edit it to change any boss. Use comma-separated folder names, your own folder names work too, and `Vanilla` keeps the normal game music for that boss.

## Settings

Everything is in **Settings > Mod Options > Calamity Music**:

- **Music**: Vanilla / Calamity / Infernum / Mixed. This is the main switch.
- **Music Volume**: the Calamity music volume. By default it also follows the game's Master and Music sliders.
- **Bass Boost** and **Bass Boost Frequency**: how hard the bass hits (0 dB turns it off).
- **Preview**: plays a buildup and drop into a random song right there in the menu so you can tune volume and bass.
- **Teleporter Buildup**: on/off, synth or your own buildup files, length, and how loud the drop impact is.
- **Teleporter Music Ends When**: keep the theme going until the teleporter is fully charged (default) or stop when the boss dies.
- **When A Song Ends**: loop it seamlessly, or crossfade into another song for the same boss.
- **Now Playing**: on/off, how long it stays, position, size and the bass pulse. "Test Banner" shows it.

While Calamity music plays the game's own music is faded out, and it fades back in afterwards. Your music volume setting is never changed.

## Troubleshooting

- **No music**: check that Music isn't set to Vanilla and that songs are in the right folder. The game log (`BepInEx/LogOutput.log`) lists how many songs were found and which boss mapped to which theme.
- **A song won't play**: the log says why. Low quality or odd formats can be converted to `.ogg` with any audio converter.
- **Too loud / too much bass**: lower Bass Boost or the Drop Impact. 6 to 8 dB is a good range.
- **Banner in the way**: change its position or turn it off under Now Playing.

## Building from source

You need the .NET 8 SDK. The game's libraries come from NuGet, so you don't need the game installed to build.

```
./build.sh        # Linux / macOS
.\build.ps1       # Windows PowerShell
```

The installable zip ends up in `dist/`. The offline tests (decoders, resampler, buildup timing, limiter, library, boss mapping) run with:

```
python3 tests/make_fixtures.py tests/fixtures
dotnet run --project tests/CalamityMusic.Tests -c Release -- tests/fixtures tests/out
```

## How it works

Risk of Rain 2 plays all its audio through Wwise and has Unity's audio turned off, so the mod has its own small audio engine. It decodes your files (NLayer for MP3, NVorbis for OGG), resamples them to 48 kHz, runs them through the bass boost and a look-ahead limiter, and plays them through Windows waveOut on its own thread (this also works under Proton). The teleporter state and boss lists it watches are synced to every player, so it behaves the same whether you host or join.
