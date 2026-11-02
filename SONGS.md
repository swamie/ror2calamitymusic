# Song list and where to get it

The quickest way to fill every boss folder is `tools/get_songs.py`. It copies songs you already have on your PC into the right folders with the right names. It does not download anything from YouTube.

## Step 1: get the songs

**A. Steam Workshop (free, has every in-game boss theme).** In Steam, subscribe to these tModLoader mods, then open tModLoader once so Steam downloads them. You don't need to play Terraria.

| Mod | What it has | Link |
|---|---|---|
| Calamity Mod Music | every Calamity boss theme (DM DOKURO, PinpinNeon, CDMusic) | https://steamcommunity.com/sharedfiles/filedetails/?id=2824688266 |
| Infernum Mode Music | the Infernum versions (PinpinNeon) | https://steamcommunity.com/sharedfiles/filedetails/?id=3458992153 |
| Wrath of the Gods | Avatar of Emptiness, Nameless Deity, Mars themes | https://steamcommunity.com/sharedfiles/filedetails/?id=2995193002 |

tModLoader itself is free on Steam.

**B. Bandcamp ($7 or more, supports DM DOKURO directly).** DM DOKURO's album *The Tale of a Cruel World* has his Calamity boss themes as full-length tracks:
https://dmdokuro.bandcamp.com/album/the-tale-of-a-cruel-world

Pick **MP3 320** or **Ogg Vorbis** when downloading. FLAC also works if you have ffmpeg installed.

**Listening links (official channels):**
- DM DOKURO: https://www.youtube.com/@DMDOKURO
- PinpinNeon: https://www.youtube.com/channel/UCfbpIwaXAPuE3kTFq1tEqDA
- CDMusic: https://www.youtube.com/channel/UCOItBqiAAbWEdm21Mcv3g_Q
- Heart Plus Up!: https://www.youtube.com/@HeartPlusUp
- The Infernum OST playlist: https://www.youtube.com/playlist?list=PLi_Z42PVYivQd_jpP__Y0zNRK-vs1IT0C

## Step 2: run the script

