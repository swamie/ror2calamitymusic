"""End-to-end test for tools/get_songs.py: fake Workshop music mods (using the real internal file
names from the Calamity / Infernum Music / Wrath of the Gods sources), a fake Bandcamp album in
Downloads and a fake r2modman profile, all in a temporary HOME.

usage: python3 tests/test_get_songs.py <fixtures dir>
"""
import os
import struct
import subprocess
import sys
import tempfile
import zipfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "tools", "get_songs.py")


def s7(text):
    b = text.encode()
    n, out = len(b), bytearray()
    while True:
        x = n & 0x7F
        n >>= 7
        out.append(x | (0x80 if n else 0))
        if not n:
            break
    return bytes(out) + b


def write_tmod(path, name, files):
    """Writes a .tmod the way tModLoader's TmodFile.cs does (some entries deflated)."""
    body = bytearray(s7(name) + s7("1.0") + struct.pack("<i", len(files)))
    blobs = []
    for i, (fname, data) in enumerate(files):
        stored = data
        if i % 2:
            c = zlib.compressobj(9, zlib.DEFLATED, -15)
            stored = c.compress(data) + c.flush()
        body += s7(fname) + struct.pack("<ii", len(data), len(stored))
        blobs.append(stored)
    for b in blobs:
        body += b
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"TMOD" + s7("2024.8.3.0") + bytes(20 + 256) + struct.pack("<i", len(body)) + body)


def make_env(home, fixtures):
    ogg = open(os.path.join(fixtures, [f for f in os.listdir(fixtures) if f.endswith(".ogg")][0]), "rb").read()
    mp3 = open(os.path.join(fixtures, [f for f in os.listdir(fixtures) if f.endswith(".mp3")][0]), "rb").read()
    ws = os.path.join(home, ".steam", "steam", "steamapps", "workshop", "content", "1281930")

    cal = ["DesertScourge", "CalamitasClone", "CalamitasPhase1", "CalamitasPhase2", "CalamitasPhase3", "CalamitasDefeat",
           "DevourerofGodsPhase1", "DevourerofGodsPhase2", "YharonPhase1", "YharonPhase2", "ExoMechs", "BossRushTier1",
           "BossRushTier2", "Anahita", "AnahitasLure", "Leviathan", "BrimstoneElemental", "BrimstoneCrags", "AstralInfection"]
    write_tmod(os.path.join(ws, "2824688266", "2024.8", "CalamityModMusic.tmod"), "CalamityModMusic",
               [("Sounds/Music/" + n + ".ogg", ogg) for n in cal] + [("icon.png", b"png"), ("Info", b"x")])
    # Older copy for a previous tModLoader version: must be ignored.
    write_tmod(os.path.join(ws, "2824688266", "2023.6", "CalamityModMusic.tmod"), "CalamityModMusic",
               [("Sounds/Music/Ravager.ogg", ogg)])
    write_tmod(os.path.join(ws, "3458992153", "2024.8", "InfernumModeMusic.tmod"), "InfernumModeMusic",
               [("Sounds/Music/" + n + ".ogg", ogg) for n in
                ["Calamitas", "ExoMechBosses", "ExoMechBossesOld", "ZenithFabrications", "AdultEidolonWyrm", "Draedon", "StormBeforeDawn", "EyeOfCthulhu"]])
    write_tmod(os.path.join(ws, "2995193002", "2024.8", "NoxusBoss.tmod"), "NoxusBoss",
               [("Assets/Sounds/Music/" + n + ".ogg", ogg) for n in
                ["AvatarRift", "AvatarOfEmptinessP2", "AvatarOfEmptinessP3", "NamelessDeity",
                 "ARIA BEYOND THE SHINING FIRMAMENT", "Mars", "EternalGarden", "EndCredits"]]
               + [("Assets/Sounds/Custom/Avatar/Ambience.ogg", ogg), ("Assets/Sounds/Custom/Mars/Mars.ogg", ogg)])
    write_tmod(os.path.join(ws, "1111", "2024.8", "SomeOtherMod.tmod"), "SomeOtherMod", [("Sounds/Music/DesertScourge.ogg", ogg)])

    prof = os.path.join(home, ".config", "r2modmanPlus-local", "RiskOfRain2", "profiles", "Default")
    os.makedirs(os.path.join(prof, "BepInEx", "plugins", "swamie-CalamityMusic"), exist_ok=True)

    os.makedirs(os.path.join(home, "Downloads"), exist_ok=True)
    with zipfile.ZipFile(os.path.join(home, "Downloads", "DM DOKURO - The Tale of a Cruel World.zip"), "w") as z:
        for n, t in [(1, "The Tale of a Cruel World"), (12, "The Leviathan Trilogy"), (23, "Servants of the Scourge"),
                     (24, "Scourge of the Universe"), (39, "Stained, Brutal Calamity"), (41, "Guardian of the Former Seas (8-bit Mix)"),
                     (45, "Scourge of the Universe (Eulogy for the Ego)"), (3, "Infestation"), (13, "Cosmic Disgust")]:
            z.writestr(f"DM DOKURO - The Tale of a Cruel World - {n:02d} {t}.mp3", mp3)
        z.writestr("cover.jpg", b"jpg")


