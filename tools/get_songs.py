#!/usr/bin/env python3
"""
Calamity Music song installer. Run it and it does everything:

  1. Finds the CalamityMusic mod in your r2modman / Thunderstore Mod Manager profile.
  2. Gets the free Calamity music mods from Steam Workshop, using Valve's official SteamCMD
     tool, which it downloads for you. No Steam login or Terraria needed. Mods you're already
     subscribed to in tModLoader are used directly.
       Calamity Mod Music    https://steamcommunity.com/sharedfiles/filedetails/?id=2824688266
       Infernum Mode Music   https://steamcommunity.com/sharedfiles/filedetails/?id=3458992153
       Wrath of the Gods     https://steamcommunity.com/sharedfiles/filedetails/?id=2995193002
  3. Also uses DM DOKURO's Bandcamp album if it's in your Downloads folder (optional, $7+):
       https://dmdokuro.bandcamp.com/album/the-tale-of-a-cruel-world
  4. Copies every boss theme into the right folder, named "Artist - Title".

Usage:
  python get_songs.py                    do everything
  python get_songs.py --dry-run          show what would happen, change nothing
  python get_songs.py --music PATH       use this Music folder (BepInEx/config/CalamityMusic/Music)
  python get_songs.py --no-download      only use music mods already on this PC
  python get_songs.py --steam-user NAME  log SteamCMD in with your account if anonymous download is refused
  python get_songs.py --bandcamp ZIP     use this Bandcamp album zip/folder
  python get_songs.py --folder DIR       also match loose song files in DIR by title
  python get_songs.py --keep-downloads   keep the downloaded Workshop files afterwards
  python get_songs.py --list-tmod FILE   print the files inside a .tmod (troubleshooting)

Needs Python 3.8+ only. FLAC/M4A/Opus files are converted to .ogg if ffmpeg is installed.
For personal use: the songs stay on your PC and nothing is uploaded anywhere.
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
import tarfile
import tempfile
import urllib.request
import zipfile
import zlib

TMODLOADER_APP_ID = "1281930"

# Workshop item id -> (name, pack its songs go in)
WORKSHOP_MODS = {
    "2824688266": ("Calamity Mod Music", "Calamity"),
    "3458992153": ("Infernum Mode Music", "Infernum"),
    "2995193002": ("Wrath of the Gods", "Extensions"),
}

# The same mods recognised by their internal name (for copies installed some other way).
MOD_NAME_PACKS = [
    ("infernum", "Infernum"),
    ("calamitymodmusic", "Calamity"),
    ("noxusboss", "Extensions"),
]

STEAMCMD_URLS = {
    "win32": "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip",
    "linux": "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz",
    "darwin": "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz",
}

DOKURO = "DM DOKURO"
DOKURO_16 = "DM DOKURO & SixteenInMono"
NAMELESS = "Heart Plus Up! & ENNWAY!"


def song(pack, slots, artist, title, album=(), files=(), seq=None):
    """
    pack/slots: where it goes (Music/<pack>/<slot>/).
    album:      normalised titles it appears under on the Bandcamp album / in loose file names.
    files:      the music mods' internal file names (Sounds/Music/<name>.ogg), as written in the
                Calamity, Infernum Music and Wrath of the Gods source code.
    seq:        (name, part) for songs split into parts that play back to back.
    """
    return {"pack": pack, "slots": list(slots), "artist": artist, "title": title,
            "album": list(album), "files": [f.lower() for f in files], "seq": seq}


CATALOG = [
    # ---- Calamity Mod Music
    song("Calamity", ["DesertScourge"], DOKURO, "Guardian of the Former Seas", ["guardian of the former seas"], ["DesertScourge"]),
    song("Calamity", ["Crabulon"], DOKURO, "Infestation", ["infestation", "1nf3 0n"], ["Crabulon"]),
    song("Calamity", ["HiveMind"], DOKURO_16, "The Filthy Mind", ["the filthy mind"], ["HiveMind"]),
    song("Calamity", ["Perforators"], DOKURO_16, "Blood Coagulant", ["blood coagulant"], ["Perforators"]),
    song("Calamity", ["SlimeGod"], DOKURO_16, "Return to Slime", ["return to slime"], ["SlimeGod"]),
    song("Calamity", ["Cryogen"], DOKURO, "Antarctic Reinforcement", ["antarctic reinforcement"], ["Cryogen"]),
    song("Calamity", ["AquaticScourge"], DOKURO, "Outcast of the Sulphurous Seas", ["outcast of the sulphurous seas"], ["AquaticScourge"]),
    song("Calamity", ["BrimstoneElemental"], DOKURO, "Left Alone", ["left alone"], ["BrimstoneElemental"]),
    song("Calamity", ["CalamitasClone"], DOKURO, "Raw, Unfiltered Calamity", ["raw unfiltered calamity"], ["CalamitasClone"]),
    song("Calamity", ["LeviathanAnahita"], DOKURO, "Siren's War Cry", ["sirens war cry"], ["Anahita"]),
    song("Calamity", ["LeviathanAnahita"], DOKURO, "Forbidden Lullaby", ["forbidden lullaby"], ["Leviathan"]),
    song("Calamity", ["LeviathanAnahita"], DOKURO, "The Leviathan Trilogy", ["the leviathan trilogy"]),
    song("Calamity", ["AstrumAureus"], DOKURO, "Interstellar Stomper", ["interstellar stomper"], ["AstrumAureus"]),
    song("Calamity", ["PlaguebringerGoliath"], DOKURO, "Fly of Beelzebub", ["fly of beelzebub"], ["PlaguebringerGoliath"]),
    song("Calamity", ["Ravager"], DOKURO, "Open Frenzy", ["open frenzy"], ["Ravager"]),
    song("Calamity", ["AstrumDeus"], DOKURO, "Pest of the Cosmos", ["pest of the cosmos"], ["AstrumDeus"]),
    song("Calamity", ["ProfanedGuardians"], DOKURO, "Unholy Ambush", ["unholy ambush"], ["ProfanedGuardians"]),
    song("Calamity", ["Dragonfolly"], DOKURO, "Murderswarm", ["murderswarm"], ["Dragonfolly"]),
    song("Calamity", ["Providence"], DOKURO, "Unholy Insurgency", ["unholy insurgency"], ["Providence"]),
    song("Calamity", ["StormWeaver"], DOKURO, "Servants of the Scourge (Type W)", ["servants of the scourge type w"], ["StormWeaver"]),
    song("Calamity", ["CeaselessVoid"], DOKURO, "Servants of the Scourge (Type V)", ["servants of the scourge type v"], ["CeaselessVoid"]),
    song("Calamity", ["Signus"], DOKURO, "Servants of the Scourge (Type S)", ["servants of the scourge type s"], ["Signus"]),
    # The album has the three Sentinel themes as one track.
    song("Calamity", ["StormWeaver", "CeaselessVoid", "Signus"], DOKURO, "Servants of the Scourge", ["servants of the scourge"]),
    song("Calamity", ["Polterghast"], DOKURO, "Feral Amalgamation", ["feral amalgamation"], ["Polterghast"]),
    song("Calamity", ["OldDuke"], DOKURO, "Toxic Wisdom", ["toxic wisdom"], ["OldDuke"]),
    song("Calamity", ["DevourerOfGods"], DOKURO, "Scourge of the Universe", ["scourge of the universe"], ["DevourerofGodsPhase1"], ("dog", 1)),
    song("Calamity", ["DevourerOfGods", "MoonEscape"], DOKURO, "UNIVERSAL COLLAPSE", ["universal collapse"], ["DevourerofGodsPhase2"], ("dog", 2)),
    song("Calamity", ["Yharon"], DOKURO, "Infernal Catharsis (Rebirth)", ["infernal catharsis rebirth", "infernal catharsis"], ["YharonPhase1"], ("yharon", 1)),
    song("Calamity", ["Yharon"], DOKURO, "Roar of the Jungle Dragon", ["roar of the jungle dragon"], ["YharonPhase2"], ("yharon", 2)),
    song("Calamity", ["ExoMechs"], "PinpinNeon", "Inferior Fabrications", ["inferior fabrications"], ["ExoMechs"]),
    # Supreme Calamitas: the music mod splits the song into its sections; the album has it whole.
    song("Calamity", ["SupremeCalamitas"], DOKURO, "Stained, Brutal Calamity", [], ["CalamitasPhase1"], ("scal", 1)),
    song("Calamity", ["SupremeCalamitas"], DOKURO, "Stained, Brutal Calamity", [], ["CalamitasPhase2"], ("scal", 2)),
    song("Calamity", ["SupremeCalamitas"], DOKURO, "Stained, Brutal Calamity", [], ["CalamitasPhase3"], ("scal", 3)),
    song("Calamity", ["SupremeCalamitas"], DOKURO, "Stained, Brutal Calamity", ["stained brutal calamity"]),
    song("Calamity", ["PrimordialWyrm"], DOKURO, "Threats of the Ocean Floor", ["threats of the ocean floor"], ["PrimordialWyrm"]),
] + [
    song("Calamity", ["BossRush"], "CDMusic", f"Boss Rush Tier {n}", [f"boss rush tier {n}"], [f"BossRushTier{n}"], ("bossrush", n))
    for n in range(1, 6)
] + [
    # ---- Infernum Mode Music (all PinpinNeon)
    song("Infernum", ["SupremeCalamitas"], "PinpinNeon", "Scars of Calamity", ["scars of calamity"], ["Calamitas"]),
    song("Infernum", ["ExoMechs"], "PinpinNeon", "Catastrophic Fabrications", ["catastrophic fabrications"], ["ExoMechBosses"], ("exo", 1)),
    song("Infernum", ["ExoMechs"], "PinpinNeon", "Zenith Fabrications", ["zenith fabrications"], ["ZenithFabrications"], ("exo", 2)),
    song("Infernum", ["PrimordialWyrm"], "PinpinNeon", "Vocitus Terminus", ["vocitus terminus"], ["AdultEidolonWyrm"]),
    song("Infernum", ["Generic"], "PinpinNeon", "Storm Before Dawn", ["storm before dawn"], ["StormBeforeDawn"]),
    song("Infernum", ["Generic"], "PinpinNeon", "Duel for a Lost Kingdom", ["duel for a lost kingdom"], ["BereftVassal"]),

    # ---- Wrath of the Gods and other add-ons
    song("Extensions", ["AvatarOfEmptiness"], "ENNWAY!", "PARADISE PARASITE", ["paradise parasite"], ["AvatarRift"], ("avatar", 1)),
    song("Extensions", ["AvatarOfEmptiness"], "ENNWAY!", "PARADISE PARASITE", [], ["AvatarOfEmptinessP2"], ("avatar", 2)),
    song("Extensions", ["AvatarOfEmptiness"], "ENNWAY!", "PARADISE PARASITE", [], ["AvatarOfEmptinessP3"], ("avatar", 3)),
    song("Extensions", ["NamelessDeity"], NAMELESS, "TWISTED GARDEN", ["twisted garden"], ["NamelessDeity"], ("nameless", 1)),
    song("Extensions", ["NamelessDeity"], NAMELESS, "ARIA BEYOND THE SHINING FIRMAMENT", [], ["ARIA BEYOND THE SHINING FIRMAMENT"], ("nameless", 2)),
    song("Extensions", ["Mars"], "Moonburn", "RAMifications", ["ramifications"], ["Mars"]),
    # Edenic Whispers is the calm Eternal Garden theme, so it joins the general pool.
    song("Extensions", ["Generic"], "ENNWAY!", "Edenic Whispers", ["edenic whispers"], ["EternalGarden"]),
    song("Extensions", ["Goozma"], "The Exiled Fellow", "Glutinous Arbitration", ["glutinous arbitration", "glutinous arbiration"]),
    song("Extensions", ["Goozma"], "The Exiled Fellow", "Viscous Desperation", ["viscous desperation"]),
    song("Extensions", ["Astrageldon"], DOKURO, "Cosmic Disgust", ["cosmic disgust"]),
    song("Extensions", ["Astrageldon"], "Heart Plus Up!", "Galactic Destruction", ["galactic destruction"]),
]

SUPPORTED = {".ogg", ".mp3", ".wav"}
CONVERTIBLE = {".flac", ".m4a", ".aac", ".opus", ".webm", ".alac", ".aif", ".aiff", ".wma"}
AUDIO = SUPPORTED | CONVERTIBLE


def norm(text):
    """Lowercase, drop punctuation and extra spaces: "Raw, Unfiltered Calamity" -> "raw unfiltered calamity"."""
    text = text.lower().replace("&", " and ").replace("'", "")
    text = re.sub(r"[^a-z0-9]+", " ", text)
    return " ".join(text.split())


def safe_name(text):
    return re.sub(r'[<>:"/\\|?*]', "", text).strip()


def say(msg=""):
    print(msg, flush=True)


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
        if tml_version.startswith("0."):
            raise ValueError("mod built for a very old tModLoader (" + tml_version + ")")
        f.read(20 + 256 + 4)  # hash, signature, data length
        name = _read_dotnet_string(f)
        _read_dotnet_string(f)  # mod version
        count = struct.unpack("<i", f.read(4))[0]
        table = []
        for _ in range(count):
            fname = _read_dotnet_string(f)
            length, compressed = struct.unpack("<ii", f.read(8))
            table.append((fname, length, compressed))
        offset = f.tell()

    entries = []
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


def version_key(folder):
    parts = re.findall(r"\d+", folder)
    return tuple(int(p) for p in parts) if parts else (0,)


def pack_for(path, mod_name):
    for item_id, (_title, pack) in WORKSHOP_MODS.items():
        if os.sep + item_id + os.sep in path or "/" + item_id + "/" in path:
            return pack
    low = re.sub(r"[^a-z0-9]", "", mod_name.lower())
    for key, pack in MOD_NAME_PACKS:
        if key in low:
            return pack
    return None


def find_tmods(extra_roots=()):
    """Newest copy of each Calamity music mod: {mod name: (path, pack)}."""
    roots = [os.path.join(lib, "steamapps", "workshop", "content", TMODLOADER_APP_ID) for lib in steam_libraries()]
    roots += list(extra_roots)
    files = []
    for root in roots:
        files += glob.glob(os.path.join(root, "*", "**", "*.tmod"), recursive=True)
    home = os.path.expanduser("~")
    for docs in (os.path.join(home, "Documents"), os.path.join(home, "OneDrive", "Documents")):
        files += glob.glob(os.path.join(docs, "My Games", "Terraria", "tModLoader", "Mods", "*.tmod"))
    files += glob.glob(os.path.join(home, ".local", "share", "Terraria", "tModLoader", "Mods", "*.tmod"))

    best = {}
    for path in files:
        try:
            name, _ = read_tmod(path)
        except Exception:
            continue
        pack = pack_for(path, name)
        if pack is None:
            continue
        # Workshop keeps one copy per tModLoader version in folders like "2023.6", "2024.8".
        rank = (version_key(os.path.basename(os.path.dirname(path))), os.path.getmtime(path))
        if name not in best or rank > best[name][0]:
            best[name] = (rank, path, pack)
    return {name: (path, pack) for name, (_rank, path, pack) in best.items()}


def find_music_folders():
    """Music folders of every r2modman / Thunderstore Mod Manager profile with the mod, plus the plain game."""
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
        music = os.path.join(base, tail)
        if os.path.isdir(music) or glob.glob(os.path.join(base, "BepInEx", "plugins", "*CalamityMusic*")):
            found.append(music)
    return found


def find_bandcamp_album():
    home = os.path.expanduser("~")
    hits = []
    for folder in (os.path.join(home, "Downloads"), os.path.join(home, "Desktop"), os.path.join(home, "Music")):
        hits += glob.glob(os.path.join(folder, "*Tale of a Cruel World*"))
    hits = [h for h in hits if os.path.isdir(h) or zipfile.is_zipfile(h)]
    return max(hits, key=os.path.getmtime) if hits else None


# ----------------------------------------------------------------------------------------------
# SteamCMD
# ----------------------------------------------------------------------------------------------

def steamcmd_path(cache):
    return os.path.join(cache, "steamcmd", "steamcmd.exe" if sys.platform == "win32" else "steamcmd.sh")


def ensure_steamcmd(cache):
    exe = steamcmd_path(cache)
    if os.path.isfile(exe):
        return exe
    url = STEAMCMD_URLS.get("win32" if sys.platform == "win32" else "darwin" if sys.platform == "darwin" else "linux")
    say(f"Downloading SteamCMD from Valve ({url}) ...")
    target = os.path.dirname(exe)
    os.makedirs(target, exist_ok=True)
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read()
    if url.endswith(".zip"):
        zipfile.ZipFile(io.BytesIO(data)).extractall(target)
    else:
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as tar:
            if hasattr(tarfile, "data_filter"):
                tar.extractall(target, filter="data")
            else:
                tar.extractall(target)
        os.chmod(exe, 0o755)
    return exe


def run_steamcmd(exe, args):
    """Runs SteamCMD, showing its output, and returns everything it printed."""
    lines = []
    proc = subprocess.Popen([exe] + args, cwd=os.path.dirname(exe), stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, stdin=None, text=True, errors="replace")
    for line in proc.stdout:
        line = line.rstrip()
        lines.append(line)
        if line:
            say("    " + line)
    proc.wait()
    return "\n".join(lines)


def download_workshop_mods(cache, item_ids, steam_user=None):
    """Downloads Workshop items with SteamCMD. Returns the folder they land in."""
    exe = ensure_steamcmd(cache)
    content = os.path.join(os.path.dirname(exe), "steamapps", "workshop", "content", TMODLOADER_APP_ID)
    login = steam_user or "anonymous"
    for attempt in range(3):
        todo = [i for i in item_ids if not glob.glob(os.path.join(content, i, "**", "*.tmod"), recursive=True)]
        if not todo:
            break
        names = ", ".join(WORKSHOP_MODS[i][0] for i in todo)
        say(f"\nDownloading from Steam Workshop: {names}" + (" (retrying)" if attempt else "") +
            "\n  This is a few hundred MB and can take a while. Big items sometimes time out; it retries.")
        args = ["+login", login]
        for i in todo:
            args += ["+workshop_download_item", TMODLOADER_APP_ID, i, "validate"]
        args.append("+quit")
        output = run_steamcmd(exe, args)
        if login == "anonymous" and re.search(r"(No subscription|Access Denied|Failure)", output, re.IGNORECASE) \
                and not glob.glob(os.path.join(content, "*", "**", "*.tmod"), recursive=True):
            say("\n  Steam refused the anonymous download. Run again with --steam-user YOUR_STEAM_NAME")
            say("  (SteamCMD asks for your password and Steam Guard code itself), or subscribe to the mods")
            say("  in Steam and open tModLoader once.")
            break
    return content


# ----------------------------------------------------------------------------------------------
# Matching
# ----------------------------------------------------------------------------------------------

def match_mod_file(path_in_mod, pack):
    """Match a music mod's internal file (Sounds/Music/DesertScourge.ogg) by its exact name."""
    parts = re.split(r"[\\/]", path_in_mod)
    if not any(p.lower() == "music" for p in parts[:-1]):
        return None  # sound effects etc.
    stem = os.path.splitext(parts[-1])[0].lower()
    for entry in CATALOG:
        if entry["pack"] == pack and stem in entry["files"]:
            return entry
    return None


