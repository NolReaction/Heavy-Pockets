#!/usr/bin/python3
"""Open this checkout in its matching Unity Editor for a local raid on macOS."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile


def prepare_manifest(project):
    manifest = project / "Packages/manifest.json"
    original = manifest.read_bytes()
    text = original.decode("utf-8")
    config = json.loads(text)
    package = "com.playeveryware.eos"
    if package not in config["dependencies"]:
        return
    del config["dependencies"][package]

    # Remove just the package member. Verify against the parsed manifest before writing.
    member = re.escape(json.dumps(package)) + r'\s*:\s*"(?:\\.|[^"\\])*"'
    patterns = [r"(?m)^[ \t]*" + member + r"[ \t]*,[ \t]*\r?\n",
                member + r"\s*,\s*", r",\s*" + member, member]
    for pattern in patterns:
        candidate, count = re.subn(pattern, "", text, count=1)
        if count:
            try:
                if json.loads(candidate) == config:
                    break
            except ValueError:
                pass
    else:
        raise ValueError(f"Remove {package} from the dependencies in {manifest} and run setup again.")

    backups = project / "UserSettings/MacPlay"
    backups.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=backups, prefix="manifest-before-", suffix=".json", delete=False) as backup:
        backup.write(original)
    print(f"Saved original manifest in {backup.name}", flush=True)

    with tempfile.NamedTemporaryFile(dir=manifest.parent, suffix=".tmp", delete=False) as output:
        temporary = Path(output.name)
        try:
            output.write(candidate.encode("utf-8"))
            output.flush()
            shutil.copymode(manifest, temporary)
            os.replace(temporary, manifest)
        finally:
            temporary.unlink(missing_ok=True)


def main():
    project = Path(__file__).resolve().parent
    version = next(line.split()[1] for line in (project / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()
                   if line.startswith("m_EditorVersion:"))
    app = Path("/Applications/Unity/Hub/Editor") / version / "Unity.app"
    editor = app / "Contents/MacOS/Unity"
    if not editor.is_file():
        sys.exit(f"Install Unity {version} in Unity Hub. Expected editor at {editor}")

    lock = project / "Temp/UnityLockfile"
    if lock.exists() and subprocess.run(["/usr/sbin/lsof", "-t", str(lock)], stdout=subprocess.DEVNULL,
                                         stderr=subprocess.DEVNULL).returncode == 0:
        print(f"Unity already has this project open: {project}")
        return
    prepare_manifest(project)
    print(f"Opening {project} in Unity {version}", flush=True)
    os.execv(editor, [str(editor), "-projectPath", str(project)])


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError) as error:
        sys.exit(str(error))
