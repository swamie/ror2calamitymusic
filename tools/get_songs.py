#!/usr/bin/env python3
"""
Calamity Music song installer.

Finds the Calamity songs you already have and files them into the mod's music folders with the
right names ("Artist - Title"), so you don't have to sort anything by hand.

Where the songs come from (pick any, or all):

  1. Steam Workshop (free). Subscribe in tModLoader to the music mods:
       Calamity Mod Music    https://steamcommunity.com/sharedfiles/filedetails/?id=2824688266
       Infernum Mode Music   https://steamcommunity.com/sharedfiles/filedetails/?id=3458992153
       Wrath of the Gods     https://steamcommunity.com/sharedfiles/filedetails/?id=2995193002
     Let Steam download them, then run this script. It reads the songs straight out of the
     downloaded .tmod files. This covers every in-game boss theme, Infernum versions included.

  2. DM DOKURO's album "The Tale of a Cruel World" on Bandcamp ($7 or more):
       https://dmdokuro.bandcamp.com/album/the-tale-of-a-cruel-world
     Download it as MP3 or Ogg Vorbis and point the script at the zip (no need to unzip).

  3. Any folder of song files you got some other way (file names should contain the title).

Usage:
  python get_songs.py                      interactive: finds everything and asks before copying
  python get_songs.py --workshop           only the Steam Workshop music mods
  python get_songs.py --bandcamp ALBUM.zip only the Bandcamp album (zip or unzipped folder)
  python get_songs.py --folder SOME_DIR    match loose files by title
  python get_songs.py --music PATH         where to put them (the mod's Music folder);
                                           found automatically for r2modman / Thunderstore profiles
  python get_songs.py --dry-run            show what would happen, change nothing
  python get_songs.py --list-tmod FILE     print every file inside a .tmod (for troubleshooting)

Needs Python 3.8+ and nothing else. FLAC/M4A/Opus files get converted to .ogg if ffmpeg is on PATH.
For personal use: this only copies files you already have on your own PC.
"""

import argparse
import difflib
import glob
import io
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile
import zlib

# ----------------------------------------------------------------------------------------------
# Song catalogue
#   pack:   Calamity / Infernum / Extensions (the top-level folder in Music/)
#   slots:  boss theme folders the song goes into (one song can feed several bosses)
#   titles: how the song is named on the album / in file names (matched after normalising)
#   keys:   words found in the music mods' internal file names (Sounds/Music/DesertScourge.ogg...)
# ----------------------------------------------------------------------------------------------

DOKURO = "DM DOKURO"
DOKURO_16 = "DM DOKURO & SixteenInMono"

