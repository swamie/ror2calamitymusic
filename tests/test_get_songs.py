"""End-to-end test for tools/get_songs.py: fake Workshop mods, a fake Bandcamp zip and a fake
r2modman profile in a temporary HOME. usage: python3 tests/test_get_songs.py <fixtures dir>"""
import os
import struct
import subprocess
import sys
import tempfile
import zipfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "tools", "get_songs.py")


def make_env(home, fixtures):
    ogg = open(os.path.join(fixtures, [f for f in os.listdir(fixtures) if f.endswith('.ogg')][0]), 'rb').read()
    mp3 = open(os.path.join(fixtures, [f for f in os.listdir(fixtures) if f.endswith('.mp3')][0]), 'rb').read()

    def s7(t):
        b = t.encode(); n = len(b); out = bytearray()
        while True:
            x = n & 0x7F; n >>= 7
            out.append(x | (0x80 if n else 0))
            if not n: break
        return bytes(out) + b

    def tmod(path, name, files):
        body = bytearray(s7(name) + s7("1.2.3") + struct.pack("<i", len(files)))
        blobs = []
        for fname, data, compress in files:
            c = zlib.compressobj(9, zlib.DEFLATED, -15); comp = c.compress(data) + c.flush()
            stored = comp if compress else data
            body += s7(fname) + struct.pack("<ii", len(data), len(stored))
            blobs.append(stored)
        for b in blobs: body += b
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(b"TMOD" + s7("2024.8.3.0") + bytes(20) + bytes(256) + struct.pack("<i", len(body)) + body)

    ws = os.path.join(home, ".steam", "steam", "steamapps", "workshop", "content", "1281930")
    cal = [("Sounds/Music/DesertScourge.ogg", ogg, False), ("Sounds/Music/CalamitasClone.ogg", ogg, False),
           ("Sounds/Music/SupremeCalamitas.ogg", ogg, False), ("Sounds/Music/DevourerOfGodsP1.ogg", ogg, False),
           ("Sounds/Music/DevourerOfGodsP2.ogg", ogg, True), ("Sounds/Music/DevourerOfGodsEulogy.ogg", ogg, False),
           ("Sounds/Music/Yharon.ogg", ogg, False), ("Sounds/Music/YharonP2.ogg", ogg, False),
           ("Sounds/Music/ExoMechs.ogg", ogg, False), ("Sounds/Music/BossRushTier1.ogg", ogg, False),
           ("Sounds/Music/Anahita.ogg", ogg, False), ("Sounds/Music/Leviathan.ogg", ogg, False),
           ("Sounds/Music/AstralInfection.ogg", ogg, False), ("icon.png", b"png", True), ("Info", b"x", True)]
    tmod(os.path.join(ws, "2824688266", "2024.8", "CalamityModMusic.tmod"), "CalamityModMusic", cal)
    tmod(os.path.join(ws, "2824688266", "2023.6", "CalamityModMusic.tmod"), "CalamityModMusic", cal[:2])  # older copy
    tmod(os.path.join(ws, "3458992153", "2024.8", "InfernumModeMusic.tmod"), "InfernumModeMusic",
         [("Sounds/Music/SupremeCalamitas.ogg", ogg, False), ("Sounds/Music/ExoMechs.ogg", ogg, False),
          ("Sounds/Music/TheirFabricator.ogg", ogg, False), ("Sounds/Music/EyeOfCthulhu.ogg", ogg, False)])
    tmod(os.path.join(ws, "2995193002", "2024.8", "NoxusBoss.tmod"), "NoxusBoss",
         [("Assets/Sounds/Music/AvatarRift.ogg", ogg, False), ("Assets/Sounds/Music/AvatarOfEmptinessP2.ogg", ogg, False),
          ("Assets/Sounds/Music/NamelessDeity.ogg", ogg, False), ("Assets/Sounds/Music/EternalGarden.ogg", ogg, False)])
    tmod(os.path.join(ws, "1111", "2024.8", "SomeOtherMod.tmod"), "SomeOtherMod", [("Sounds/Music/DesertScourge.ogg", ogg, False)])

    prof = os.path.join(home, ".config", "r2modmanPlus-local", "RiskOfRain2", "profiles", "Default")
    os.makedirs(os.path.join(prof, "BepInEx", "plugins", "swamie-CalamityMusic"), exist_ok=True)

    with zipfile.ZipFile(os.path.join(home, "album.zip"), "w") as z:
        for n, t in [(1, "The Tale of a Cruel World"), (10, "Raw, Unfiltered Calamity"), (12, "The Leviathan Trilogy"),
                     (23, "Servants of the Scourge"), (24, "Scourge of the Universe"), (25, "Universal Collapse"),
                     (39, "Stained, Brutal Calamity"), (41, "Guardian of the Former Seas (8-bit Mix)"),
                     (45, "Scourge of the Universe (Eulogy for the Ego)"), (3, "Infestation"), (13, "Cosmic Disgust")]:
            z.writestr(f"DM DOKURO - The Tale of a Cruel World - {n:02d} {t}.mp3", mp3)
        z.writestr("cover.jpg", b"jpg")



