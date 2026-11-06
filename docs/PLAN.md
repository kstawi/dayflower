# Dayflower: plan

A Windows 11 calendar app that shows several Google accounts in one calendar, puts a **Join** button on every
video meeting, and raises desktop reminders that match the reminders set in Google Calendar. It's built to look
like part of Windows 11, starts with Google, and leaves room for Microsoft 365, iCloud, CalDAV and ICS later.

Status: agreed in the planning interview on 2026-11-06. Phases 0–6 (setup, accounts and sync, calendar views,
Join, reminders and start-up, editing, search and settings) are done; phase 4 was the daily-driver
milestone. Phase 7 (public release) is next.

## 1. Decisions

| Topic | Decision |
|---|---|
| Stack | WinUI 3 (Windows App SDK) + C# on .NET 10 LTS. Windows-only. |
| Accounts | Several Google accounts at once: personal Gmail and a Workspace account run by someone else's admin. |
| Audience | Public release: an installer on GitHub, plus the public GitHub repo. |
| Background | Runs in the background without a tray icon: closing the window hides it, and it keeps syncing and reminding. Reopened from Start; Quit in Settings. Starts with Windows (switchable in Task Manager). |
| Join links | Google Meet from the event's conference data, plus Zoom, Teams, Webex, Slack huddles, Whereby and similar found in the location or description. Opens in the default browser. |
| Join placement | Event details flyout (always), on the event in the day/week grid (from shortly before start until the end), and on reminder notifications. |
| Meet account | Join opens Meet as the Google account the event belongs to. |
| Reminder extras | Snooze, and a "starting now" alert with Join for meetings. |
| Duplicates | A meeting that's on two of your accounts is shown once, marked with both. |
| Views | Day, week, month, agenda list. |
| Editing | Create, edit, delete, RSVP with a note, guests, Meet link, reminders, colours, recurring events (this / following / all), drag & drop, guest availability, Workspace event types (Out of office, Focus time, Working location). |
| Search | Across all accounts. |
| Invitations | A toast when a new invite arrives, with Accept / Maybe / Decline. |
| Time zones | A world clock in the side pane with the zones you pick and your own labels. |
| Offline | View offline: cached events, Join buttons and reminders work without internet; changes need a connection. |
| Layout | Collapsible side pane (mini month, accounts and calendars, world clock) + main grid with a view switcher. |
| Look | Fluent / WinUI controls, Mica, follows light/dark mode and the accent colour, takes week start and 12/24 h from Windows regional settings. |
| Language | English, with all text in resource files so translations can be added later. |
| Later providers | Microsoft 365 / Outlook, iCloud, other CalDAV, ICS subscriptions. |
| Install | A `Setup.exe` from GitHub Releases (Velopack): installs per user without admin rights and updates itself. winget once the installer is signed. The Microsoft Store maybe later (decided 2026-11-06; the earlier plan was the Store first). |
| Name | Dayflower: a flower whose blooms last a single day. Chosen for now; a full trademark check comes before the public release. |
| Repo | Public, github.com/kstawi/dayflower, set up like brightness-mirror (commits as kstawi, signed with the kstawi SSH key). |
| Licence | GPL-3.0, as brightness-mirror. |
| Dev tools | .NET 10 SDK only; no Visual Studio. Build, run and test from the command line. |

## 2. What the app does

### 2.1 Accounts and sign-in

- "Add account" opens Google's sign-in page in the default browser (OAuth for desktop apps: loopback redirect + PKCE).
  Any number of accounts; each can be removed or signed in again independently.
- Tokens are stored per account, encrypted with Windows DPAPI for the current user. Nothing is sent anywhere except
  Google.
- If an account's access is revoked or expires, that account shows a "Sign in again" bar; the other accounts keep
  working.

### 2.2 Combined calendar

- Side pane lists each account with its calendars (own, shared, subscribed, Birthdays), each with a checkbox and
  Google's colour for it. Hidden calendars stay hidden across restarts.
- All visible calendars are drawn in one grid. Event colour = the event's own colour if set, otherwise its calendar's.
- **Merging duplicates:** copies of the same meeting on different accounts (same iCalendar UID, and same original
  start for recurring instances) appear as one event. The details flyout shows each account's copy with its own RSVP
  status, so you can respond or join per account.
- Event styling follows Google's conventions: invitations you haven't answered are outlined, tentative ones are
  striped, declined ones are hidden unless "Show declined events" is on.

### 2.3 Views

- **Day** and **week**: hour grid with an all-day row, a red "now" line, overlap layout for clashing events, working
  hours shaded. Scrolls to the current time on open.