CATALOG = [
    # pack, slots, artist, title, album titles, tmod keys
    ("Calamity", ["DesertScourge"], DOKURO, "Guardian of the Former Seas", ["guardian of the former seas"], ["desertscourge"]),
    ("Calamity", ["Crabulon"], DOKURO, "Infestation", ["infestation", "1nf3 0n", "1nf3"], ["crabulon"]),
    ("Calamity", ["HiveMind"], DOKURO_16, "The Filthy Mind", ["the filthy mind"], ["hivemind"]),
    ("Calamity", ["Perforators"], DOKURO_16, "Blood Coagulant", ["blood coagulant"], ["perforator"]),
    ("Calamity", ["SlimeGod"], DOKURO_16, "Return to Slime", ["return to slime"], ["slimegod"]),
    ("Calamity", ["Cryogen"], DOKURO, "Antarctic Reinforcement", ["antarctic reinforcement"], ["cryogen"]),
    ("Calamity", ["AquaticScourge"], DOKURO, "Outcast of the Sulphurous Seas", ["outcast of the sulphurous seas"], ["aquaticscourge"]),
    ("Calamity", ["BrimstoneElemental"], DOKURO, "Left Alone", ["left alone"], ["brimstone"]),
    ("Calamity", ["CalamitasClone"], DOKURO, "Raw, Unfiltered Calamity", ["raw unfiltered calamity"], ["calamitasclone", "clone"]),
    ("Calamity", ["LeviathanAnahita"], DOKURO, "The Leviathan Trilogy", ["the leviathan trilogy"], []),
    ("Calamity", ["LeviathanAnahita"], DOKURO, "Siren's War Cry", ["sirens war cry"], ["anahita"]),
    ("Calamity", ["LeviathanAnahita"], DOKURO, "Forbidden Lullaby", ["forbidden lullaby"], ["leviathan"]),
    ("Calamity", ["AstrumAureus"], DOKURO, "Interstellar Stomper", ["interstellar stomper"], ["astrumaureus"]),
    ("Calamity", ["PlaguebringerGoliath"], DOKURO, "Fly of Beelzebub", ["fly of beelzebub"], ["plaguebringer"]),
    ("Calamity", ["Ravager"], DOKURO, "Open Frenzy", ["open frenzy"], ["ravager"]),
    ("Calamity", ["AstrumDeus"], DOKURO, "Pest of the Cosmos", ["pest of the cosmos"], ["astrumdeus"]),
    ("Calamity", ["ProfanedGuardians"], DOKURO, "Unholy Ambush", ["unholy ambush"], ["profanedguardian", "guardians"]),
    ("Calamity", ["Dragonfolly"], DOKURO, "Murderswarm", ["murderswarm"], ["dragonfolly", "bumblebirb", "bumblefuck"]),
    ("Calamity", ["Providence"], DOKURO, "Unholy Insurgency", ["unholy insurgency"], ["providence"]),
    # The album has the three Sentinel themes as one track; the music mod has them separately.
    ("Calamity", ["StormWeaver", "CeaselessVoid", "Signus"], DOKURO, "Servants of the Scourge", ["servants of the scourge"], []),
    ("Calamity", ["StormWeaver"], DOKURO, "Servants of the Scourge (Type W)", ["servants of the scourge type w"], ["stormweaver", "weaver"]),
    ("Calamity", ["CeaselessVoid"], DOKURO, "Servants of the Scourge (Type V)", ["servants of the scourge type v"], ["ceaselessvoid", "ceaseless"]),
    ("Calamity", ["Signus"], DOKURO, "Servants of the Scourge (Type S)", ["servants of the scourge type s"], ["signus"]),
    ("Calamity", ["Polterghast"], DOKURO, "Feral Amalgamation", ["feral amalgamation"], ["polterghast"]),
    ("Calamity", ["OldDuke"], DOKURO, "Toxic Wisdom", ["toxic wisdom"], ["oldduke"]),
    ("Calamity", ["DevourerOfGods"], DOKURO, "Scourge of the Universe", ["scourge of the universe"], ["devourerofgodsp1", "devourerofgods1", "dogp1", "devourerofgods"]),
    ("Calamity", ["DevourerOfGods", "MoonEscape"], DOKURO, "UNIVERSAL COLLAPSE", ["universal collapse"], ["devourerofgodsp2", "devourerofgods2", "dogp2"]),
    ("Calamity", ["Yharon"], DOKURO, "Infernal Catharsis (Rebirth)", ["infernal catharsis rebirth", "infernal catharsis"], ["yharonp1", "yharon1", "yharon"]),
    ("Calamity", ["Yharon"], DOKURO, "Roar of the Jungle Dragon", ["roar of the jungle dragon"], ["yharonp2", "yharon2"]),
    ("Calamity", ["ExoMechs"], "PinpinNeon", "Inferior Fabrications", ["inferior fabrications"], ["exomech", "exomechs"]),
    ("Calamity", ["SupremeCalamitas"], DOKURO, "Stained, Brutal Calamity", ["stained brutal calamity"], ["supremecalamitas", "scal", "calamitas"]),
    ("Calamity", ["PrimordialWyrm"], DOKURO, "Threats of the Ocean Floor", ["threats of the ocean floor"], ["primordialwyrm", "adulteidolonwyrm", "eidolonwyrm"]),
    ("Calamity", ["BossRush"], "CDMusic", "Boss Rush Tier 1", ["boss rush tier 1"], ["bossrush1", "bossrushtier1"]),
    ("Calamity", ["BossRush"], "CDMusic", "Boss Rush Tier 2", ["boss rush tier 2"], ["bossrush2", "bossrushtier2"]),
    ("Calamity", ["BossRush"], "CDMusic", "Boss Rush Tier 3", ["boss rush tier 3"], ["bossrush3", "bossrushtier3"]),
    ("Calamity", ["BossRush"], "CDMusic", "Boss Rush Tier 4", ["boss rush tier 4"], ["bossrush4", "bossrushtier4", "bossrush5", "bossrushtier5"]),

    # Infernum Mode Music (all by PinpinNeon)
    ("Infernum", ["SupremeCalamitas"], "PinpinNeon", "Scars of Calamity", ["scars of calamity"], ["supremecalamitas", "scal", "calamitas"]),
    ("Infernum", ["ExoMechs"], "PinpinNeon", "Catastrophic Fabrications", ["catastrophic fabrications"], ["catastrophic", "exomech"]),
    ("Infernum", ["ExoMechs"], "PinpinNeon", "Zenith Fabrications", ["zenith fabrications"], ["zenith"]),
    ("Infernum", ["PrimordialWyrm"], "PinpinNeon", "Vocitus Terminus", ["vocitus terminus"], ["vocitus", "primordialwyrm", "adulteidolonwyrm", "eidolonwyrm"]),

    # Calamity add-on mods
    ("Extensions", ["AvatarOfEmptiness"], "ENNWAY!", "PARADISE PARASITE", ["paradise parasite"], ["avatarofemptiness", "avatarrift", "avatar"]),
    ("Extensions", ["NamelessDeity"], "Heart Plus Up! & ENNWAY!", "TWISTED GARDEN", ["twisted garden"], ["namelessdeity"]),
    # Edenic Whispers is the calm Eternal Garden theme, so it goes in the general pool rather than a boss fight.
    ("Extensions", ["Generic"], "ENNWAY!", "Edenic Whispers", ["edenic whispers"], ["eternalgarden", "edenic"]),
    ("Extensions", ["Mars"], "Moonburn", "RAMifications", ["ramifications"], ["mars"]),
    ("Extensions", ["Goozma"], "The Exiled Fellow", "Glutinous Arbitration", ["glutinous arbitration", "glutinous arbiration"], ["glutinous", "goozma"]),
    ("Extensions", ["Goozma"], "The Exiled Fellow", "Viscous Desperation", ["viscous desperation"], ["viscous"]),
    ("Extensions", ["Astrageldon"], DOKURO, "Cosmic Disgust", ["cosmic disgust"], ["astrageldon"]),
    ("Extensions", ["Astrageldon"], "Heart Plus Up!", "Galactic Destruction", ["galactic destruction"], []),
]