def is_music_file(path_in_mod):
    parts = re.split(r"[\\/]", path_in_mod)
    return any(p.lower() == "music" for p in parts[:-1]) and os.path.splitext(parts[-1])[1].lower() in AUDIO


def title_from_filename(filename):
    """'DM DOKURO - The Tale of a Cruel World - 02 Guardian of the Former Seas.mp3' -> 'Guardian of the Former Seas'."""
    stem = os.path.splitext(os.path.basename(filename))[0]
    stem = re.sub(r"\[[^\]]*\]", "", stem)
    stem = re.sub(r"\s*\((official[^)]*|hq|hd|lyrics?|audio|full song|4k|1080p)\)", "", stem, flags=re.IGNORECASE)
    parts = [p.strip() for p in stem.split(" - ")]
    last = parts[-1] if parts else stem
    return re.sub(r"^\d{1,3}[\s._-]+", "", last)


def match_title(filename):
    title = norm(title_from_filename(filename))
    if not title:
        return None
    for entry in CATALOG:
        if title in entry["album"]:
            return entry
    # Remixes on the album ("8-bit Mix", "Eulogy for the Ego"...) must not replace the real theme.
    if re.search(r"\b(mix|remix|eulogy|cover|extended|loop|8 bit|16 bit)\b", title):
        return None
    candidates = {t: e for e in CATALOG for t in e["album"]}
    close = difflib.get_close_matches(title, list(candidates), n=1, cutoff=0.88)
    return candidates[close[0]] if close else None


