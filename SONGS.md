# Song list and where to get it

## One click

In game: **Settings > Mod Options > Calamity Music > Get Songs**. You can also double-click `Get Calamity Songs.bat` in the mod's `tools` folder, or run `python tools/get_songs.py` from this repo.

That's it. The installer:
1. Finds your r2modman / Thunderstore Mod Manager profile.
2. Downloads Valve's official SteamCMD tool, then uses it to download the free music mods below from Steam Workshop. You don't need a Steam login or Terraria.
3. Uses DM DOKURO's Bandcamp album too, if its zip is in your Downloads folder (optional).
4. Copies every boss theme into the right folder as "Artist - Title". It skips intros, outros and remixes, and puts the rest of the mods' music (biome themes etc.) in `CalamityMusic/Unsorted`.
5. Deletes the big downloaded files afterwards. The mod notices the new songs by itself.

You need **Python 3**: https://www.python.org/downloads/ (tick "Add python.exe to PATH"). If you don't have it, the launcher opens that page.

Options, for `get_songs.py`:
- `--dry-run` shows what it would do without changing anything.
- `--no-download` only uses mods you already have.
- `--steam-user NAME` is for when Steam refuses the anonymous download. SteamCMD then asks for your password and Steam Guard code itself.
- `--help` lists the rest.

## Where the songs come from

| Source | What it has | Link |
|---|---|---|
| Calamity Mod Music (Workshop, free) | every Calamity boss theme (DM DOKURO, PinpinNeon, CDMusic) | https://steamcommunity.com/sharedfiles/filedetails/?id=2824688266 |
| Infernum Mode Music (Workshop, free) | the Infernum versions (PinpinNeon) | https://steamcommunity.com/sharedfiles/filedetails/?id=3458992153 |
| Wrath of the Gods (Workshop, free) | Avatar of Emptiness, Nameless Deity, Mars themes | https://steamcommunity.com/sharedfiles/filedetails/?id=2995193002 |
| *The Tale of a Cruel World* (Bandcamp, $7+, optional) | DM DOKURO's full-length album versions; supports him directly | https://dmdokuro.bandcamp.com/album/the-tale-of-a-cruel-world |

Official channels to listen first:
- DM DOKURO: https://www.youtube.com/@DMDOKURO
- PinpinNeon: https://www.youtube.com/channel/UCfbpIwaXAPuE3kTFq1tEqDA
- CDMusic: https://www.youtube.com/channel/UCOItBqiAAbWEdm21Mcv3g_Q
- Heart Plus Up!: https://www.youtube.com/@HeartPlusUp
- The Infernum OST playlist: https://www.youtube.com/playlist?list=PLi_Z42PVYivQd_jpP__Y0zNRK-vs1IT0C

## Multi-part songs

Some themes come in parts in the music mods: Supreme Calamitas (three sections), Devourer of Gods (phase 1 then phase 2), Yharon, Avatar of Emptiness, Nameless Deity, the Infernum Exo Mechs and Boss Rush tiers. The installer tags these `[seq=name N]`. The mod always starts the fight at part 1 and moves on to the next part without a gap when one ends, just like in Terraria. Only the last part loops. For Mithrix, Voidling and False Son the parts follow the fight's phases instead, and each part loops until the next phase starts. In the Simulacrum the Boss Rush tiers play in order, a tier every 10 waves. To tag your own files, add `[seq=anyname 1]`, `[seq=anyname 2]` and so on to the file names.

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
| Calamity/SupremeCalamitas | Supreme Witch, Calamitas | Stained, Brutal Calamity (3 parts from the Workshop, whole from Bandcamp) | DM DOKURO | yes | yes |
| Calamity/PrimordialWyrm | Primordial Wyrm | Threats of the Ocean Floor | DM DOKURO | yes | yes |
| Calamity/BossRush | Boss Rush | Boss Rush Tier 1 to 5 | CDMusic | yes | no |
| Calamity/MoonEscape | (moon escape after Mithrix) | UNIVERSAL COLLAPSE | DM DOKURO | yes | yes |
| Infernum/SupremeCalamitas | Supreme Witch, Calamitas | Scars of Calamity | PinpinNeon | Infernum Mode Music | no |
| Infernum/ExoMechs | Exo Mechs | Catastrophic Fabrications, Zenith Fabrications | PinpinNeon | Infernum Mode Music | no |
| Infernum/PrimordialWyrm | Primordial Wyrm | Vocitus Terminus | PinpinNeon | Infernum Mode Music | no |
| Infernum/Generic | any boss without a better match | Storm Before Dawn, Duel for a Lost Kingdom, Misanthropic Encounters | PinpinNeon | Infernum Mode Music | no |
| Extensions/AvatarOfEmptiness | Avatar of Emptiness (used for Voidling) | PARADISE PARASITE | ENNWAY! | Wrath of the Gods | no |
| Extensions/NamelessDeity | Nameless Deity (used for False Son) | TWISTED GARDEN, then the final-phase theme (file name "ARIA BEYOND THE SHINING FIRMAMENT"; the official title is hidden in game) | Heart Plus Up! & ENNWAY! | Wrath of the Gods | no |
| Extensions/Mars | XG-07 Mars (used for Xi Construct) | RAMifications | Moonburn | Wrath of the Gods | no |
| Extensions/Astrageldon | Astrageldon (used for Twisted Scavengers) | Cosmic Disgust | DM DOKURO | no | yes |


