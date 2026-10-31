"""Export approved project source/state without credentials, or install this skill.

Python 3.10+, standard library only. No Telegram calls and no service changes.
"""
from __future__ import annotations

import argparse
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import tempfile
import time
from datetime import datetime, timezone
from zipfile import ZIP_DEFLATED, ZipFile


ROOT_FILES = {
    "DotNetChannel.sln", "Dockerfile", "compose.yaml", ".gitignore", ".dockerignore",
    "README.md", "ADMIN.md", "OPERATIONS.md", "AGENT_HANDOFF.md", "AGENTS.md",
    "PORTABILITY.md", "settings.example.json", "start.ps1", "install-autostart.ps1",
}
SOURCE_DIRS = {"src", "tests", "prompts", "skills"}
SKIP_DIRS = {"bin", "obj", ".git", ".vs", ".idea", "__pycache__", "node_modules", "TestResults"}
SUFFIXES = {".cs", ".csproj", ".xaml", ".md", ".json", ".yaml", ".yml", ".txt", ".py", ".ps1"}
SECRET_PATTERNS = [
    re.compile(rb"\bsk-[A-Za-z0-9_-]{20,}\b"),
    re.compile(rb"\b[0-9]{6,12}:[A-Za-z0-9_-]{30,}\b"),
]


def known_secrets(config: dict) -> list[bytes]:
    values = [config.get("BotToken"), config.get("OpenAiKey"),
              os.environ.get("OPENAI_API_KEY"), os.environ.get("CHANNEL_BOT_TOKEN")]
    return [v.encode("utf-8") for v in values if isinstance(v, str) and len(v) >= 20]


def check_content(name: str, content: bytes, secrets: list[bytes]) -> None:
    if any(value in content for value in secrets) or any(pattern.search(content) for pattern in SECRET_PATTERNS):
        raise ValueError(f"Credential-like content detected in {name}; export refused (value not displayed).")


def source_files(root: Path):
    for name in sorted(ROOT_FILES):
        file = root / name
        if file.is_file():
            yield file
    for directory in sorted(SOURCE_DIRS):
        base = root / directory
        if not base.is_dir():
            continue
        for current, dirs, names in os.walk(base, followlinks=False):
            dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS and not (Path(current) / d).is_symlink())
            for name in sorted(names):
                file = Path(current) / name
                if file.is_symlink() or file.suffix.lower() not in SUFFIXES:
                    continue
                lower = name.lower()
                if lower == "settings.local.json" or lower.startswith(".env") or "secret" in lower or "credential" in lower:
                    continue
                yield file


def sqlite_snapshot(database: Path, output: Path) -> None:
    deadline = time.monotonic() + 20

    def progress(status, remaining, total):
        if time.monotonic() > deadline:
            raise TimeoutError("SQLite snapshot timed out; no archive was created.")

    with closing(sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=5)) as source:
        with closing(sqlite3.connect(output)) as destination:
            source.backup(destination, pages=128, progress=progress, sleep=0.1)
            if destination.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise ValueError("SQLite snapshot failed integrity_check.")


