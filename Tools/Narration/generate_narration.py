"""
Voices every line in lines.json with the open-source Kokoro TTS model (run
locally, no account or API key), into Assets/Resources/Narration/<key>.ogg
for NarrationManager to play.

<key> is a hash of the line's on-screen text (rich-text tags removed,
whitespace collapsed). NarrationManager.KeyFor computes the same hash at
runtime, so the game finds a clip from the text alone — and a line whose text
is edited in code just stays silent (with a console warning naming it) until
lines.json is updated and this is re-run.

Setup, once, from the project root:
    py -3.12 -m venv Tools/Narration/.venv
    Tools/Narration/.venv/Scripts/python -m pip install -r Tools/Narration/requirements.txt
The Kokoro model (~350 MB) downloads into Tools/Narration/models/ on first run.

Run, from the project root:
    Tools/Narration/.venv/Scripts/python Tools/Narration/generate_narration.py
        --force       regenerate clips that already exist
        --only L1-05  only lines whose id starts with this
        --prune       delete clips that no line in lines.json uses any more
        --dry-run     print what would be spoken, generate nothing

lines.json, per line:
    "text"   exactly what the game shows (copy it from the C# source).
    "vars"   for a line the game fills in at runtime: each {name} in text is
             expanded over every combination of these values...
    "cases"  ...or over just these listed combinations.
    "say"    optional — spoken instead of text, for a pronunciation that the
             rules in speakable() don't already fix.
"""
import argparse
import itertools
import json
import re
import sys
import urllib.request
from pathlib import Path

import numpy as np
import soundfile as sf

HERE = Path(__file__).resolve().parent
OUT_DIR = HERE.parents[1] / "Assets" / "Resources" / "Narration"
INDEX_FILE = HERE / "clip_index.tsv"
MODEL_DIR = HERE / "models"
MODEL_URL = "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/"
MODEL_FILES = ("kokoro-v1.0.onnx", "voices-v1.0.bin")

PEAK = 0.89          # normalize every clip to ~-1 dBFS so lines play at one loudness
SILENCE = 0.01       # amplitude under which leading/trailing audio counts as silence
PAD_SECONDS = 0.05   # kept either side of the speech after trimming
WRITE_BLOCK = 4096   # frames per write — libsndfile's Vorbis encoder overflows the stack on one big write

# ── Clip key (must match NarrationManager.KeyFor) ────────────────────────────

TAGS = re.compile(r"<[^>]*>")
SPACES = re.compile(r"\s+")


def normalize(text):
    return SPACES.sub(" ", TAGS.sub("", text)).strip()