# ----------------------------------------------------------------------------------------------
# Writing
# ----------------------------------------------------------------------------------------------

class Installer:
    def __init__(self, music_dir, dry_run):
        self.music = music_dir
        self.dry = dry_run
        self.placed = {}
        self.added = 0
        self.skipped = []
        self.ffmpeg = shutil.which("ffmpeg")

    def place(self, entry, ext, get_bytes, source_label):
        ext = ext.lower()
        out_ext = ext if ext in SUPPORTED else ".ogg"
        if ext not in SUPPORTED and not self.ffmpeg:
            self.skipped.append(f"{source_label}: {ext} needs converting but ffmpeg isn't installed (download MP3 or Ogg instead)")
            return
        name = f"{entry['artist']} - {entry['title']}"
        if entry["seq"]:
            name += f" [seq={entry['seq'][0]} {entry['seq'][1]}]"
        name = safe_name(name)
        data = None
        for slot in entry["slots"]:
            key = (entry["pack"], slot, name)
            if key in self.placed:
                continue  # already got this song from an earlier source
            dest_dir = os.path.join(self.music, entry["pack"], slot)
            dest = os.path.join(dest_dir, name + out_ext)
            self.placed[key] = dest
            existing = glob.glob(os.path.join(glob.escape(dest_dir), glob.escape(name) + ".*"))
            if existing:
                say(f"  = {entry['pack']}/{slot}/{os.path.basename(existing[0])} (already there)")
                continue
            say(f"  + {entry['pack']}/{slot}/{name}{out_ext}")
            self.added += 1
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

    def missing_bosses(self):
        have = {(pack, slot) for (pack, slot, _name) in self.placed}
        missing = []
        for entry in CATALOG:
            if entry["pack"] != "Calamity":
                continue
            for slot in entry["slots"]:
                if (entry["pack"], slot) not in have and slot not in missing and slot != "MoonEscape":
                    missing.append(slot)
        return missing