def main():
    fixtures = sys.argv[1]
    home = tempfile.mkdtemp()
    make_env(home, fixtures)
    env = dict(os.environ, HOME=home)
    run = subprocess.run([sys.executable, SCRIPT, "--workshop", "--bandcamp", os.path.join(home, "album.zip")],
                         env=env, capture_output=True, text=True)
    print(run.stdout)
    assert run.returncode == 0, run.stderr
    music = os.path.join(home, ".config", "r2modmanPlus-local", "RiskOfRain2", "profiles", "Default",
                         "BepInEx", "config", "CalamityMusic", "Music")
    expect = [
        "Calamity/DesertScourge/DM DOKURO - Guardian of the Former Seas.ogg",
        "Calamity/DevourerOfGods/DM DOKURO - UNIVERSAL COLLAPSE.ogg",
        "Calamity/MoonEscape/DM DOKURO - UNIVERSAL COLLAPSE.ogg",
        "Calamity/Yharon/DM DOKURO - Roar of the Jungle Dragon.ogg",
        "Calamity/ExoMechs/PinpinNeon - Inferior Fabrications.ogg",
        "Calamity/Signus/DM DOKURO - Servants of the Scourge.mp3",
        "Infernum/SupremeCalamitas/PinpinNeon - Scars of Calamity.ogg",
        "Extensions/AvatarOfEmptiness/ENNWAY! - PARADISE PARASITE (Phase 2).ogg",
        "Extensions/Generic/ENNWAY! - Edenic Whispers.ogg",
        "Extensions/Astrageldon/DM DOKURO - Cosmic Disgust.mp3",
    ]
    absent = [
        "Calamity/DevourerOfGods/DM DOKURO - Scourge of the Universe (DevourerOfGodsEulogy).ogg",
        "Infernum/ExoMechs/PinpinNeon - Catastrophic Fabrications (Part 2).ogg",
    ]
    failed = 0
    for rel in expect:
        ok = os.path.isfile(os.path.join(music, rel))
        failed += not ok
        print(("  ok   " if ok else "  FAIL ") + rel)
    for rel in absent:
        ok = not os.path.exists(os.path.join(music, rel))
        failed += not ok
        print(("  ok   not " if ok else "  FAIL unexpected ") + rel)
    unsorted = os.path.join(os.path.dirname(music), "Unsorted", "Calamity", "DevourerOfGodsEulogy.ogg")
    ok = os.path.isfile(unsorted)
    failed += not ok
    print(("  ok   " if ok else "  FAIL ") + "non-fight track kept in Unsorted")
    print("ALL PASSED" if not failed else f"{failed} FAILED")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