SUPPORTED = {".ogg", ".mp3", ".wav"}
CONVERTIBLE = {".flac", ".m4a", ".aac", ".opus", ".webm", ".alac", ".aif", ".aiff", ".wma"}
AUDIO = SUPPORTED | CONVERTIBLE

# Music mods by internal name -> which pack their songs belong to
MOD_PACKS = [
    ("infernum", "Infernum"),
    ("calamitymodmusic", "Calamity"),
    ("calamitymusic", "Calamity"),
    ("noxusboss", "Extensions"),
    ("wrathofthegods", "Extensions"),
    ("calamityhunt", "Extensions"),
    ("catalyst", "Extensions"),
]

TMODLOADER_APP_ID = "1281930"


def norm(text):
    """Lowercase, drop punctuation and extra spaces: "Raw, Unfiltered Calamity" -> "raw unfiltered calamity"."""
    text = text.lower().replace("&", " and ").replace("'", "")
    text = re.sub(r"[^a-z0-9]+", " ", text)
    return " ".join(text.split())


def squash(text):
    return re.sub(r"[^a-z0-9]", "", text.lower())


def safe_name(text):
    return re.sub(r'[<>:"/\\|?*]', "", text).strip()


# ----------------------------------------------------------------------------------------------
# .tmod reader (format from tModLoader's TmodFile.cs)
# ----------------------------------------------------------------------------------------------

def _read_dotnet_string(f):
    length, shift = 0, 0
    while True:
        b = f.read(1)
        if not b:
            raise EOFError("unexpected end of file")
        b = b[0]
        length |= (b & 0x7F) << shift
        if b < 0x80:
            break
        shift += 7
    return f.read(length).decode("utf-8", "replace")