- **Month**: classic grid; days with more events than fit show "+3 more".
- **Agenda**: scrolling list of upcoming events grouped by day.
- Top bar: New event, Today, previous/next, the date range (click for a date picker), view switcher, search box.
- Keyboard: Ctrl+N new event, T today, D / W / M / A switch view, arrows and Page Up/Down to move, Ctrl+F search.

### 2.4 Event management

- **Quick create:** click or drag on the grid, type a title, pick the calendar, Enter. "More options" opens the full
  editor.
- **Full editor:** title; account and calendar; all-day; start/end with optional per-event time zone; recurrence
  (daily, weekly on chosen days, monthly, yearly, custom, with an end); location; "Add Google Meet"; guests (with
  "guests can modify / invite others / see the guest list"); description; reminders; colour; busy/free; visibility.
- **Guests:** typed email addresses, autocompleted from people already in your cached events (no extra Google
  permission needed). **Guest availability** shows each guest's busy times for the chosen day (works for colleagues
  in the same Workspace and anyone who shares free/busy).
- **Drag & drop:** move an event, or drag its bottom edge to change the length, with 15-minute snapping.
- **Recurring events:** any change asks "This event / This and following events / All events".
- **Guests notified:** changes to events with guests ask whether to send updates, as Google Calendar does.
- **RSVP:** Yes / Maybe / No with an optional note; for a series, "this event" or "all events".
- **Delete:** for your own events, delete (asking whether to notify guests); for invitations, decline.
- **Workspace event types** (Workspace accounts, primary calendar): Out of office (with auto-decline and a message),
  Focus time (with auto-decline), Working location (home, office or a custom place). Shown in their Google style for
  all accounts; the create options are hidden for Gmail accounts.
- **Search:** instant search of cached events across all accounts by title, description, location and guests (works
  offline). The cache holds every event Google has (sync has no date range), so a separate "Search Google" isn't
  needed.

### 2.5 Join button (the headline feature)

- **Finding the link**, in this order: the event's Google conference data (Meet, and Zoom/Teams when added through
  their Google add-ons), then the location field, then links in the description. Only links on a list of known
  meeting providers count: Google Meet, Zoom, Microsoft Teams (work and personal), Webex, Slack huddles, Whereby,
  GoTo Meeting, Jitsi, Amazon Chime, and more as needed. Other links are ignored.
- **Where it shows:**
  - Event details flyout: always, for any meeting, with the provider's name ("Join Google Meet").
  - On the event in the day/week grid: an accent button from 10 minutes before the start until the meeting ends
    (the lead time is a setting). Small events show a compact camera button.
  - Reminder and "starting now" notifications.
- **Opening:** the link opens in the default browser. Zoom and Teams then offer their desktop apps as usual.
- **Meet as the right account:** Meet links get Google's account selector for the account the event belongs to, so a
  work meeting opens as the work account. For a merged event the button uses the account that accepted the invite
  (or the first in your account order), and the flyout offers "Join as …" for the others.

### 2.6 Notifications

- **Reminders match Google:** each event's own "notification" reminders, or its calendar's default notifications when
  the event uses defaults. Google's email reminders are left to Google. No reminders for declined events.
