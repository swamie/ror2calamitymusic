# Changelog

## 1.1.0

- Boss title cards. The boss's name slams in at the top of the screen with its title, lens flares and themed particles when its theme drops.
- Music follows boss phases. Mithrix's phases move through Supreme Calamitas' three parts, and Voidling and False Son work the same way. Each part loops until the fight moves on.
- Last stand: below 25% health on the last boss the song jumps to its final part, or gets an impact hit and extra bass.
- Auto mode: Calamity, switching to Infernum on Monsoon and Eclipse. It is the default for new installs; an existing config keeps the mode you picked.
- Simulacrum Boss Rush. Tier 1 to 5, a tier every 10 waves, with a title card for each.
- Tape stop when you die, spinning back up on revive or while spectating.
- Low health muffle and heartbeat under 25% health.
- Hotkeys: skip (F8), favourite (F9) and ban (F10), plus a Clear Banned Songs button. Favourites are picked three times as often.
- Multiplayer sync: when the host has the mod, modded clients play the host's song picks.

## 1.0.0

- First release.
- Teleporter buildup (synthesised riser or your own files) that drops into the boss theme when the boss spawns.
- Per-boss Calamity themes for every Risk of Rain 2 boss through Seekers of the Storm and newer DLC, plus Starstorm 2.
- Calamity, Infernum and Mixed modes, or Vanilla to switch it all off.
- Live bass boost with a look-ahead limiter, seamless looping, crossfades, pause muffle.
- Now Playing banner styled after the Calamity/Infernum music display. It sits bottom left by default, and each boss or area has its own colours and particles (embers, holy fire, void, lightning, glitches, bubbles, toxic drips, snow, petals...). Letters pop in, a light sweeps across, they bump to the bass, and the boss/area name shows underneath.
- Settings in Mod Options: volume, bass, buildup, banner, preview button.
- One-click song installer (Get Songs button, or tools/get_songs.py): fetches the free Calamity music mods from Steam Workshop and files every boss theme.
- Stage music: every stage, menu, Bazaar, Void stage, hidden realm and DLC stage plays a Calamity area theme (Sunken Sea, Sulphurous Sea, Abyss, Astral Infection, Brimstone Crags, labs, Wrath of the Gods areas). Boss themes crossfade back into it. Mapping in CalamityMusic.StageThemes.cfg.
- Multi-part songs (Supreme Calamitas, Devourer of Gods, Yharon, Avatar of Emptiness...) play their parts back to back without a gap.
