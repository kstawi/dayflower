# Dayflower

**A Windows 11 calendar for all your Google accounts, with a Join button for every meeting.**

Dayflower shows several Google accounts in one calendar, puts a Join button on every video meeting (Google Meet,
Zoom, Teams and more) and shows desktop reminders that match the ones you set in Google Calendar. It's built with
WinUI 3 to look and feel like part of Windows 11.

> **Status:** beta. See the [changelog](CHANGELOG.md) for what's in each version and the [plan](docs/PLAN.md) for
> what's coming.

## Installing

You need Windows 11.

1. From the [releases page](https://github.com/kstawi/dayflower/releases), download `Dayflower-win-x64-Setup.exe`,
   or `Dayflower-win-arm64-Setup.exe` if your PC has an ARM processor (Windows Settings › System › About › System
   type says "ARM-based processor").
2. Run it. It installs Dayflower for your Windows user only, without asking for admin rights, and opens it.

   The installer isn't code-signed yet, so Windows may say **"Windows protected your PC"**: choose **More info**,
   then **Run anyway**. Where Smart App Control is on, Windows doesn't let unsigned apps run at all, so Dayflower
   can't be installed there until it's signed.
3. Choose **Add account** and sign in to Google in your browser. Until Google has reviewed Dayflower, Google says
   **"Google hasn't verified this app"**: choose **Advanced**, then **Go to Dayflower**. A work or school account may
   need its admin to allow Dayflower first.

Dayflower starts with Windows and keeps running in the background so that reminders arrive; both can be turned off
in its Settings. It updates itself: a new version downloads in the background and is installed the next time
Dayflower starts, or straight away with **Restart to update** in Settings.

Your events go only between your PC and Google; see the [privacy policy](PRIVACY.md). To uninstall, use Windows
Settings › Apps › Installed apps; that also removes Dayflower's cached events, settings and saved sign-ins. To also
withdraw its access to your Google account, remove it at [Google account permissions](https://myaccount.google.com/permissions).

Something wrong? [Report a problem](https://github.com/kstawi/dayflower/issues/new/choose).

## Building

You need Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
.\run.ps1
```

builds Dayflower and starts it as a plain program, the same way the installer will ship it, with nothing to install
or switch on first. It keeps its cache, settings and sign-ins in `%AppData%\Dayflower`. `.\run.ps1 -NoLaunch`
builds without starting it. `.\run.ps1 -Packaged` builds the MSIX package instead and installs it for your user as a
development package, which needs Developer Mode (Settings › System › Advanced › For developers).

```powershell
dotnet test --solution Dayflower.slnx
```

runs the tests.

Signing in to Google needs an OAuth client of your own in local builds: see
[Google Cloud setup](docs/google-cloud-setup.md).

## Releasing

1. Add a section for the version to [CHANGELOG.md](CHANGELOG.md); it becomes the release notes.
2. Tag the commit and push the tag:

   ```powershell
   git tag v0.7.0
   git push origin v0.7.0
   ```

The [release workflow](.github/workflows/release.yml) runs the tests, builds the installer and update packages for
x64 and ARM64, and publishes them as a GitHub Release. Installed copies pick the update up by themselves. A tag like
`v0.7.0-beta.1` makes a pre-release, which installed copies ignore, for trying a build first. It needs the Google
sign-in secrets described in [Google Cloud setup](docs/google-cloud-setup.md#7-give-the-client-to-release-builds).

## Project layout

| Folder | What's in it |
|---|---|
| `src/Dayflower.App` | The WinUI 3 app: windows, views, view models, notifications |
| `src/Dayflower.Core` | Provider-neutral model and logic, with no UI or Google code |
| `src/Dayflower.Storage` | Local cache, search index and settings |
| `src/Dayflower.Providers.Google` | Google sign-in and the Google Calendar API |
| `tests/` | Unit tests |
| `tools/Dayflower.DevTool` | `dayflower-dev`: cache statistics, a one-off sync, the latest changes, and checks against Google (`audit` for repeating events, `compare` for everything the views show), of Join links (`meetings`), and of every kind of edit on throwaway events (`edit-test`) |
| `tools/Pack.ps1` | Builds the installer (`Setup.exe`) and update packages with [Velopack](https://velopack.io) into `releases/`; `-PackId DayflowerTest` makes a test install that keeps its own folder, data and start-up entry |
| `tools/MakeAssets.ps1` | Draws the app icon (a tear-off calendar pad in the layered style of Microsoft's own icons) at every size the package needs, plus a window icon for each day of the month with that date on the pad; `-Preview` renders a sheet of all sizes (`-PreviewDay 26` for a dated one) |

For example, after signing in with the app:

```powershell
dotnet run --project tools/Dayflower.DevTool -- stats
dotnet run --project tools/Dayflower.DevTool -- audit --days 180
```

## Licence

Dayflower is free software under the GNU General Public License v3.0. See [LICENSE](LICENSE).