- **Toast:** title, time, location; buttons **Join** (meetings), **Snooze** with a duration choice (1, 5, 10, 15
  minutes, or until it starts) and **Dismiss**; clicking the toast opens the event. Reminders stay on screen until
  dealt with (Windows' reminder style); a setting turns that off.
- **"Starting now" alert:** for meetings, a second toast with Join at the start time (skipped when the reminder is
  already at the start). On by default, switchable.
- **Merged events** remind once, using the combined reminder times.
- **Missed reminders:** if the PC was asleep or the app wasn't running, a reminder that was due is shown on wake or
  start-up if the event hasn't ended yet.
- **Invitation toasts:** when a sync finds a new invitation you haven't answered, a toast with **Accept / Maybe /
  Decline**. Not raised for the existing invitations found when an account is first added.
- Windows' Do Not Disturb and Focus settings apply as with any app.

### 2.7 World clock

- A "Time zones" section in the side pane, under the mini month: one row per zone with your label, the current time
  and the difference from your time, with "+1" or "âˆ’1" when it's a different day (for example "Team NY 09:14 âˆ’6h").
- Zones are added from a searchable city / time-zone list, can be renamed and reordered, and follow daylight saving
  automatically.

### 2.8 Background

- Closing the window hides it; Dayflower keeps running in the background so reminders arrive. Opening it again
  from Start (or a notification) brings the window back, and Quit in Settings ends it. There is no tray icon (it was
  dropped after the first UI review as not needed).
- Starts with Windows (Windows' startup-app switch, also in Task Manager).

### 2.9 Offline and sync

- Events are cached in a local SQLite database, so the app opens instantly and works offline: calendar, search, Join
  buttons and reminders. While offline, editing is disabled and a bar says so.
- Sync runs at start, every 2 minutes, when the window is brought forward, on wake from sleep and when the network
  comes back, and right after every change you make. It uses Google's incremental sync, so each check only downloads
  what changed. (Google's push notifications need a public web server, so a desktop app polls instead.)

### 2.10 Design

- WinUI 3 controls throughout: Mica backdrop, the WinUI title bar with the search box in it, Segoe UI Variable,
  Fluent icons, flyouts for event details, dialogs for confirmations, info bars for sync and account problems.
- Settings page in the style of Windows Settings (setting cards): accounts, calendars, default calendar for new
  events, notifications, Join lead time, world clock, theme (system / light / dark), start with Windows.
- Accessible: full keyboard use, screen-reader names, high-contrast themes.
- No telemetry. Logs stay on the PC.

## 3. Architecture

### 3.1 Solution layout

```
dayflower/
  src/
    Dayflower.App/               WinUI 3 app: windows, views, view models, notifications, startup
    Dayflower.Core/              Provider-neutral model and logic: events, merging, meeting-link detection,
                                reminder planning, recurrence, time zones. No UI or Google code.
    Dayflower.Storage/           SQLite cache, search index, settings
    Dayflower.Providers.Google/  Google sign-in, Calendar API client, mapping to the Core model
  tests/
    Dayflower.Core.Tests/
    Dayflower.Storage.Tests/
    Dayflower.Providers.Google.Tests/   mapping tests against recorded API responses
    Dayflower.App.UITests/              a few UI automation smoke tests (FlaUI)
  tools/
    Dayflower.DevTool/                  dayflower-dev: cache statistics, one-off sync, recurrence audit
  docs/
  .github/workflows/
```

### 3.2 Main libraries

| Need | Choice |
|---|---|
| UI | Windows App SDK (WinUI 3), CommunityToolkit.WinUI (setting cards, segmented control) |
| MVVM | CommunityToolkit.Mvvm; services are composed by hand in `AppServices` (no DI container needed yet) |
| Google | The Calendar REST API called directly with HttpClient and System.Text.Json, plus Dayflower's own OAuth flow (loopback + PKCE). Chosen over Google's .NET client libraries in phase 1: lighter, and the tests feed Google-shaped JSON into the same code the app runs. |
| Cache and search | Microsoft.Data.Sqlite with SQLite FTS5 |
| Recurrence | Ical.Net (RRULE expansion) |
| Saved sign-ins | Windows DPAPI (System.Security.Cryptography.ProtectedData) |
| Notifications | Windows App SDK app notifications (toasts with buttons and a snooze picker) |
| Logging | Serilog to a rolling file in the app's local folder |
| Tests | xUnit; FlaUI for UI smoke tests |

The day/week/month grid is a custom control. There is no free WinUI scheduler control, and commercial ones don't
fit a public open-source project. The views build their elements in code; the layout maths (side-by-side clashing
events, lanes for multi-day items, local-time spans, merging copies of a meeting) lives in Core with unit tests.
Brushes for code-built elements are looked up for the element's own light or dark theme, so the views follow a
theme change while the app is open.

### 3.3 Provider abstraction

Core defines an `ICalendarProvider` (accounts, calendar list, incremental sync, create/update/delete with recurrence
scope, RSVP, free/busy) plus capability flags (supports Meet creation, Workspace event types, free/busy…). The UI
reads the flags, so Microsoft 365, CalDAV/iCloud and read-only ICS feeds slot in later without UI rewrites. Every
event in the cache keeps its source (provider, account, calendar, remote ID), which is also what merging and per-account
Join rely on.

### 3.4 Sync engine

- Per account: calendar list and colour palette. Per calendar: events, with Google's sync token for incremental
  updates; a full resync when Google says the token has expired.
- Back-off and retry on rate limits and network errors; one failing calendar or account doesn't block the others.
- **Repeating events (decided in phase 1):** cached as series plus their changed and cancelled occurrences
  (Google's `singleEvents=false`), and expanded on the PC in the event's own time zone. This works for any date
  range and keeps sync small. Checked against Google's own expansion on a real Workspace calendar with the dev
  tool's recurrence audit: 373 of 373 occurrences matched from March 2026 to April 2027, across three clock changes
  and 291 changed or cancelled occurrences.
- Events are cached as Google's JSON plus a few indexed columns (time range, series, iCalendar UID); the model is
  rebuilt from the JSON when read, so mapping improvements apply without a resync.
- Some calendars (certain Google holiday calendars) expire their sync token on every request; those are fully
  refreshed at most every 6 hours instead of every 2 minutes.

### 3.5 Reminder scheduler

- After each sync, plan the reminder and "starting now" times for the next 48 hours from the cache, and keep one
  timer for the next one. Re-plan on sync changes, wake from sleep, clock or time-zone changes.
- Toasts are tagged per event, so an updated reminder replaces the old toast instead of stacking.
- Pure logic (which reminders, at what times, after merging, across daylight-saving changes) lives in Core and is
  unit-tested.

## 4. Google Cloud and Workspace setup

Done once, in the Google Cloud console under your personal Google account (I'll guide this step by step):

1. Create a project "Dayflower" and enable the Google Calendar API.
2. OAuth consent screen: type External, app name, support email, and the calendar scopes. Settled in phase 1 as
   the narrowest set that covers every planned feature: `calendar.calendarlist.readonly` (calendar list),
   `calendar.events` (read and edit events, RSVP, Meet links) and `calendar.events.freebusy` (guest availability,
   optional at sign-in), plus `openid`, `email` and `profile`.
3. Create an OAuth client of type **Desktop app**.
4. **Publish the app to "In production" straight away**, even unverified. In "Testing" status Google expires sign-ins
   after 7 days. Unverified, each person clicks through an "unverified app" warning once, and there's a 100-user cap
   until Google verifies the app.
5. The client ID and secret stay out of the public repo: a git-ignored local config for development, and GitHub
   Actions secrets for release builds. (Google doesn't treat a desktop app's secret as confidential, but publishing
   it invites misuse of the app's quota.)

**Workspace account run by someone else:** Workspace admins can block third-party apps that aren't verified or on
their trusted list. Phase 1 tests sign-in with that account early. If it's blocked, the fix is for that admin to
mark the app's client ID as trusted in the Admin console (Security › API controls).

**Verification for the public release (phase 7):** Calendar scopes are sensitive, so Google reviews the app before
lifting the 100-user cap and the "unverified app" warning. It wants a homepage and privacy policy on a domain you own,
verified in Google Search Console by a project owner (a `github.io` address most likely won't count, so a cheap domain
of its own, hosted by GitHub Pages), an unlisted YouTube video showing sign-in and how each scope is used, and a reason
for each scope. Google quotes 3–5 business days, longer if it comes back with questions, so it starts first.

## 5. Phases

Each phase ends with something you can run and check. Phases 1–4 build a "daily driver" (see, join and get reminded
of everything) before the larger editing work; until phase 5 you'd still make changes in Google Calendar on the web.

### Phase 0: Setup (S)
- Install the .NET 10 SDK with winget. You switch on Windows **Developer Mode** (Settings › System › Advanced) so the
  app can be installed as a local package; it's off on this PC, and it's a system setting I won't change.
- Create the solution, the projects above, a git repo set up like brightness-mirror, the GPL-3.0 licence, a README,
  and a GitHub Actions build that compiles and runs the tests. The public GitHub repo is created only when you say so.
- Google Cloud project and OAuth client (section 4).
- **Done when:** an empty Mica window with the title bar and side pane builds, installs locally and launches; CI is
  green.

### Phase 1: Accounts and sync (M)
- Multi-account sign-in, encrypted token storage, account list.
- Calendar list, colours, the SQLite cache and the incremental sync engine; the recurrence spike decides the cache
  model.
- Test sign-in with the Workspace account managed by someone else.
- **Done when:** all your accounts are signed in, every event is in the local cache, and changes made on the web
  arrive within one sync interval. Restarting the app doesn't ask you to sign in again.

### Phase 2: Calendar views (L)
- Shell: side pane with mini month and calendar checkboxes, top bar, view switcher, theme and regional settings.
- Week, day, month and agenda views; event details flyout (read-only); merged duplicates; declined/tentative styling.
- **Done when:** the four views show the same events as Google Calendar for all accounts, look at home next to
  Windows Settings in light and dark mode, and stay smooth when scrolling through months.
- **Result:** the dev tool's `compare` matched 374 of 374 events against Google's own expanded list (March 2026 to
  January 2027, three calendars). Merging copies across accounts is covered by tests; it gets a real check once a
  second account with shared meetings is added.

### Phase 3: Join (M)
- Meeting-link detection with a unit-test suite built from real-world invites (Meet, Zoom, Teams, Webex…).
- Join in the details flyout and on grid events; Meet opens as the right account; "Join as …" for merged events.
- **Done when:** every meeting in your calendars shows Join in the flyout, the grid button appears 10 minutes before,
  and Meet opens as the correct account in your default browser.
- **Result:** detection is in Core (`MeetingLinkDetector`) with tests for Meet, Zoom (links and the add-on), Teams
  (Outlook invites, new and personal links), Webex, Slack huddles, Whereby, GoTo, Jitsi and Chime, Outlook Safe
  Links and Google redirect wrappers, and links that aren't meetings. On the real work calendar the dev tool's
  `meetings` command found a Join link on all 325 meetings from March 2026 to January 2027 and no misses (the
  other 49 events were holidays and one test event). Meet links get `authuser=<account email>`. The grid shows
  "Join" (or a camera button on short events) from 10 minutes before the start (`joinLeadMinutes` in
  settings.json) until the end.

### Phase 4: Reminders, tray and start-up (M)
- Reminder scheduler, toasts with Join / Snooze / Dismiss, "starting now" alert, missed-reminder handling.
- Tray icon and menu, hide-to-tray, start with Windows.
- **Done when:** reminders fire at the same times Google Calendar's would, with the window closed, after sleep and
  across a restart. **This is the daily-driver milestone.**
- **Result:** reminder rules are in Core (`ReminderPlanner`, unit-tested); the app checks every 10 seconds, so a
  reminder missed during sleep or before start-up shows once if its event hasn't ended, and `reminders.json`
  remembers what was shown and snoozed. Notifications use Windows App SDK app notifications (reminder scenario,
  Join / Snooze with a choice / Dismiss), activated through a COM activator declared in the manifest. The tray
  icon uses H.NotifyIcon.WinUI; Program.cs keeps one instance per user; the manifest's startup task is on by
  default and starts Dayflower hidden. Checked with a real event: the reminder, two snoozes, the starting-now alert
  and Join from the notification all worked with the window closed, and a missed reminder appeared at start-up
  and not again after a restart.
- **Limits:** Google doesn't expose its default reminders for all-day events through the API, so all-day events
  only remind with their own reminders. Calendars hidden in Dayflower don't remind.
- **Note:** a manifest change (new extensions) needs a higher package version to re-register the development
  build without losing its data; the version is now 0.4.0.

### Phase 5: Editing (L)
- Quick create, full editor, delete, RSVP with note, guests with autocomplete and availability, Meet link,
  reminders, colours, recurrence and the this / following / all choices, "notify guests?", drag & drop,
  Workspace event types. Offline: editing disabled with a clear bar.
- **Done when:** everything you'd normally do in Google Calendar on the web can be done in the app, and the result
  looks right in Google Calendar.
- **Result:** the edit model is in Core (`EventEdit`, `RecurrenceRule`, `SeriesSplit`, `SeriesShift`, permission
  rules); the Google provider sends only what changed (PATCH), addresses an occurrence by its instance ID, splits a
  series for "this and following" as Google does, answers invitations by sending the guest list back with only the
  user's answer changed, and asks free/busy for availability. What Google sends back goes straight into the cache
  (`ApplyLocalChangesAsync`, sync token untouched) and a sync follows. In the app: Edit, Delete (Decline for someone
  else's invitation) and Going? Yes / Maybe / No with a note in the details flyout; click or drag over empty time
  for a quick-create flyout, or New event for the full editor (a dialog with every field above, plus out of office,
  focus time and working location on Workspace main calendars); drag events to move them and drag their bottom
  edge to resize, in 15-minute steps. Editing is unavailable offline (buttons disabled, a message otherwise).
  `dayflower-dev edit-test` runs every change against Google on throwaway events (created a month ahead, without
  guests or notifications, deleted afterwards): all 23 checks passed. The app's own create, edit, "this event" and
  delete flows were also run on test events, and the cache matched Google afterwards.
- **Learned:** moving "all events" of a series makes Google reset occurrences that were changed one by one, as in
  Google Calendar itself. Background reloads wait while a flyout is open, so a sync can't pull an event out from
  under it.

### Phase 6: Search, invitations, world clock, settings (M)
- Search (cached + "Search Google"), invitation toasts with Accept / Maybe / Decline, world clock in the side pane,
  the full Settings page, keyboard shortcuts, accessibility pass.
- **Done when:** these work end to end, and a keyboard-only and screen-reader pass finds no blockers.
- **Result:** search runs over the whole cache (Core `EventSearchIndex`: every word must match the title, location,
  description, guests or organiser, ignoring case and accents, so "swieta" finds "ÅšwiÄ™ta"); a repeating event shows
  once, at its next occurrence. Typing lists the first matches under the search box; Enter shows all of them,
  upcoming first, then past ones. "Search Google" was dropped: the cache already has everything Google has.
  Invitation notifications (`InvitationWatch` in Core, `InvitationWatcher` in the app) run after each sync on the
  user's own visible main calendars, once per meeting, with Accept / Maybe / Decline (a series is answered for all
  occurrences); invitations already there when an account is first synced are remembered, not announced. The
  world clock sits under the mini month, with a city search (about 140 cities, plus every Windows time zone),
  rename, reorder and remove. The Settings page (setting cards, as in Windows Settings) has the default calendar,
  working hours, show declined events, the notification switches, the Join lead time, accounts, theme (applied at
  once), start with Windows, the shortcuts and About; the tray menu gained New event and Settings. Shortcuts:
  Ctrl+N, Ctrl+F, Ctrl+, F5, Esc, and T, D / W / M / A, Page Up / Down and â† / â†’ when not typing. Accessibility:
  every keyboard-reachable control in the four views and Settings has a name (checked with UI Automation),
  events are read with their day, time and answer, headings and landmarks are marked, the sync status is a live
  region, and contrast themes draw events in system colours.
- **Also fixed:** the Going? buttons now only answer on the user's own main calendars (on a colleague's shared
  calendar Google marks the colleague as "self").
- **Not checked live:** an invitation notification needs a new invitation to arrive (the first run correctly
  stayed quiet about the one already waiting), and contrast themes need Windows' contrast setting switched on.
- **After the first UI reviews:** a new icon: a tear-off calendar pad in the layered style of Microsoft's own icons
  (layers casting soft shadows, navy-to-sky gradients, a curling corner), with a grid of days in the package icon and
  today's date in large type on the running window's icon, as on a Mac (generated by `tools/MakeAssets.ps1`); no icon or name in the title bar; a wider
  search box centred on the whole window; caption buttons as tall as the title bar; Settings pinned to the bottom of
  the side pane; the side pane animating together with the calendar, whose corner squares off when the pane is
  closed; and no tray icon (Quit is in Settings). The dated window icon (31 pre-drawn, switched at midnight) shows in
  Alt+Tab and Task View; Windows draws the taskbar, Start and notification icons from the package, so those can't
  change, and a date on them would be wrong most days. (Giving the window a taskbar identity of its own, as browsers do
  for web apps, was tried: for a packaged app the button still shows the package logo. A corner badge would work but
  reads as an unread count.)
### Phase 7: Public release (M)
Anyone can download a `Setup.exe` from GitHub, install Dayflower without admin rights and get updates on their own,
and you install it the same way. Steps 1 and 6 need only you and can start straight away; the rest is code and CI.

1. **Google verification (you, with texts from me).** Buy a domain; I build a small homepage and put the privacy
   policy on it, published by GitHub Pages from the repo. You verify the domain in Search Console, record the demo
   video from a script I write, and submit with the scope reasons I draft.
2. **Unpackaged build, checked first.** Dayflower built as a self-contained app (the .NET runtime and Windows App SDK
   included, nothing to install first), then a check of everything that behaves differently without a package:
   - notifications show Dayflower's name and icon, and their buttons (Join, Snooze, Accept / Maybe / Decline) reach
     the running app;
   - a second launch still hands over to the running copy;
   - start with Windows: a Run entry for the current user instead of the package's startup task, starting hidden
     with `--background`; Settings reads Windows's own Startup apps switch to show when it's turned off there;
   - the version comes from the program instead of the package, and assets load from the app's folder;
   - the cache and saved sign-ins live outside the folder the installer replaces on each update;
   - whether the taskbar can now show today's date (it couldn't for a package; a pinned button may still keep the
     shortcut's icon).

   `run.ps1` moves to the unpackaged build, so day-to-day testing matches what ships and Developer Mode is no longer
   needed. The MSIX build stays buildable for a possible Store listing.

   **Built and checked 2026-11-06** on this PC (Windows 11 build 26300):
   - Works: the window, a second launch handing over to the running copy, Start with Windows (the Run entry on and
     off, and a hidden start with `--background`), clicking a notification while Dayflower runs, and today's date on
     the taskbar button (not pinned).
   - Windows took a blurry 16-pixel icon from `Dayflower.exe` for notifications, so Dayflower now registers its
     48-pixel icon instead.
   - A notification click while Dayflower isn't running starts nothing: COM ignores per-user registrations on this PC,
     even a throwaway one, and another developer reports the same on build 26200 ([laching#37](https://github.com/vmihalachi/laching/issues/37)).
     Dayflower is nearly always running, so this only affects clicking an old notification after Quit. Check again on
     another PC and with Velopack's install.
   - Pop-up banners couldn't be checked by script here (not even PowerShell's own), so they need a look by eye.
   - The taskbar date holds only while the window has no Start menu shortcut of its own; see step 3 for the installed
     copy.
3. **Installer and updates (Velopack).** Velopack runs first in `Main`. Installing adds a Start menu shortcut only;
   uninstalling stops Dayflower, removes the start-up entry and the notification registration, and deletes the cache
   and saved sign-ins, as removing the package did. Updates are checked at start and every few hours and downloaded in
   the background. Dayflower rarely closes, so an update applies at the next start (usually signing in to Windows) or
   at once from a "Restart to update" button in Settings.

   **Built and checked 2026-11-06** with test installs under their own app IDs (`tools\Pack.ps1 -PackId
   DayflowerTest`), which keep their own install folder, data, start-up entry and shortcut, so your real copy was
   never touched:
   - Velopack installs to `%LocalAppData%\Dayflower` and deletes that whole folder on uninstall, so the data moved to
     `%AppData%\Dayflower` (the usual place for Velopack and Squirrel apps), and the uninstall step deletes it.
   - Install: per user, no admin rights; a Start menu shortcut, Start with Windows on, an entry in Windows' Apps list.
   - Updates, all three ways: "Restart to update" (0.7.0 → 0.7.1, a 0.6 MB delta), a hidden start at sign-in with an
     update downloaded earlier (stays hidden), and Quit with one downloaded (applied after exit). Settings shows the
     state; `DAYFLOWER_UPDATE_SOURCE` points a copy at a local folder of packages for testing.
   - Uninstall removes everything: the program, data, start-up entry, shortcut, notification registration and Apps
     list entry, also while Dayflower runs in the background (Velopack doesn't stop it, so the uninstall step does).
   - The taskbar drew a generic icon for the installed copy: it reads the shortcut's icon from `Dayflower.exe` and
     can't use the PNG-compressed small sizes `MakeAssets.ps1` wrote. Sizes below 256 px are now classic bitmaps.
   - Today's date on the taskbar (`TaskbarDate`). Windows 11 draws the button from the Start menu shortcut that
     shares the window's taskbar identity, not from the window's icon; the date showed earlier only because the
     program icon was unreadable and the taskbar fell back to the window's. So the installed copy points its Start menu
     shortcut (and pinned copies) at today's icon, which also dates the Start menu entry, and when the date changes
     with the window open it briefly switches the window's identity so the taskbar redraws the button. A development
     build gets an identity no shortcut carries, so the taskbar shows its window's icon. Checked with test installs;
     the midnight redraw and a pinned button only with stand-in windows so far.
   - The taskbar can't read icons in a folder created directly under AppData since Windows started (a blank page
     instead), which includes the installed copy's own folder, while older folders and Temp are fine (Windows 11 build
     26300). So today's icon is copied to `%TEMP%\Dayflower` and put back if a cleanup removes it. Without that, even
     the plain program icon would be blank on the taskbar until the next sign-in after installing.
   - The installer is about 105 MB and a full update about 98 MB (the .NET runtime and Windows App SDK included);
     ordinary updates are deltas of under a megabyte.
4. **Release workflow.** Pushing a version tag (`v0.7.0`) runs the tests, builds x64 and ARM64 with the OAuth client
   from Actions secrets, packs `Setup.exe` plus full and delta update packages, and publishes a GitHub Release with
   notes. The tag sets the version number.

   **Written 2026-11-06** (`.github/workflows/release.yml`, notes from `CHANGELOG.md`, `v…-beta.N` tags make
   pre-releases that installed copies ignore). Checked locally: the notes step, fetching the previous release (none
   yet, which it handles), and packing both x64 and ARM64 on an x64 PC (the ARM64 program really is ARM64). Not run
   on GitHub yet: it needs the two OAuth secrets and a pushed tag.
5. **Ready for other people.**
   - An About section in Settings: version, privacy policy, "Report a problem" (GitHub Issues) and "Open logs folder".
   - The README covers installing, what the SmartScreen warning means and why it's there, and that a work Google
     account may need its admin's approval.
   - Checked live: an invitation notification end to end, high contrast, and a first run on a Windows account that
     has never had Dayflower.
   - Moving your own copy over: done on 2026-11-06 (the development package uninstalled, its data copied to
     `%AppData%\Dayflower`); installing the first release over it keeps that data.
6. **Signing (you apply, I wire it in).** Apply to SignPath Foundation once the release workflow exists (they sign
   files built by GitHub-hosted CI from a public repo, and everyone on the project needs two-factor sign-in on GitHub
   and SignPath). Then CI signs Dayflower's own files before packing and `Setup.exe` after. A paid certificate is the
   fallback if SignPath says no.
7. **winget.** Once signed: a manifest in winget-pkgs (installer type exe, silent install), updated by the release
   workflow on each release.

- **Done when:** a version tag produces a release on GitHub; a PC that never had Dayflower installs it without admin
  rights and updates itself to the next release; Google has verified the app; and, once SignPath has approved it, the
  installer is signed and installable with winget.

### Later
- Providers, suggested order: Microsoft 365 / Outlook, ICS subscriptions, iCloud, then other CalDAV (iCloud and
  CalDAV share one engine).
- Polish translation.

## 6. Testing

- **Unit tests (Core):** meeting-link detection, duplicate merging, reminder planning (defaults vs overrides,
  daylight-saving changes, all-day events, missed reminders), recurrence and time-zone conversion.
- **Mapping tests (Google):** recorded Google API responses (scrubbed of personal data) checked against the Core
  model, including odd cases: conference add-ons, Workspace event types, cancelled instances of a series.
- **UI smoke tests:** a few FlaUI tests that start the app and walk the main screens.
- **Manual checks per phase:** a short checklist against your real accounts, plus a light/dark visual review. I can
  build, run and test from the command line, but you'll be the one judging the look.
- CI runs the unit and mapping tests on every push.

## 7. Release and distribution

- **GitHub Releases:** the main channel. A Velopack `Setup.exe` that installs per user in `%LocalAppData%` without
  admin rights, and updates that download only what changed. An `.exe` installs unsigned, unlike an MSIX, which
  Windows refuses unless the user trusts its certificate by hand.
- **Signing:** until it's signed, SmartScreen shows "Windows protected your PC" (More info › Run anyway) and Smart App
  Control, where it's on, blocks the app outright. SignPath Foundation signs open-source projects for free, by
  application. Azure Artifact Signing only takes individual developers in the USA and Canada, so it's out. A paid
  certificate (a few hundred euros a year) is the fallback.
- **winget:** once the installer is signed.
- **Microsoft Store:** maybe later. Registration has been free for individual developers since September 2025, and
  the Store would sign and update the MSIX build. Not needed for the first release.

## 8. Risks

| Risk | Plan |
|---|---|
| The work Workspace blocks the unverified app | Tested in phase 1; that admin can trust the client ID; verification in phase 7 also helps. |
| Recurring events and sync are the hardest part to get exactly right | Spike in phase 1; tests built from real data. |
| The custom calendar grid is the largest UI job (layout, overlaps, drag & drop, performance) | Read-only grid first (phase 2), interaction later (phase 5). |
| Google verification takes weeks or asks for changes | Narrow scopes from the start; begin as soon as features are complete. |
| Reminder timing across sleep, restarts and DST | Scheduler logic is pure and unit-tested; manual sleep/restart checks in phase 4. |
| Google's API can't propose a new meeting time | Out of scope; "Open in Google Calendar" link in the flyout for that. |
| Windows features behave differently without a package (notifications, start-up) | Checked first in phase 7, before the installer work. |
| An unsigned installer puts people off, and Smart App Control blocks it | The README and homepage explain the warning; apply to SignPath as soon as the release workflow exists; a paid certificate if refused. |

## 9. Not in v1

Natural-language quick add, Google Tasks, extra time-zone columns in the grid, a global join hotkey, auto-join at
start, a Windows Widgets board widget, work-week / 3-day / year views, offline editing,
uploading attachments, contact autocomplete from Google Contacts, other providers, translations.

## 10. Still open

1. **Name check.** "Dayflower" turned up no app of that name in the Microsoft Store or on the web; the nearest
   are several "DayFlow" planner apps on iOS. Trademarks were only partly checked (US records via third-party
   mirrors). Before the public release: search EUIPO, USPTO and the Polish patent office (software classes 9 and 42).
   The domain and homepage carry the name, so this comes before buying the domain.
2. **Signing:** SignPath Foundation's answer (phase 7); a paid certificate if they say no.
