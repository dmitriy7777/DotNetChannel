# Windows service and desktop admin

The current worker is a console application supervised by a separate WinExe launcher. The
launcher creates no console window, redirects stdout/stderr and remains alive with the child.
Windows Task Scheduler starts the launcher after user sign-in, not before login. Local sleep,
shutdown and missing connectivity interrupt operation; a local task is not a 24/7 remote server.

## Lifecycle

Keep one worker per bot/data directory and retain the process lock. Start through the registered
task so an admin window does not create an independent second service.

For stop, verify the actual worker executable path, then atomically write its PID to
`data/stop.request`. The runtime monitor cancels the linked token, and the worker exits with
success so the scheduler does not restart an intentional stop. Preserve uncertain publication
states across interruption. If stopping takes too long, display the condition; do not silently
force kill a potentially publishing process.

Write runtime heartbeat atomically and tolerate sharing/IO races while a reader or writer holds
a file. A monitor failure must not leave the app processing indefinitely with a dead monitor.
Regression tests cover a temporarily exclusively locked stop file. Guard stale PID files with
process identity; a recycled PID alone is not sufficient authorization to kill a process.

The existing admin reads the scheduler through its COM API and the database using read-only
SQLite transactions. Closing the WPF window leaves the worker running. F5 refreshes, F6 starts,
F7 stops, and F8 restarts. Do not expose raw secrets through settings, errors, process arguments
or log viewers. Preserve manual stopped state when doing unrelated documentation/export work.

## Machine-specific details

- WPF is Windows-only. The current target is `net10.0-windows`; install a compatible SDK/runtime
  on the target machine and recheck current support if upgrading.
- `Europe/Kyiv` was not recognized by the tested Windows installation. The UI uses Windows
  `FLE Standard Time` for Kyiv timestamps. Daily AI budgets intentionally use UTC calendar days.
- Authenticated API settings may be present in the user environment but absent in an older
  Task Scheduler/explorer process. The launcher forwards the user-level `OPENAI_API_KEY` if its
  inherited process environment is empty. Never print the value while diagnosing this.
- Script execution was blocked by signing/RemoteSigned handling on this machine. Use the native
  .NET launcher; do not weaken Windows execution/security policy to make startup work.
- Recreate scheduler actions and `.lnk` shortcuts for the new root. Never assume `C:\Projects`
  or the original Windows user exists. The current launcher and admin discover their root by
  walking upwards to `settings.example.json`; keep those executables within the source tree
  or deliberately update deployment/root discovery.
- The current task name and bot link are project-specific constants in places. Before cloning
  a second channel, search those literals and isolate the instance instead of sharing the task.

## Validation

Run the actual app and check service state against the process and task. Verify stop/start,
restart, correct button enablement, queue persistence and closing the window without shutdown.
Test that the heartbeat stays fresh beyond one poll; a process being present is insufficient.

Native screenshot capture failed globally in the original tool session, including Telegram and
the WPF app. AX-only state plus documented keyboard navigation still worked. Use the currently
available computer-use tooling and its documented recovery; do not copy stale window handles.
The admin's `--snapshot` diagnostic option renders its own WPF content to `data/admin-preview.png`
after loading; this is a renderer-based layout check, not a desktop screenshot.

Rendering only a transparent child Grid initially lost Window.Background in the PNG; give the
root visual its own background. Check card captions and resized layouts rather than relying
only on successful XAML compilation. Generated screenshots belong with local diagnostics,
not with credentials or the portable skill.