def clip_key(text):
    """64-bit FNV-1a over the normalized text's UTF-8 bytes, as 16 hex digits."""
    h = 0xCBF29CE484222325
    for b in normalize(text).encode("utf-8"):
        h = ((h ^ b) * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return f"{h:016x}"


# ── Spoken form ──────────────────────────────────────────────────────────────

IP = r"(?:\d{1,3}|x)(?:\.(?:\d{1,3}|x)){3}"
ONES = ("zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen "
        "sixteen seventeen eighteen nineteen").split()
TENS = "_ _ twenty thirty forty fifty sixty seventy eighty ninety".split()


def number(n):
    """How a network engineer reads an octet: 192 -> "one ninety-two", 0 -> "zero"."""
    if n < 20:
        return ONES[n]
    if n < 100:
        return TENS[n // 10] + (f"-{ONES[n % 10]}" if n % 10 else "")
    hundreds, rest = divmod(n, 100)
    if rest == 0:
        return f"{ONES[hundreds]} hundred"
    return f"{ONES[hundreds]} " + (f"oh {ONES[rest]}" if rest < 10 else number(rest))


def say_address(match):
    words = " dot ".join(o if o == "x" else number(int(o)) for o in match.group(1).split("."))
    return words + (f" slash {number(int(match.group(2)))}" if match.group(2) else "")


# Applied in order to the tag-free text. Kokoro reads "192.168.1.10" as "one
# hundred ninety two point one six eight...", "WASD" as one word, "CLI" as
# "cly", "e.g." letter by letter, "/" as "slash" and "→" as "right arrow".
RULES = [
    (re.compile(rf"({IP}) (?:/ )?(?={IP})"), r"\1, mask "),  # an address followed by its mask
    (re.compile(r"\be\.g\. "), "for example, "),
    (re.compile(r"\bWASD\b"), "W, A, S, D"),
    (re.compile(r"\bCLI\b"), "C L I"),
    (re.compile(r" / "), " or "),
    (re.compile(r" → "), ", then "),
    (re.compile(rf"(?<![\w.])({IP})(?:/(\d{{1,2}}))?"), say_address),
]


def speakable(text):
    text = normalize(text)
    for pattern, replacement in RULES:
        text = pattern.sub(replacement, text)
    return text


# ── Lines ────────────────────────────────────────────────────────────────────

HOLE = re.compile(r"\{(\w+)\}")


def expand(entry):
    """(display text, spoken text) for every variant of one lines.json entry."""
    if "cases" in entry:
        combos = entry["cases"]
    elif "vars" in entry:
        names = list(entry["vars"])
        combos = [dict(zip(names, values)) for values in itertools.product(*entry["vars"].values())]
    else:
        combos = [{}]

    for combo in combos:
        def fill(s):
            return HOLE.sub(lambda m: combo[m.group(1)], s)
        yield fill(entry["text"]), fill(entry.get("say", entry["text"]))


def collect(manifest):
    """key -> (id, display text, spoken text), in lines.json order."""
    clips = {}
    for entry in manifest["lines"]:
        for text, say in expand(entry):
            key = clip_key(text)
            if key in clips and clips[key][1] != normalize(text):
                sys.exit(f"Hash collision between {clips[key][0]} and {entry['id']} — reword one of them.")
            clips.setdefault(key, (entry["id"], normalize(text), speakable(say)))
    return clips


# ── Audio ────────────────────────────────────────────────────────────────────

def ensure_models():
    MODEL_DIR.mkdir(exist_ok=True)
    for name in MODEL_FILES:
        path = MODEL_DIR / name
        if path.exists():
            continue
        print(f"Downloading {name} ...")
        partial = path.with_suffix(path.suffix + ".part")
        urllib.request.urlretrieve(MODEL_URL + name, partial)
        partial.rename(path)
    return [str(MODEL_DIR / name) for name in MODEL_FILES]


def synthesize(kokoro, text, voice, speed):
    samples, rate = kokoro.create(text, voice=voice, speed=speed, lang="en-us")
    loud = np.flatnonzero(np.abs(samples) > SILENCE)
    if loud.size:
        pad = int(PAD_SECONDS * rate)
        samples = samples[max(loud[0] - pad, 0): loud[-1] + pad]
    peak = np.max(np.abs(samples))
    if peak > 0:
        samples = samples * (PEAK / peak)
    return samples.astype(np.float32), rate


def write_ogg(path, samples, rate):
    """Written to a temp file and renamed once complete, so an interrupted run never
    leaves a truncated clip behind for the next run to skip as already done."""
    partial = path.with_name(path.name + ".part")
    with sf.SoundFile(str(partial), "w", rate, 1, format="OGG", subtype="VORBIS") as out:
        for start in range(0, len(samples), WRITE_BLOCK):
            out.write(samples[start:start + WRITE_BLOCK])
    partial.replace(path)


# ── Main ─────────────────────────────────────────────────────────────────────

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Generate narration clips from lines.json.")
    parser.add_argument("--force", action="store_true", help="regenerate clips that already exist")
    parser.add_argument("--only", default="", help="only lines whose id starts with this")
    parser.add_argument("--prune", action="store_true", help="delete clips no line uses any more")
    parser.add_argument("--dry-run", action="store_true", help="print what would be spoken, generate nothing")
    args = parser.parse_args()

    manifest = json.loads((HERE / "lines.json").read_text(encoding="utf-8"))
    clips = collect(manifest)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    todo = [(key, clip) for key, clip in clips.items()
            if clip[0].startswith(args.only) and (args.force or not (OUT_DIR / f"{key}.ogg").exists())]

    if args.dry_run:
        for key, (line_id, _, spoken) in todo:
            print(f"{line_id:7} {key}  {spoken}")
        print(f"{len(todo)} of {len(clips)} clips would be generated.")
        return

    if todo:
        from kokoro_onnx import Kokoro  # imported here so --dry-run works before setup
        kokoro = Kokoro(*ensure_models())
        for i, (key, (line_id, _, spoken)) in enumerate(todo, 1):
            samples, rate = synthesize(kokoro, spoken, manifest["voice"], manifest["speed"])
            write_ogg(OUT_DIR / f"{key}.ogg", samples, rate)
            print(f"[{i}/{len(todo)}] {line_id:7} {key}  {len(samples) / rate:4.1f}s")

    if args.prune:
        for path in OUT_DIR.glob("*.ogg"):
            if path.stem not in clips:
                path.unlink()
                Path(str(path) + ".meta").unlink(missing_ok=True)
                print(f"Removed unused clip {path.name}")

    with INDEX_FILE.open("w", encoding="utf-8", newline="\n") as index:
        index.write("key\tid\ttext\n")
        for key, (line_id, text, _) in clips.items():
            index.write(f"{key}\t{line_id}\t{text}\n")

    print(f"Done: {len(todo)} generated, {len(clips)} clips in lines.json. Index: {INDEX_FILE.name}")


if __name__ == "__main__":
    main()
