"""Portable skill helper tests: python -m unittest discover -s tests -p test_skill_transfer.py"""
import hashlib
from contextlib import closing
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from zipfile import ZipFile


SCRIPT = Path(__file__).resolve().parents[1] / "skills/telegram-channel-studio/scripts/project_transfer.py"
spec = importlib.util.spec_from_file_location("project_transfer", SCRIPT)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


class TransferTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="channel-transfer-test-")
        self.root = Path(self.scratch.name) / "project"
        (self.root / "src").mkdir(parents=True)
        (self.root / "DotNetChannel.sln").write_text("Solution fixture", encoding="utf-8")
        (self.root / "src/Example.cs").write_text("// safe source", encoding="utf-8")
        self.token = "123456789:" + "a" * 35
        self.config = {"BotToken": self.token, "OpenAiKey": "sk-" + "b" * 40, "AdminUserId": 123,
                       "AutoPublish": True, "TargetChannel": "@example", "DataDirectory": "data"}
        (self.root / "settings.local.json").write_text(json.dumps(self.config), encoding="utf-8")
        self.output = Path(self.scratch.name) / "export.zip"

    def tearDown(self):
        self.scratch.cleanup()

    def test_source_export_has_no_credentials_or_implicit_auto_start(self):
        helper.export_project(self.root, self.output, False)
        with ZipFile(self.output) as archive:
            self.assertNotIn("settings.local.json", archive.namelist())
            template = json.loads(archive.read("settings.transfer.json"))
            self.assertEqual("", template["BotToken"])
            self.assertEqual(0, template["AdminUserId"])
            self.assertFalse(template["AutoPublish"])
            manifest = json.loads(archive.read("TRANSFER-MANIFEST.json"))
            for name, digest in manifest["files"].items():
                content = archive.read(name)
                self.assertNotIn(self.token.encode(), content)
                self.assertEqual(digest, hashlib.sha256(content).hexdigest())

    def test_sqlite_backup_preserves_committed_wal_records(self):
        (self.root / "data").mkdir()
        with closing(sqlite3.connect(self.root / "data/channel.db")) as connection:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("CREATE TABLE example(value TEXT)")
            connection.execute("INSERT INTO example VALUES ('committed in WAL')")
            connection.commit()
            helper.export_project(self.root, self.output, True)
        with ZipFile(self.output) as archive:
            backup = Path(self.scratch.name) / "backup.db"
            backup.write_bytes(archive.read("data/channel.db"))
            self.assertNotIn("data/channel.db-wal", archive.namelist())
        with closing(sqlite3.connect(backup)) as connection:
            self.assertEqual("committed in WAL", connection.execute("SELECT value FROM example").fetchone()[0])

    def test_refuses_a_secret_accidentally_committed_in_source(self):
        (self.root / "src/Leak.cs").write_text(self.token, encoding="utf-8")
        with self.assertRaises(ValueError):
            helper.export_project(self.root, self.output, False)
        self.assertFalse(self.output.exists())

    def test_skill_install_does_not_overwrite_another_version(self):
        source = self.root / "skill"
        source.mkdir()
        (source / "SKILL.md").write_text("skill fixture", encoding="utf-8")
        installed = Path(self.scratch.name) / "installed"
        helper.install_skill(source, installed)
        helper.install_skill(source, installed)
        (installed / "SKILL.md").write_text("local edits", encoding="utf-8")
        with self.assertRaises(FileExistsError):
            helper.install_skill(source, installed)
        self.assertEqual("local edits", (installed / "SKILL.md").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