### Area themes (stage music)

| Folder | Area | Song | Composer | Workshop | Bandcamp |
|---|---|---|---|---|---|
| Calamity/Biome_Title | main menu | The Tale of a Cruel World | DM DOKURO | yes | yes |
| Infernum/Biome_Title | main menu (Infernum mode) | Sky After Rain | PinpinNeon | Infernum Mode Music | no |
| Calamity/Biome_SunkenSea | Sunken Sea | sanctuary | DM DOKURO | yes | yes |
| Calamity/Biome_SulphurousSea | Sulphurous Sea (day) | wasteland | DM DOKURO | yes | yes |
| Calamity/Biome_SulphurousNight | Sulphurous Sea (night) | caustic tides | Heart Plus Up! | yes | no |
| Calamity/Biome_SulphurousRain | Sulphurous Sea (rain) | septication | Heart Plus Up! | yes | no |
| Calamity/Biome_AcidRain | Acid Rain | corrosion!, nuclear monsoon | RoverdriveX, PinpinNeon | yes | no |
| Calamity/Biome_Abyss1 | Abyss layer 1 | hesitation | Heart Plus Up! | yes | no |
| Calamity/Biome_Abyss2 | Abyss layer 2 | Hadopelagic Pressure | DM DOKURO | yes | yes |
| Calamity/Biome_Abyss3 | Abyss layer 3 | hydrothermophobia | CDMusic | yes | no |
| Calamity/Biome_Abyss4 | Abyss layer 4 | void | DM DOKURO | yes | yes |
| Calamity/Biome_Astral | Astral Infection | The Heaven-Sent Abomination | DM DOKURO | yes | yes |
| Calamity/Biome_AstralUnderground | Astral Infection (underground) | Treasures Within the Abomination | DM DOKURO | yes | yes |
| Calamity/Biome_BrimstoneCrags | Brimstone Crags | The Step Below Hell | DM DOKURO | yes | yes |
| Calamity/Biome_Lab | Draedon's labs | Engineer's Sanctum | Heart Plus Up! | yes | no |
| Extensions/Biome_EternalGarden | Eternal Garden | Edenic Whispers | ENNWAY! | Wrath of the Gods | no |
| Extensions/Biome_Rift | Rift Eclipse | nowhere now here, Mirrortwist | Moonburn, Lucille Karma | Wrath of the Gods | no |
| Extensions/Biome_Blizzard | Rift Eclipse blizzard | -273.15, Svalbard | Moonburn | Wrath of the Gods | no |
| Extensions/Biome_Stars | sky themes | Stellar Requiem, ascent, The Telescope Song | Moonburn, ENNWAY! & Moonburn | Wrath of the Gods | no |
| Extensions/Biome_Credits | ending credits | Wrath of the Gods | Moonburn | Wrath of the Gods | no |

Which Risk of Rain 2 stage uses which area theme is in the README and in `CalamityMusic.StageThemes.cfg`.

Notes:
- The Bandcamp album has the three Sentinel themes as one "Servants of the Scourge" track. The script puts it in all three folders.
- Remixes on the album (the 8-bit, 16-bit and Structured mixes, Siren's Call, the Eulogy) are skipped, because they aren't fight themes.
- If a folder stays empty, that boss uses the Generic folder or any other song, so you never get silence.
- The installer finds songs inside the Workshop mods by the file names their source code uses. If a future update renames a file and a song lands in `Unsorted`, move it into the right folder by hand. The table above says where.