def main():
    fixtures = os.path.abspath(sys.argv[1])
    home = tempfile.mkdtemp()
    make_env(home, fixtures)
    env = dict(os.environ, HOME=home, CALAMITY_NO_PAUSE="1")
    run = subprocess.run([sys.executable, SCRIPT, "--no-download"], env=env, capture_output=True, text=True)
    print(run.stdout[-3000:])
    if run.returncode != 0:
        print(run.stderr)
        return 1

    music = os.path.join(home, ".config", "r2modmanPlus-local", "RiskOfRain2", "profiles", "Default",
                         "BepInEx", "config", "CalamityMusic", "Music")
    expect = [
        "Calamity/DesertScourge/DM DOKURO - Guardian of the Former Seas.ogg",
        "Calamity/SupremeCalamitas/DM DOKURO - Stained, Brutal Calamity [seq=scal 1].ogg",
        "Calamity/SupremeCalamitas/DM DOKURO - Stained, Brutal Calamity [seq=scal 3].ogg",
        "Calamity/SupremeCalamitas/DM DOKURO - Stained, Brutal Calamity.mp3",
        "Calamity/DevourerOfGods/DM DOKURO - Scourge of the Universe [seq=dog 1].ogg",
        "Calamity/DevourerOfGods/DM DOKURO - UNIVERSAL COLLAPSE [seq=dog 2].ogg",
        "Calamity/MoonEscape/DM DOKURO - UNIVERSAL COLLAPSE [seq=dog 2].ogg",
        "Calamity/Yharon/DM DOKURO - Roar of the Jungle Dragon [seq=yharon 2].ogg",
        "Calamity/ExoMechs/PinpinNeon - Inferior Fabrications.ogg",
        "Calamity/BossRush/CDMusic - Boss Rush Tier 2 [seq=bossrush 2].ogg",
        "Calamity/LeviathanAnahita/DM DOKURO - Siren's War Cry.ogg",
        "Calamity/LeviathanAnahita/DM DOKURO - The Leviathan Trilogy.mp3",
        "Calamity/BrimstoneElemental/DM DOKURO - Left Alone.ogg",
        "Calamity/Signus/DM DOKURO - Servants of the Scourge.mp3",
        "Calamity/Crabulon/DM DOKURO - Infestation.mp3",
        "Infernum/SupremeCalamitas/PinpinNeon - Scars of Calamity.ogg",
        "Infernum/ExoMechs/PinpinNeon - Catastrophic Fabrications [seq=exo 1].ogg",
        "Infernum/ExoMechs/PinpinNeon - Zenith Fabrications [seq=exo 2].ogg",
        "Infernum/PrimordialWyrm/PinpinNeon - Vocitus Terminus.ogg",
        "Infernum/Generic/PinpinNeon - Storm Before Dawn.ogg",
        "Extensions/AvatarOfEmptiness/ENNWAY! - PARADISE PARASITE [seq=avatar 1].ogg",
        "Extensions/AvatarOfEmptiness/ENNWAY! - PARADISE PARASITE [seq=avatar 3].ogg",
        "Extensions/NamelessDeity/Heart Plus Up! & ENNWAY! - ARIA BEYOND THE SHINING FIRMAMENT [seq=nameless 2].ogg",
        "Extensions/Mars/Moonburn - RAMifications.ogg",
        "Extensions/Generic/ENNWAY! - Edenic Whispers.ogg",
        "Extensions/Astrageldon/DM DOKURO - Cosmic Disgust.mp3",
    ]
    failed = 0
    for rel in expect:
        ok = os.path.isfile(os.path.join(music, rel))
        failed += not ok
        print(("  ok   " if ok else "  FAIL ") + rel)

    # Nothing that isn't a boss fight theme may land in a boss folder.
    placed = [os.path.relpath(os.path.join(r, f), music) for r, _d, fs in os.walk(music) for f in fs
              if not f.endswith(".txt")]
    for bad in ("Defeat", "Lure", "Crags", "Eulogy", "8-bit", "Old", "Draedon", "Ambience", "EndCredits", "Ravager"):
        hits = [p for p in placed if bad.lower() in p.lower()]
        failed += bool(hits)
        print(("  FAIL " + str(hits)) if hits else f"  ok   no '{bad}' in boss folders")

    unsorted = os.path.join(os.path.dirname(music), "Unsorted")
    for rel in ("Calamity/CalamitasDefeat.ogg", "Calamity/BrimstoneCrags.ogg", "Infernum/ExoMechBossesOld.ogg"):
        ok = os.path.isfile(os.path.join(unsorted, rel))
        failed += not ok
        print(("  ok   " if ok else "  FAIL ") + "kept in Unsorted: " + rel)
    sfx = os.path.isfile(os.path.join(unsorted, "Extensions", "Ambience.ogg"))
    failed += sfx
    print(("  FAIL " if sfx else "  ok   ") + "sound effects ignored")

    # Running again changes nothing.
    again = subprocess.run([sys.executable, SCRIPT, "--no-download"], env=env, capture_output=True, text=True)
    ok = "Done. 0 new song file(s) added." in again.stdout
    failed += not ok
    print(("  ok   " if ok else "  FAIL ") + "second run adds nothing")

    failed += test_download_flow(fixtures)

    print("ALL PASSED" if not failed else f"{failed} FAILED")
    return 1 if failed else 0