You need Python 3 (https://www.python.org/downloads/). Start the game once with the mod installed so its folders exist, then:

```
python tools/get_songs.py
```

It finds your r2modman profile and the Workshop mods by itself. It asks for the Bandcamp zip if you have one, then copies and renames everything. Non-boss tracks from the music mods (biome themes and so on) go into `CalamityMusic/Unsorted` in case you want them.

Other options: `--dry-run` shows what it would do without copying anything, and `--music PATH` points it at a specific Music folder. Run `python tools/get_songs.py --help` for the rest.

Then in game: **Settings > Mod Options > Calamity Music > Rescan Music Folder**.

## The full list

What goes in each boss folder. Every track listed here is filled in by the script.

| Folder | Boss | Song | Composer | Workshop | Bandcamp |
|---|---|---|---|---|---|
| Calamity/DesertScourge | Desert Scourge | Guardian of the Former Seas | DM DOKURO | yes | yes |
| Calamity/Crabulon | Crabulon | Infestation (1NF3$+@+!0N) | DM DOKURO | yes | yes |
| Calamity/HiveMind | The Hive Mind | The Filthy Mind | DM DOKURO & SixteenInMono | yes | yes |
| Calamity/Perforators | The Perforators | Blood Coagulant | DM DOKURO & SixteenInMono | yes | yes |
| Calamity/SlimeGod | The Slime God | Return to Slime | DM DOKURO & SixteenInMono | yes | yes |
| Calamity/Cryogen | Cryogen | Antarctic Reinforcement | DM DOKURO | yes | yes |
| Calamity/AquaticScourge | Aquatic Scourge | Outcast of the Sulphurous Seas | DM DOKURO | yes | yes |
| Calamity/BrimstoneElemental | Brimstone Elemental | Left Alone | DM DOKURO | yes | yes |
| Calamity/CalamitasClone | Calamitas Clone | Raw, Unfiltered Calamity | DM DOKURO | yes | yes |
| Calamity/LeviathanAnahita | Leviathan and Anahita | Siren's War Cry, Forbidden Lullaby | DM DOKURO | yes | as "The Leviathan Trilogy" |
| Calamity/AstrumAureus | Astrum Aureus | Interstellar Stomper | DM DOKURO | yes | yes |
| Calamity/PlaguebringerGoliath | The Plaguebringer Goliath | Fly of Beelzebub | DM DOKURO | yes | yes |
| Calamity/Ravager | Ravager | Open Frenzy | DM DOKURO | yes | yes |
| Calamity/AstrumDeus | Astrum Deus | Pest of the Cosmos | DM DOKURO | yes | yes |
| Calamity/ProfanedGuardians | Profaned Guardians | Unholy Ambush | DM DOKURO | yes | yes |
| Calamity/Dragonfolly | The Dragonfolly | Murderswarm | DM DOKURO | yes | yes |
| Calamity/Providence | Providence | Unholy Insurgency | DM DOKURO | yes | yes |
| Calamity/StormWeaver | Storm Weaver | Servants of the Scourge (Type W) | DM DOKURO | yes | combined track |
| Calamity/CeaselessVoid | Ceaseless Void | Servants of the Scourge (Type V) | DM DOKURO | yes | combined track |
| Calamity/Signus | Signus | Servants of the Scourge (Type S) | DM DOKURO | yes | combined track |
| Calamity/Polterghast | Polterghast | Feral Amalgamation | DM DOKURO | yes | yes |
| Calamity/OldDuke | The Old Duke | Toxic Wisdom | DM DOKURO | yes | yes |
| Calamity/DevourerOfGods | Devourer of Gods | Scourge of the Universe, UNIVERSAL COLLAPSE | DM DOKURO | yes | yes |
| Calamity/Yharon | Yharon | Infernal Catharsis (Rebirth), Roar of the Jungle Dragon | DM DOKURO | yes | yes |
| Calamity/ExoMechs | Exo Mechs | Inferior Fabrications | PinpinNeon | yes | no |
| Calamity/SupremeCalamitas | Supreme Witch, Calamitas | Stained, Brutal Calamity | DM DOKURO | yes | yes |
| Calamity/PrimordialWyrm | Primordial Wyrm | Threats of the Ocean Floor | DM DOKURO | yes | yes |
| Calamity/BossRush | Boss Rush | Boss Rush Tier 1 to 4 | CDMusic | yes | no |
| Calamity/MoonEscape | (moon escape after Mithrix) | UNIVERSAL COLLAPSE | DM DOKURO | yes | yes |
| Infernum/SupremeCalamitas | Supreme Witch, Calamitas | Scars of Calamity | PinpinNeon | Infernum Mode Music | no |
| Infernum/ExoMechs | Exo Mechs | Catastrophic Fabrications, Zenith Fabrications | PinpinNeon | Infernum Mode Music | no |
| Infernum/PrimordialWyrm | Primordial Wyrm | Vocitus Terminus | PinpinNeon | Infernum Mode Music | no |
| Extensions/AvatarOfEmptiness | Avatar of Emptiness (used for Voidling) | PARADISE PARASITE | ENNWAY! | Wrath of the Gods | no |
| Extensions/NamelessDeity | Nameless Deity (used for False Son) | TWISTED GARDEN | Heart Plus Up! & ENNWAY! | Wrath of the Gods | no |
| Extensions/Mars | XG-07 Mars (used for Xi Construct) | RAMifications | Moonburn | Wrath of the Gods | no |
| Extensions/Generic | any boss without a better match | Edenic Whispers | ENNWAY! | Wrath of the Gods | no |
| Extensions/Astrageldon | Astrageldon (used for Twisted Scavengers) | Cosmic Disgust | DM DOKURO | no | yes |

Notes:
- The Bandcamp album has the three Sentinel themes as one "Servants of the Scourge" track. The script puts it in all three folders.
- Remixes on the album (the 8-bit, 16-bit and Structured mixes, Siren's Call, the Eulogy) are skipped, because they aren't fight themes.
- If a folder stays empty, that boss uses the Generic folder or any other song, so you never get silence.
- The script reads songs from the Workshop mods by their file names inside the mod. If a future update renames those files and some songs land in `Unsorted`, move them into the right folder by hand. The table above says where.
