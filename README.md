# Dayflower

**A Windows 11 calendar for all your Google accounts, with a Join button for every meeting.**

Dayflower shows several Google accounts in one calendar, puts a Join button on every video meeting (Google Meet,
Zoom, Teams and more) and shows desktop reminders that match the ones you set in Google Calendar. It's built with
WinUI 3 to look and feel like part of Windows 11.

> **Status:** early development. There's nothing to install yet. See the [plan](docs/PLAN.md) for what's coming.

## Building

You need Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). To install the
development build, also switch on Developer Mode (Settings › System › Advanced › For developers).

```powershell
.\run.ps1
```

builds the app, installs it for your user as a development package and starts it. `.\run.ps1 -NoLaunch` installs
without starting it.

```powershell
dotnet test --solution Dayflower.slnx
```

runs the tests.

Signing in to Google needs an OAuth client of your own in local builds: see
[Google Cloud setup](docs/google-cloud-setup.md).

## Project layout

| Folder | What's in it |
|---|---|
| `src/Dayflower.App` | The WinUI 3 app: windows, views, view models, tray, notifications |
| `src/Dayflower.Core` | Provider-neutral model and logic, with no UI or Google code |
| `src/Dayflower.Storage` | Local cache, search index and settings |
| `src/Dayflower.Providers.Google` | Google sign-in and the Google Calendar API |
| `tests/` | Unit tests |
| `tools/Dayflower.DevTool` | `dayflower-dev`: cache statistics, a one-off sync, and an audit of repeating events against Google |
| `tools/MakeAssets.ps1` | Draws the placeholder logo at every size the package needs |

For example, after signing in with the app:

```powershell
dotnet run --project tools/Dayflower.DevTool -- stats
dotnet run --project tools/Dayflower.DevTool -- audit --days 180
```

## Licence

Dayflower is free software under the GNU General Public License v3.0. See [LICENSE](LICENSE).
