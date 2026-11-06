# Dayflower: plan

A Windows 11 calendar app that shows several Google accounts in one calendar, puts a **Join** button on every
video meeting, and raises desktop reminders that match the reminders set in Google Calendar. It's built to look
like part of Windows 11, starts with Google, and leaves room for Microsoft 365, iCloud, CalDAV and ICS later.

Status: agreed in the planning interview on 2026-11-06. Phases 0 (setup) and 1 (accounts and sync) are done;
phase 2 (calendar views) is next.

## 1. Decisions

| Topic | Decision |
|---|---|
| Stack | WinUI 3 (Windows App SDK) + C# on .NET 10 LTS. Windows-only. |
| Accounts | Several Google accounts at once: personal Gmail and a Workspace account run by someone else's admin. |
| Audience | Public release later: Microsoft Store, plus a public GitHub repo. |
| Background | Tray app. Closing the window hides it; it keeps syncing and reminding. Starts with Windows (switchable in Task Manager). |
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
| Install | MSIX. Microsoft Store as the main channel, plus GitHub Releases and winget. |
| Name | Dayflower: a flower whose blooms last a single day. Chosen for now; a full trademark check comes before the Store listing. |
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
  offline), plus "Search Google" for events outside the cached range.

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
  and the difference from your time, with "+1" or "−1" when it's a different day (for example "Team NY 09:14 −6h").
- Zones are added from a searchable city / time-zone list, can be renamed and reordered, and follow daylight saving
  automatically.

### 2.8 Tray and background

- Tray icon with the next event in its tooltip ("Next: Standup in 12 min"). Left-click opens the window; right-click:
  Open, New event, Sync now, Settings, Quit.
- Closing the window hides it to the tray. Starts with Windows (Windows' startup-app switch, also in Task Manager).

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
    Dayflower.App/               WinUI 3 app: windows, views, view models, tray, notifications, startup
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
| Tray icon | H.NotifyIcon.WinUI (WinUI 3 has no built-in tray icon) |
| Google | The Calendar REST API called directly with HttpClient and System.Text.Json, plus Dayflower's own OAuth flow (loopback + PKCE). Chosen over Google's .NET client libraries in phase 1: lighter, and the tests feed Google-shaped JSON into the same code the app runs. |
| Cache and search | Microsoft.Data.Sqlite with SQLite FTS5 |
| Recurrence | Ical.Net (RRULE expansion) |
| Saved sign-ins | Windows DPAPI (System.Security.Cryptography.ProtectedData) |
| Notifications | Windows App SDK app notifications (toasts with buttons and a snooze picker) |
| Logging | Serilog to a rolling file in the app's local folder |
| Tests | xUnit; FlaUI for UI smoke tests |

The day/week/month grid is a custom control. There is no free WinUI scheduler control, and commercial ones don't
fit a public open-source project.

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

**Verification for the public release (phase 7):** Google reviews apps that use Calendar scopes: a homepage, a
privacy policy, a verified domain, a short demo video and a reason for each scope. A cheap domain of its own (or a
verifiable GitHub Pages site) hosts the homepage and privacy policy. Review takes days to weeks, so it starts as soon
as the app is feature-complete.

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

### Phase 3: Join (M)
- Meeting-link detection with a unit-test suite built from real-world invites (Meet, Zoom, Teams, Webex…).
- Join in the details flyout and on grid events; Meet opens as the right account; "Join as …" for merged events.
- **Done when:** every meeting in your calendars shows Join in the flyout, the grid button appears 10 minutes before,
  and Meet opens as the correct account in your default browser.

### Phase 4: Reminders, tray and start-up (M)
- Reminder scheduler, toasts with Join / Snooze / Dismiss, "starting now" alert, missed-reminder handling.
- Tray icon and menu, hide-to-tray, start with Windows.
- **Done when:** reminders fire at the same times Google Calendar's would, with the window closed, after sleep and
  across a restart. **This is the daily-driver milestone.**

### Phase 5: Editing (L)
- Quick create, full editor, delete, RSVP with note, guests with autocomplete and availability, Meet link,
  reminders, colours, recurrence and the this / following / all choices, "notify guests?", drag & drop,
  Workspace event types. Offline: editing disabled with a clear bar.
- **Done when:** everything you'd normally do in Google Calendar on the web can be done in the app, and the result
  looks right in Google Calendar.

### Phase 6: Search, invitations, world clock, settings (M)
- Search (cached + "Search Google"), invitation toasts with Accept / Maybe / Decline, world clock in the side pane,
  the full Settings page, keyboard shortcuts, accessibility pass.
- **Done when:** these work end to end, and a keyboard-only and screen-reader pass finds no blockers.

### Phase 7: Public release (M)
- MSIX packaging and Store listing (reserve the name in Partner Center early, ideally in phase 0), screenshots,
  privacy policy and homepage, Google OAuth verification, GitHub Releases and winget.
- **Done when:** the app is live in the Microsoft Store and installable with winget.

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

- **Microsoft Store:** primary channel. The Store signs the package for free and handles updates. Needs a Partner
  Center developer account (check the current fee for individual developers).
- **winget:** can install straight from the Store listing.
- **GitHub Releases:** a downloadable MSIX must be code-signed, otherwise Windows won't install it without the
  user trusting a certificate by hand. Options: SignPath Foundation (free signing for open-source projects, by
  application) or a paid certificate. Decided in phase 7; until then GitHub Releases link to the Store.

## 8. Risks

| Risk | Plan |
|---|---|
| The work Workspace blocks the unverified app | Tested in phase 1; that admin can trust the client ID; verification in phase 7 also helps. |
| Recurring events and sync are the hardest part to get exactly right | Spike in phase 1; tests built from real data. |
| The custom calendar grid is the largest UI job (layout, overlaps, drag & drop, performance) | Read-only grid first (phase 2), interaction later (phase 5). |
| Google verification takes weeks or asks for changes | Narrow scopes from the start; begin as soon as features are complete. |
| Reminder timing across sleep, restarts and DST | Scheduler logic is pure and unit-tested; manual sleep/restart checks in phase 4. |
| Google's API can't propose a new meeting time | Out of scope; "Open in Google Calendar" link in the flyout for that. |

## 9. Not in v1

Natural-language quick add, Google Tasks, extra time-zone columns in the grid, a global join hotkey, auto-join at
start, Join in the tray menu, a Windows Widgets board widget, work-week / 3-day / year views, offline editing,
uploading attachments, contact autocomplete from Google Contacts, other providers, translations.

## 10. Still open

1. **Name check.** "Dayflower" turned up no app of that name in the Microsoft Store or on the web; the nearest
   are several "DayFlow" planner apps on iOS. Trademarks were only partly checked (US records via third-party
   mirrors). Before the Store listing: search EUIPO, USPTO and the Polish patent office (software classes 9 and 42),
   and reserve the name in Partner Center.
2. **GitHub download signing** (SignPath Foundation or a paid certificate), decided in phase 7.