def read_tmod(path):
    """Returns (mod name, [(file name, reader function)])."""
    with open(path, "rb") as f:
        if f.read(4) != b"TMOD":
            raise ValueError("not a .tmod file")
        tml_version = _read_dotnet_string(f)
        f.read(20 + 256)  # hash + signature
        f.read(4)          # data length
        major = int(re.match(r"\d+", tml_version).group(0)) if re.match(r"\d+", tml_version) else 2022
        if major == 0:
            raise ValueError("mod built for a very old tModLoader (" + tml_version + ")")
        name = _read_dotnet_string(f)
        _read_dotnet_string(f)  # mod version
        count = struct.unpack("<i", f.read(4))[0]
        table = []
        for _ in range(count):
            fname = _read_dotnet_string(f)
            length, compressed = struct.unpack("<ii", f.read(8))
            table.append((fname, length, compressed))
        start = f.tell()

    entries = []
    offset = start
    for fname, length, compressed in table:
        def reader(off=offset, length=length, compressed=compressed):
            with open(path, "rb") as f:
                f.seek(off)
                data = f.read(compressed)
            return zlib.decompress(data, -15) if compressed != length else data
        entries.append((fname, reader))
        offset += compressed
    return name, entries


# ----------------------------------------------------------------------------------------------
# Finding things on disk
# ----------------------------------------------------------------------------------------------