FAKE_STEAMCMD = r'''
import os, sys
sys.path.insert(0, os.environ["FAKE_HELPER_DIR"])
from test_get_songs import write_tmod
here = os.path.dirname(os.path.abspath(__file__))
state = os.path.join(here, "calls.txt")
calls = open(state).read().count("\n") if os.path.exists(state) else 0
with open(state, "a") as f:
    f.write(" ".join(sys.argv[1:]) + "\n")
ogg = open(os.environ["FAKE_OGG"], "rb").read()
names = {"2824688266": ("CalamityModMusic", ["DesertScourge", "CalamitasPhase1"]),
         "3458992153": ("InfernumModeMusic", ["Calamitas"]),
         "2995193002": ("NoxusBoss", ["Mars"])}
args = sys.argv[1:]
ids = [args[i + 2] for i, a in enumerate(args) if a == "+workshop_download_item"]
for n, item in enumerate(ids):
    if calls == 0 and n == len(ids) - 1:
        print("ERROR! Timeout downloading item " + item)  # first run: last item times out
        continue
    mod, files = names[item]
    write_tmod(os.path.join(here, "steamapps", "workshop", "content", "1281930", item, mod + ".tmod"),
               mod, [("Sounds/Music/" + f + ".ogg", ogg) for f in files])
    print("Success. Downloaded item " + item)
'''


def test_download_flow(fixtures):
    """Runs the full one-click flow with a stand-in SteamCMD (the real one can't run in CI)."""
    home = tempfile.mkdtemp()
    prof = os.path.join(home, ".config", "r2modmanPlus-local", "RiskOfRain2", "profiles", "Default")
    os.makedirs(os.path.join(prof, "BepInEx", "plugins", "swamie-CalamityMusic"), exist_ok=True)
    cache = os.path.join(home, "cache")
    steamcmd = os.path.join(cache, "steamcmd")
    os.makedirs(steamcmd)
    with open(os.path.join(steamcmd, "fake.py"), "w") as f:
        f.write(FAKE_STEAMCMD)
    sh = os.path.join(steamcmd, "steamcmd.sh")
    with open(sh, "w") as f:
        f.write('#!/bin/sh\nexec "%s" "$(dirname "$0")/fake.py" "$@"\n' % sys.executable)
    os.chmod(sh, 0o755)

    ogg = os.path.join(fixtures, [f for f in os.listdir(fixtures) if f.endswith(".ogg")][0])
    env = dict(os.environ, HOME=home, CALAMITY_NO_PAUSE="1", FAKE_HELPER_DIR=HERE, FAKE_OGG=ogg)
    run = subprocess.run([sys.executable, SCRIPT, "--cache", cache], env=env, capture_output=True, text=True)
    music = os.path.join(prof, "BepInEx", "config", "CalamityMusic", "Music")
    calls = open(os.path.join(steamcmd, "calls.txt")).read().splitlines()
    checks = [
        (run.returncode == 0, "one-click run succeeds"),
        (len(calls) == 2 and "+login anonymous" in calls[0], f"SteamCMD called anonymously, retried the timed-out item ({len(calls)} calls)"),
        (os.path.isfile(os.path.join(music, "Calamity", "DesertScourge", "DM DOKURO - Guardian of the Former Seas.ogg")), "Calamity song from the download installed"),
        (os.path.isfile(os.path.join(music, "Extensions", "Mars", "Moonburn - RAMifications.ogg")), "retried item installed"),
        (os.path.isfile(os.path.join(music, "Infernum", "SupremeCalamitas", "PinpinNeon - Scars of Calamity.ogg")), "Infernum song installed"),
        (not os.path.exists(os.path.join(steamcmd, "steamapps", "workshop")), "downloaded Workshop files cleaned up"),
    ]
    failed = 0
    for ok, what in checks:
        failed += not ok
        print(("  ok   " if ok else "  FAIL ") + what)
    if failed:
        print(run.stdout[-2000:], run.stderr[-2000:])
    return failed


if __name__ == "__main__":
    sys.exit(main())