def install_from_tmod(inst, mod_name, path, pack, unsorted_dir):
    say(f"\n{mod_name}  ({path})")
    _, entries = read_tmod(path)
    for fname, reader in entries:
        if not is_music_file(fname):
            continue
        entry = match_mod_file(fname, pack)
        if entry:
            inst.place(entry, os.path.splitext(fname)[1], reader, f"{mod_name}:{fname}")
        elif unsorted_dir and not inst.dry:
            # Biome themes and other non-boss music, kept to one side in case you want them.
            dest = os.path.join(unsorted_dir, pack, safe_name(os.path.basename(fname)))
            if not os.path.exists(dest):
                os.makedirs(os.path.dirname(dest), exist_ok=True)
                with open(dest, "wb") as f:
                    f.write(reader())


def iter_audio(source):
    """Yields (name, extension, bytes getter) for a zip file or folder."""
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
        say(f"  ! {source} is not a zip file or folder")


def install_from_files(inst, source):
    say(f"\n{source}")
    unmatched = []
    for name, ext, getter in iter_audio(source):
        entry = match_title(name)
        if entry:
            inst.place(entry, ext, getter, os.path.basename(name))
        else:
            unmatched.append(os.path.basename(name))
    if unmatched:
        say(f"  ({len(unmatched)} file(s) aren't boss themes and were left alone, e.g. {unmatched[0]})")


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
        say("Found CalamityMusic in more than one profile:")
        for i, f in enumerate(found, 1):
            say(f"  {i}. {f}")
        pick = ask("Which one? [1] ", "1")
        if pick.isdigit() and 1 <= int(pick) <= len(found):
            return found[int(pick) - 1]
    say("Couldn't find the CalamityMusic mod. Install it with r2modman and start the game once,")
    say("or paste the path to BepInEx/config/CalamityMusic/Music.")
    path = ask("Music folder: ")
    return os.path.abspath(path) if path else None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--music", help="the mod's Music folder (BepInEx/config/CalamityMusic/Music)")
    ap.add_argument("--no-download", action="store_true", help="don't download anything; use what's on this PC")
    ap.add_argument("--steam-user", help="log SteamCMD in with this Steam account")
    ap.add_argument("--bandcamp", help="Bandcamp album zip or folder")
    ap.add_argument("--folder", action="append", default=[], help="any folder or zip of song files")
    ap.add_argument("--dry-run", action="store_true", help="show what would be copied")
    ap.add_argument("--keep-downloads", action="store_true", help="keep the downloaded Workshop files")
    ap.add_argument("--no-unsorted", action="store_true", help="don't keep the non-boss music from the music mods")
    ap.add_argument("--cache", help="where to put SteamCMD and downloads (default: next to this script)")
    ap.add_argument("--list-tmod", help="list the files inside a .tmod and exit")
    args = ap.parse_args()

    if args.list_tmod:
        name, entries = read_tmod(args.list_tmod)
        say(name)
        for fname, _ in entries:
            say("  " + fname)
        return 0

    music = choose_music_folder(args.music)
    if not music:
        return 1
    say(f"Music folder: {music}")
    inst = Installer(music, args.dry_run)
    unsorted = None if args.no_unsorted else os.path.join(os.path.dirname(music), "Unsorted")
    cache = os.path.abspath(args.cache or os.path.join(os.path.dirname(os.path.abspath(__file__)), "calamity_song_downloads"))
    workshop_dir = os.path.join(cache, "steamcmd", "steamapps", "workshop", "content", TMODLOADER_APP_ID)

    # 1. Music mods already on this PC, then download whatever is missing.
    mods = find_tmods([workshop_dir])
    have_packs = {pack for (_path, pack) in mods.values()}
    missing = [i for i, (_t, pack) in WORKSHOP_MODS.items() if pack not in have_packs]
    downloaded = False
    if missing and not args.no_download and not args.dry_run:
        try:
            download_workshop_mods(cache, missing, args.steam_user)
            downloaded = True
        except Exception as e:
            say(f"\n  ! Download failed: {e}")
            say("    You can subscribe to the mods in Steam instead (links at the top of this script),")
            say("    open tModLoader once so they download, then run this again.")
        mods = find_tmods([workshop_dir])
    elif missing and args.dry_run:
        say("\n(dry run: would download " + ", ".join(WORKSHOP_MODS[i][0] for i in missing) + " from Steam Workshop)")

    for name in sorted(mods):
        path, pack = mods[name]
        install_from_tmod(inst, name, path, pack, unsorted)

    # 2. Bandcamp album and loose folders.
    sources = list(args.folder)
    album = args.bandcamp or find_bandcamp_album()
    if album:
        sources.insert(0, album)
    for source in sources:
        install_from_files(inst, source)

    # 3. Tidy up the big Workshop downloads (SteamCMD itself stays for next time).
    if downloaded and not args.keep_downloads and not args.dry_run:
        shutil.rmtree(os.path.join(cache, "steamcmd", "steamapps", "workshop"), ignore_errors=True)

    say()
    if args.dry_run:
        say("Dry run: nothing was copied.")
    for s in inst.skipped:
        say("  ! " + s)
    say(f"Done. {inst.added} new song file(s) added.")
    if not args.dry_run and os.path.isdir(music):
        # The mod watches this file and reloads its songs as soon as it changes.
        with open(os.path.join(music, ".songs_installed"), "w") as f:
            f.write(str(inst.added))
    missing_bosses = inst.missing_bosses()
    if missing_bosses:
        say("Boss folders still empty (those bosses borrow other songs): " + ", ".join(missing_bosses))
    if unsorted and os.path.isdir(unsorted):
        say(f"Other music from the mods (biomes etc.) is in {unsorted} if you want it.")
    say("In game: Settings > Mod Options > Calamity Music > Rescan Music Folder (or just start a new stage).")
    return 0


if __name__ == "__main__":
    code = 1
    try:
        code = main()
    except KeyboardInterrupt:
        say("\nCancelled.")
    finally:
        # Keep the window open when double-clicked on Windows (the .bat launcher pauses on its own).
        if sys.platform == "win32" and not os.environ.get("CALAMITY_NO_PAUSE") and sys.stdin and sys.stdin.isatty():
            ask("\nPress Enter to close.")
    sys.exit(code)