def steam_libraries():
    roots = []
    if sys.platform == "win32":
        try:
            import winreg
            for hive, key in ((winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"),
                              (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam")):
                try:
                    with winreg.OpenKey(hive, key) as k:
                        for value in ("SteamPath", "InstallPath"):
                            try:
                                roots.append(winreg.QueryValueEx(k, value)[0])
                            except OSError:
                                pass
                except OSError:
                    pass
        except ImportError:
            pass
        roots += [r"C:\Program Files (x86)\Steam", r"C:\Program Files\Steam"]
    else:
        home = os.path.expanduser("~")
        roots += [os.path.join(home, ".steam", "steam"), os.path.join(home, ".local", "share", "Steam"),
                  os.path.join(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                  os.path.join(home, "Library", "Application Support", "Steam")]

    libs = []
    for root in roots:
        if not os.path.isdir(root):
            continue
        libs.append(root)
        vdf = os.path.join(root, "steamapps", "libraryfolders.vdf")
        if os.path.isfile(vdf):
            with open(vdf, encoding="utf-8", errors="replace") as f:
                for m in re.finditer(r'"path"\s+"([^"]+)"', f.read()):
                    libs.append(m.group(1).replace("\\\\", "\\"))
    seen, out = set(), []
    for lib in libs:
        key = os.path.normcase(os.path.abspath(lib))
        if key not in seen and os.path.isdir(lib):
            seen.add(key)
            out.append(lib)
    return out


def find_tmods():
    files = []
    for lib in steam_libraries():
        files += glob.glob(os.path.join(lib, "steamapps", "workshop", "content", TMODLOADER_APP_ID, "*", "**", "*.tmod"), recursive=True)
    docs = [os.path.join(os.path.expanduser("~"), "Documents"), os.path.join(os.path.expanduser("~"), "OneDrive", "Documents")]
    for d in docs:
        files += glob.glob(os.path.join(d, "My Games", "Terraria", "tModLoader", "Mods", "*.tmod"))
    files += glob.glob(os.path.join(os.path.expanduser("~"), ".local", "share", "Terraria", "tModLoader", "Mods", "*.tmod"))

    # Keep the newest copy of each music mod. Workshop keeps one per tModLoader version, in
    # folders like "2023.6" and "2024.8"; the highest one matches the current tModLoader.
    best = {}
    for path in files:
        try:
            name, _ = read_tmod(path)
        except Exception:
            continue
        if pack_for_mod(name) is None:
            continue
        rank = (version_key(os.path.basename(os.path.dirname(path))), os.path.getmtime(path))
        if name not in best or rank > best[name][0]:
            best[name] = (rank, path)
    return sorted((name, path) for name, (_rank, path) in best.items())


def version_key(folder):
    parts = re.findall(r"\d+", folder)
    return tuple(int(p) for p in parts) if parts else (0,)


def pack_for_mod(mod_name):
    s = squash(mod_name)
    for key, pack in MOD_PACKS:
        if key in s:
            return pack
    return None


def find_music_folders():
    """Music folders of every r2modman / Thunderstore Mod Manager profile, plus a plain game install."""
    tail = os.path.join("BepInEx", "config", "CalamityMusic", "Music")
    bases = []
    if sys.platform == "win32":
        appdata = os.environ.get("APPDATA", "")
        bases += glob.glob(os.path.join(appdata, "r2modmanPlus-local", "RiskOfRain2", "profiles", "*"))
        bases += glob.glob(os.path.join(appdata, "Thunderstore Mod Manager", "DataFolder", "RiskOfRain2", "profiles", "*"))
    else:
        home = os.path.expanduser("~")
        bases += glob.glob(os.path.join(home, ".config", "r2modmanPlus-local", "RiskOfRain2", "profiles", "*"))
        bases += glob.glob(os.path.join(home, ".config", "Thunderstore Mod Manager", "DataFolder", "RiskOfRain2", "profiles", "*"))
    for lib in steam_libraries():
        bases.append(os.path.join(lib, "steamapps", "common", "Risk of Rain 2"))

    found = []
    for base in bases:
        plugin_dirs = glob.glob(os.path.join(base, "BepInEx", "plugins", "*CalamityMusic*"))
        music = os.path.join(base, tail)
        if os.path.isdir(music) or plugin_dirs:
            found.append(music)
    return found


# ----------------------------------------------------------------------------------------------
# Matching
# ----------------------------------------------------------------------------------------------

# Internal tracks that mention a boss but aren't its fight theme (intros, first-defeat eulogies...).
NOT_FIGHT = ("eulogy", "intro", "outro", "lore", "menu", "title", "victory", "defeat", "acceptance", "musicbox", "ambien")


def match_internal_name(path_in_mod, pack):
    """Match a music mod's internal file (e.g. Sounds/Music/DesertScourge.ogg) by its name."""
    stem = squash(os.path.splitext(os.path.basename(path_in_mod))[0])
    if any(word in stem for word in NOT_FIGHT):
        return None
    best, best_len = None, 0
    for entry in CATALOG:
        if entry[0] != pack:
            continue
        for key in entry[5]:
            # The longest key wins, so "devourerofgodsp2" beats "devourerofgods".
            if key and key in stem and len(key) > best_len:
                best, best_len = entry, len(key)
    return best


def title_from_filename(filename):
    """'DM DOKURO - The Tale of a Cruel World - 02 Guardian of the Former Seas.mp3' -> 'Guardian of the Former Seas'."""
    stem = os.path.splitext(os.path.basename(filename))[0]
    stem = re.sub(r"\[[^\]]*\]", "", stem)
    # Leftovers from video titles: (Official Audio), (HQ), (Lyrics)...
    stem = re.sub(r"\s*\((official[^)]*|hq|hd|lyrics?|audio|full song|4k|1080p)\)", "", stem, flags=re.IGNORECASE)
    parts = [p.strip() for p in stem.split(" - ")]
    last = parts[-1] if parts else stem
    last = re.sub(r"^\d{1,3}[\s._-]+", "", last)  # leading track number
    return last


def match_title(filename):
    title = norm(title_from_filename(filename))
    if not title:
        return None
    exact = [e for e in CATALOG if any(title == t for t in e[4])]
    if exact:
        return exact[0]
    # Remixes on the album ("8-bit Mix", "Eulogy for the Ego"...) must not replace the real theme.
    if re.search(r"\b(mix|remix|eulogy|cover|extended|loop|8 bit|16 bit)\b", title):
        return None
    candidates = {t: e for e in CATALOG for t in e[4]}
    close = difflib.get_close_matches(title, list(candidates), n=1, cutoff=0.88)
    return candidates[close[0]] if close else None


# ----------------------------------------------------------------------------------------------
# Writing
# ----------------------------------------------------------------------------------------------

class Installer:
    def __init__(self, music_dir, dry_run):
        self.music = music_dir
        self.dry = dry_run
        self.placed = {}  # (pack, slot, title) -> path
        self.sources = {}  # (pack, slot, title) -> where it came from
        self.skipped = []
        self.ffmpeg = shutil.which("ffmpeg")

    def place(self, entry, ext, get_bytes, source_label, variant=None):
        pack, slots, artist, title = entry[0], entry[1], entry[2], entry[3]
        # Two different files for the same song (e.g. separate phase files): keep both, labelled.
        same = [k for k in self.placed if k[2].startswith(title) and k[0] == pack and self.sources.get(k) != source_label]
        if variant and same:
            phase = re.search(r"p(?:hase)?\s*(\d+)$", variant, re.IGNORECASE)
            title = f"{title} (Phase {phase.group(1)})" if phase else f"{title} (Part {len({k[2] for k in same}) + 1})"
        ext = ext.lower()
        out_ext = ext if ext in SUPPORTED else ".ogg"
        if ext not in SUPPORTED and not self.ffmpeg:
            self.skipped.append(f"{source_label}: {ext} needs converting but ffmpeg isn't installed (download MP3/Ogg instead)")
            return
        data = None
        for slot in slots:
            dest_dir = os.path.join(self.music, pack, slot)
            dest = os.path.join(dest_dir, safe_name(f"{artist} - {title}") + out_ext)
            key = (pack, slot, title)
            if key in self.placed:
                continue
            self.placed[key] = dest
            self.sources[key] = source_label
            if os.path.exists(dest):
                print(f"  = {pack}/{slot}/{os.path.basename(dest)} (already there)")
                continue
            print(f"  + {pack}/{slot}/{os.path.basename(dest)}   <- {source_label}")
            if self.dry:
                continue
            os.makedirs(dest_dir, exist_ok=True)
            if data is None:
                data = get_bytes()
            if ext in SUPPORTED:
                with open(dest, "wb") as f:
                    f.write(data)
            else:
                self.convert(data, ext, dest)

    def convert(self, data, ext, dest):
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "in" + ext)
            with open(src, "wb") as f:
                f.write(data)
            subprocess.run([self.ffmpeg, "-y", "-loglevel", "error", "-i", src, "-vn", "-c:a", "libvorbis", "-q:a", "6", dest], check=True)

    def summary(self):
        have = {(p, s) for (p, s, _t) in self.placed}
        missing = []
        for entry in CATALOG:
            pack, slots = entry[0], entry[1]
            if pack != "Calamity":
                continue
            for slot in slots:
                if (pack, slot) not in have and slot not in missing and slot != "MoonEscape":
                    missing.append(slot)
        return missing


def install_from_tmods(inst, tmods, unsorted_dir):
    for mod_name, path in tmods:
        pack = pack_for_mod(mod_name)
        print(f"\n{mod_name} ({pack})  {path}")
        _, entries = read_tmod(path)
        for fname, reader in entries:
            ext = os.path.splitext(fname)[1].lower()
            if ext not in AUDIO:
                continue
            entry = match_internal_name(fname, pack)
            if entry:
                variant = os.path.splitext(os.path.basename(fname))[0]
                inst.place(entry, ext, reader, f"{mod_name}:{fname}", variant)
            elif unsorted_dir is not None:
                # Biome themes etc. Kept to one side so you can use them if you want.
                dest = os.path.join(unsorted_dir, pack, safe_name(os.path.basename(fname)))
                if not os.path.exists(dest) and not inst.dry:
                    os.makedirs(os.path.dirname(dest), exist_ok=True)
                    with open(dest, "wb") as f:
                        f.write(reader())


def iter_audio(source):
    """Yields (display name, extension, bytes getter) for a zip file or folder."""
    if os.path.isfile(source) and zipfile.is_zipfile(source):
        z = zipfile.ZipFile(source)
        for info in z.infolist():
            ext = os.path.splitext(info.filename)[1].lower()
            if ext in AUDIO and not info.is_dir():
                yield info.filename, ext, (lambda n=info.filename: z.read(n))
    elif os.path.isdir(source):
        for root, _dirs, files in os.walk(source):
            for name in sorted(files):
                ext = os.path.splitext(name)[1].lower()
                full = os.path.join(root, name)
                if ext in AUDIO:
                    yield full, ext, (lambda p=full: open(p, "rb").read())
                elif ext == ".zip":
                    yield from iter_audio(full)
    else:
        print(f"  ! {source} is not a zip file or folder")


def install_from_files(inst, source):
    print(f"\n{source}")
    unmatched = []
    for name, ext, getter in iter_audio(source):
        entry = match_title(name)
        if entry:
            inst.place(entry, ext, getter, os.path.basename(name))
        else:
            unmatched.append(os.path.basename(name))
    if unmatched:
        print(f"  ({len(unmatched)} file(s) aren't boss themes and were left alone, e.g. {unmatched[0]})")


# ----------------------------------------------------------------------------------------------

def ask(prompt, default=""):
    try:
        answer = input(prompt).strip().strip('"')
    except EOFError:
        return default
    return answer or default


def choose_music_folder(arg):
    if arg:
        return os.path.abspath(arg)
    found = find_music_folders()
    if len(found) == 1:
        return found[0]
    if found:
        print("Found CalamityMusic in these profiles:")
        for i, f in enumerate(found, 1):
            print(f"  {i}. {f}")
        pick = ask("Which one? [1] ", "1")
        if pick.isdigit() and 1 <= int(pick) <= len(found):
            return found[int(pick) - 1]
    print("Couldn't find the mod's Music folder automatically.")
    print("Start the game once with the mod installed, or paste the path to BepInEx/config/CalamityMusic/Music.")
    path = ask("Music folder: ")
    return os.path.abspath(path) if path else None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--music", help="the mod's Music folder (BepInEx/config/CalamityMusic/Music)")
    ap.add_argument("--workshop", action="store_true", help="use the Steam Workshop music mods")
    ap.add_argument("--bandcamp", help="Bandcamp album zip or folder")
    ap.add_argument("--folder", action="append", default=[], help="any folder or zip of song files")
    ap.add_argument("--dry-run", action="store_true", help="show what would be copied")
    ap.add_argument("--no-unsorted", action="store_true", help="don't keep the non-boss tracks from the music mods")
    ap.add_argument("--list-tmod", help="list the files inside a .tmod and exit")
    args = ap.parse_args()

    if args.list_tmod:
        name, entries = read_tmod(args.list_tmod)
        print(name)
        for fname, _ in entries:
            print("  " + fname)
        return 0

    interactive = not (args.workshop or args.bandcamp or args.folder)
    music = choose_music_folder(args.music)
    if not music:
        return 1
    print(f"Music folder: {music}")
    inst = Installer(music, args.dry_run)
    unsorted = None if args.no_unsorted else os.path.join(os.path.dirname(music), "Unsorted")

    if args.workshop or interactive:
        tmods = find_tmods()
        if tmods:
            print(f"\nFound {len(tmods)} Calamity music mod(s) from Steam Workshop / tModLoader.")
            install_from_tmods(inst, tmods, unsorted)
        elif args.workshop or interactive:
            print("\nNo Calamity music mods found. Subscribe to them in Steam Workshop (links at the top of this script),")
            print("open tModLoader once so Steam downloads them, then run this again.")

    sources = list(args.folder)
    if args.bandcamp:
        sources.insert(0, args.bandcamp)
    if interactive:
        extra = ask("\nPath to the Bandcamp album zip or any folder of songs (Enter to skip): ")
        if extra:
            sources.append(extra)
    for source in sources:
        install_from_files(inst, source)

    print()
    if args.dry_run:
        print("Dry run: nothing was copied.")
    for s in inst.skipped:
        print("  ! " + s)
    missing = inst.summary()
    print(f"Done. {len(inst.placed)} song slot(s) filled.")
    if missing:
        print("Boss folders still empty (these bosses fall back to other songs): " + ", ".join(missing))
    if unsorted and os.path.isdir(unsorted):
        print(f"Non-boss tracks from the music mods were kept in {unsorted} if you want them.")
    print("In game: Settings > Mod Options > Calamity Music > Rescan Music Folder (or just start a new stage).")
    if interactive and sys.platform == "win32":
        ask("\nPress Enter to close.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