def export_project(project: Path, output: Path, include_state: bool) -> None:
    root = project.resolve()
    if not (root / "DotNetChannel.sln").is_file():
        raise ValueError("Project root must contain DotNetChannel.sln.")
    if output.exists():
        raise FileExistsError(f"Output already exists: {output}. Choose a new archive name.")
    local = root / "settings.local.json"
    config = json.loads(local.read_text(encoding="utf-8-sig")) if local.exists() else {}
    secrets = known_secrets(config)
    files: dict[str, bytes] = {}
    for file in source_files(root):
        name = file.relative_to(root).as_posix()
        content = file.read_bytes()
        check_content(name, content, secrets)
        files[name] = content
    template = {
        "SourceChannel": config.get("SourceChannel", "NetDeveloperDiary"),
        "BotToken": "", "AdminUserId": 0,
        "TargetChannel": config.get("TargetChannel", ""), "OpenAiKey": "",
        "Model": config.get("Model", ""),
        "PollSeconds": config.get("PollSeconds", 120),
        "MaxDraftsPerDay": config.get("MaxDraftsPerDay", 3),
        "AutoPublish": False, "DataDirectory": "data", "PromptFile": "prompts/rewrite.txt",
    }
    files["settings.transfer.json"] = (json.dumps(template, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    state_included = False
    with tempfile.TemporaryDirectory(prefix="dotnetchannel-transfer-") as scratch:
        if include_state:
            data_dir = Path(config.get("DataDirectory", "data"))
            if not data_dir.is_absolute():
                data_dir = root / data_dir
            database = data_dir / "channel.db"
            if not database.is_file():
                raise FileNotFoundError("--include-state requested, but channel.db is absent.")
            backup = Path(scratch) / "channel.db"
            sqlite_snapshot(database.resolve(), backup)
            content = backup.read_bytes()
            check_content("data/channel.db", content, secrets)
            files["data/channel.db"] = content
            state_included = True
        manifest = {
            "createdUtc": datetime.now(timezone.utc).isoformat(),
            "stateIncluded": state_included, "credentialsIncluded": False,
            "note": "Read PORTABILITY.md. Stop old worker before reusing the same bot. Configure credentials separately; AutoPublish is false in the transfer template.",
            "files": {name: hashlib.sha256(content).hexdigest() for name, content in sorted(files.items())},
        }
        files["TRANSFER-MANIFEST.json"] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        # Validate all generated text too, before writing anything to the output archive.
        for name, content in files.items():
            check_content(name, content, secrets)
        output.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(output, "x", compression=ZIP_DEFLATED) as archive:
            for name, content in sorted(files.items()):
                archive.writestr(name, content)
    print(json.dumps({"archive": str(output.resolve()), "files": len(files), "stateIncluded": state_included, "credentialsIncluded": False}, ensure_ascii=False))


def install_skill(source: Path, destination: Path | None) -> None:
    source = source.resolve()
    if not (source / "SKILL.md").is_file():
        raise ValueError("Skill source must contain SKILL.md.")
    if destination is None:
        codex_home = Path(os.environ["CODEX_HOME"]) if os.environ.get("CODEX_HOME") else Path.home() / ".codex"
        destination = codex_home / "skills" / "telegram-channel-studio"
    destination = destination.resolve()
    if destination == source:
        print("Skill is already at the requested location.")
        return
    files = {p.relative_to(source): p for p in source.rglob("*") if p.is_file() and not p.is_symlink() and "__pycache__" not in p.parts}
    if destination.exists():
        installed = {p.relative_to(destination): p for p in destination.rglob("*") if p.is_file() and "__pycache__" not in p.parts}
        if set(installed) == set(files) and all(installed[name].read_bytes() == file.read_bytes() for name, file in files.items()):
            print("An identical skill copy is already installed.")
            return
        raise FileExistsError("A different installed skill exists. Review/back it up instead of overwriting it automatically.")
    destination.mkdir(parents=True)
    for relative, file in files.items():
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)
    print(f"Skill installed: {destination}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    export = commands.add_parser("export", help="Export approved source and optional SQLite state, without credentials.")
    export.add_argument("--project", type=Path, required=True)
    export.add_argument("--output", type=Path, required=True)
    export.add_argument("--include-state", action="store_true")
    install = commands.add_parser("install-skill", help="Install the skill for local discovery; does not overwrite another version.")
    install.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[1])
    install.add_argument("--destination", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "export":
            export_project(args.project, args.output, args.include_state)
        else:
            install_skill(args.source, args.destination)
    except (OSError, ValueError, sqlite3.Error) as error:
        parser.exit(1, f"Error: {error}\n")


if __name__ == "__main__":
    main()
