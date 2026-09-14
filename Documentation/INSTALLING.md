# Installing

The tool comes in two parts, and only one of them is installed. **The launcher** is a small program that
fetches, keeps and starts **the application**. A new application release therefore needs no new
installer, and the installer almost never changes.

## What to download

Each release publishes three assets. Which one you want depends on who you are:

| Asset | For |
| --- | --- |
| `TestFramework.DebugUI.Setup.exe` | **A person installing the tool.** Start here. |
| `TestFramework.DebugUI.Launcher.zip` | Anyone who would rather not run an installer. Unpack and run the launcher. |
| `TestFramework.DebugUI.zip` | The launcher's own download. You do not need it by hand. |

### The SmartScreen warning is expected

The installer is **not signed**, so Windows shows *"Windows protected your PC"*. Choose **More info →
Run anyway**. There is no certificate behind this project, and a self-signed signature buys nothing -
SmartScreen treats it exactly like an unsigned file - so nothing in the installer can improve the
warning. MSIX was considered and rejected for the same reason: it refuses to install at all without a
trusted signature, with no equivalent door.

## What the installer does

- **Per-user, no administrator rights, no UAC prompt.** The same shape as the VS Code user installer or
  Discord.
- Installs the launcher to `%LOCALAPPDATA%\Programs\TestFramework`.
- Adds a Start-menu entry, and a desktop icon if you tick the box.
- **Offers the .NET 8 desktop runtime if it is missing.** That part is machine-wide and elevates itself.
  The application is framework-dependent on purpose: whoever runs it needs the runtime anyway, and
  shipping a copy inside every update would multiply the download for no gain.
- **x64**, or x64-compatible - so ARM64 under emulation works.

## Where things end up

Program and data are kept apart on purpose:

```
%LOCALAPPDATA%\Programs\TestFramework      the launcher. Installed, upgraded and removed by the installer.
%USERPROFILE%\.testframework\versions      application versions the launcher downloaded.
%USERPROFILE%\.testframework\Debug         run journals.
%USERPROFILE%\.testframework\DebugUI       settings, themes and the tool's log.
```

Uninstalling takes the program and the downloaded versions. It leaves your settings, custom themes and
recorded runs alone.

Two notes on those paths:

- They are under the profile root rather than `AppData` deliberately - see the remarks on
  `LauncherPaths` for the argument. It costs journals and version caches being included in a classic
  roaming profile.
- `%USERPROFILE%\.testframework\Debug` is **load-bearing**. Core decides whether to record a run by
  whether that folder exists, so creating it is the launcher's first job, ahead of any update check.
  Until something creates it, runs are not journalled and there is nothing to reopen later.

## Updating

The launcher checks the GitHub releases on start and fetches anything newer than what is on disk - there
is no prompt. It keeps the **last three** versions side by side, so backing out a bad
release does not need a reinstall:

- **Hold Shift while starting the launcher** to run the version before the newest.
- **`--version 0.4.1`** to name one, for anyone scripting it.

There is no picker - the window is too small for one, and this is the rare path. Either way the choice
outranks the feed, so the next start does not quietly move you forward again.

The governing rule is that **an update must never stand between you and your tool.** If the feed is
unreachable, the installed version starts. The only outcome that starts nothing is the one where there
is genuinely nothing to start: no version on disk and no way to fetch one.

A version that fails to start is remembered and moved to the back of the queue rather than dropped - the
launcher works down to the next one and says which build it skipped and why. It is still tried when
nothing better remains, because the cause may have been the machine (a runtime missing then, present
now) rather than the build, and surviving a start clears the mark. "Survived" means still running, not
merely having pumped a message loop.

The journal folder is created either way, because that has to happen on the starts where the update does
not.

## For maintainers: cutting a release

`.github/workflows/release-debugui.yml` runs on a version tag:

```bash
git tag v0.2.0 && git push origin v0.2.0
```

The tag is the source of truth - it sets the version stamped into the build and the version the launcher
compares against, so the tag is what decides whether anyone is offered an update.

Two things in there are contracts rather than conventions:

- **The asset name `TestFramework.DebugUI.zip`.** The launcher asks for it by name; renaming it silently
  stops every existing launcher from updating.
- **`TestFramework.DebugUI.exe` at the root of that archive.** The workflow fails the build rather than
  publish an archive the launcher could install but not start.

The workflow also cannot run until the `TestFramework.Core` version that `TestFramework.DebugUI.State`
references is on nuget.org - `dotnet restore` on the runner has no access to a local feed, and a release
built from a feed nobody else has is not a release.
