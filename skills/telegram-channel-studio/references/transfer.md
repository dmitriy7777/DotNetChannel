# Transfer to another machine or agent login

## Same project, same computer

Read the project-local `AGENTS.md` and `AGENT_HANDOFF.md`. The local skill is usable directly
even when the agent's account has no installed skills. A Codex login does not itself supply or
remove Windows user environment variables. Check actual availability without printing values.

Optional discovery installation:

```text
python <project>/skills/telegram-channel-studio/scripts/project_transfer.py install-skill
```

This copies the project-local skill into `CODEX_HOME/skills/telegram-channel-studio`, or
`~/.codex/skills/telegram-channel-studio` if CODEX_HOME is unset. An existing different skill is
not overwritten. A new chat/app reload may be needed to refresh skill discovery. Directly
reading the project-local SKILL.md always remains possible without installation.

## Another computer, continuing the same channel

Export with the helper using `export --project <project> --output <zip> --include-state`.
The SQLite backup API captures a consistent snapshot including committed WAL data, rather
than copying only a live `.db` file. Normal export does not stop or start the service.

The bundle contains source, skill, docs, an optional database, and `settings.transfer.json`.
It excludes actual credentials, binaries, logs, `.git`, old drafts, shortcuts and machine PIDs.
`settings.transfer.json` preserves non-secret editorial/source/destination settings but sets
AdminUserId=0, empty credentials and AutoPublish=false. It is not an immediately live deployment.

On the new Windows computer:

1. Extract to the chosen project folder; read `PORTABILITY.md` and `AGENT_HANDOFF.md`.
2. Install a compatible .NET SDK (the current solution targets .NET 10 with WPF on Windows).
3. Copy `settings.transfer.json` to `settings.local.json` only if there is no existing local
   config. Fill the existing bot's token and owner ID locally. Restore an authorized AI API key
   through environment/config. Do not create a different bot just because this is a new machine.
4. Stop the worker on the old host before starting the same bot on the new host. Do not run
   two long-poll consumers with the same token, even against separate copies of SQLite.
5. Restore/build/test. `--preview` reads the source without sending; `--check` checks Telegram
   without AI generation or publication. Keep the copied database for deduplication.
6. Register the launcher in a new scheduler task using the new absolute root and current Windows
   account. Recreate the admin shortcuts. Do not import old `.lnk` or scheduler paths.
7. Start deliberately in the selected publication mode. If any send was in flight at snapshot
   time, let recovery mark it uncertain and reconcile manually before a retry.

The old computer can remain a development checkout, but only one host should own the bot's
active worker. Verify the new setup before decommissioning any old data or tasks.

## A new similar channel

Use a source-only export (omit `--include-state`). Start with a fresh data directory and new
bot, destination and owner configuration. Adapt task name, bot link and instance paths in code
where they are currently hard-coded. Existing public URLs are examples, not authorization to
write to that channel. Do not import publication metadata from an unrelated channel.

## Handling secrets

Move credentials separately through a channel the user selects. A portable source archive is
not a credential backup. The helper refuses to export detected credential values/patterns;
still inspect the manifest and remember that arbitrary newly added files require review.
Never make the archive public without checking project/user-specific content. Database state
contains operational history and source text, even though credentials are excluded.
