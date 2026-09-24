"""One-nudge-per-session throttle in front of `graphify hook-guard <kind>`.

graphify's own PreToolUse guard (site-packages/graphify/__main__.py,
_run_hook_guard) prints its "MANDATORY... use graphify" nudge on every single
matching Bash/Read/Glob call for the life of the graph — useful the first
time in a session, pure repeated noise after that (there's no built-in
throttle or CLI flag for it). This wrapper sits in front of it in
.claude/settings.json: it reads the same PreToolUse payload graphify's guard
gets, keys a marker file in the system temp dir off the payload's
`session_id`, and only forwards to the real hook-guard (and lets its nudge
through) the first time THIS session actually produces one. Every other call
that session — whether or not it would have nudged — stays silent.

Delegates the actual "does this call even qualify" decision (grep-like
command / source-file-extension matching) to graphify itself by piping the
original stdin through unchanged, so this stays correct if graphify changes
its own matching rules on a future upgrade. Fails open throughout: any
error here just falls through to letting the real guard run normally, since
a missed throttle is harmless but a swallowed tool call is not.
"""
import json
import os
import subprocess
import sys
import tempfile

GRAPHIFY_EXE = r"C:\Users\My PC\AppData\Roaming\Python\Python313\Scripts\graphify.exe"


def main() -> None:
    if len(sys.argv) < 2:
        return
    kind = sys.argv[1]

    raw = sys.stdin.buffer.read()

    session_id = "unknown"
    try:
        payload = json.loads(raw.decode("utf-8", "replace"))
        if isinstance(payload, dict) and payload.get("session_id"):
            session_id = str(payload["session_id"])
    except Exception:
        pass

    marker_path = None
    try:
        marker_dir = os.path.join(tempfile.gettempdir(), "graphify-session-nudge")
        os.makedirs(marker_dir, exist_ok=True)
        marker_path = os.path.join(marker_dir, session_id.replace(os.sep, "_") + ".nudged")
        if os.path.exists(marker_path):
            return  # already nudged once this session — stay quiet
    except OSError:
        pass  # can't track sessions on this machine — just fall through

    try:
        result = subprocess.run([GRAPHIFY_EXE, "hook-guard", kind], input=raw, capture_output=True)
    except Exception:
        return  # never block the tool call over a nudge failure

    if result.stdout:
        # Only claim the marker once a nudge actually fired — a non-matching
        # call (e.g. reading a .json file) shouldn't burn the session's one
        # nudge on nothing.
        if marker_path is not None:
            try:
                with open(marker_path, "w", encoding="utf-8"):
                    pass
            except OSError:
                pass
        sys.stdout.buffer.write(result.stdout)


if __name__ == "__main__":
    main()
