# Google verification

Google reviews apps that use sensitive scopes before lifting the 100-user cap and the "Google hasn't verified this
app" warning. For Dayflower that's one scope, `calendar.events`; the calendar list, free/busy and sign-in scopes are
non-sensitive. The project is `dayflower-513910`, with one OAuth client, **Dayflower desktop** (Desktop app).

## Where it stands

| Step | State |
|---|---|
| Homepage and privacy policy | Live at <https://kstawi.github.io/dayflower/> |
| Search Console | `https://kstawi.github.io/` verified (HTML file in the `kstawi.github.io` repo), 2026-11-06 |
| Branding | Home page, privacy policy and authorized domain `kstawi.github.io` saved |
| Data access | All six scopes declared, with the reason below |
| Brand verification | First check 2026-11-07: "home page isn't registered to you; wait 24 hours". Retry from 2026-11-08 (Branding › Show problems › Problems resolved) |
| Data access verification | Needs the demo video below, then Verification Center › Prepare for verification |

## Why Dayflower needs `calendar.events`

As entered under Data access › How will the scopes be used:

> Dayflower is an open-source Windows desktop calendar (https://kstawi.github.io/dayflower/) that shows a user's
> Google calendars and lets them manage their events. It uses calendar.events to read the user's events, to show them
> in day, week, month and agenda views, raise the reminders set in Google Calendar as desktop notifications and offer
> a Join button for video meetings; and to make the changes the user asks for in the app: create, edit, move and
> delete events, reply to invitations, add a Google Meet link and set reminders. A narrower scope is not enough:
> calendar.events.readonly cannot make these changes, and Dayflower deliberately does not request the broader calendar
> scope, as it never creates, shares or deletes calendars. Calendar data goes only between Google and the user's own
> PC, where it is cached; Dayflower has no servers and sends it nowhere else.

## The demo video

Google wants to see the whole sign-in, with the consent screen in English and the OAuth client ID visible in the
browser's address bar, and then each use of the sensitive scope in the app. The "unverified app" screen will appear
and should stay in the video. Aim for 4–5 minutes, no narration needed: short captions (below) say what's happening.

### Before recording

1. **A demo Google account**, so no real events or email addresses appear: for example `dayflower.demo@gmail.com`.
   Set its language to English (Google Account › Personal info › General preferences for the web › Language), so the
   consent screen is in English.
2. **Demo events** in that account's Google Calendar, this week: four or five ordinary events, one with a Google Meet
   link, one repeating weekly. From another account (your personal one), invite the demo account to a meeting
   tomorrow, so there's an invitation to reply to.
3. **A clean Dayflower** that has only the demo account: a separate test install, so your own copy and data stay out
   of it (`.\tools\Pack.ps1 -Version 0.7.0 -PackId DayflowerDemo`, then run its `Setup.exe`; uninstall it afterwards).
   Run its installer from Explorer, not from a Claude Code shell, whose AppData and registry writes Windows doesn't
   see. Turn off its Start with Windows in its Settings. Start it with
   `%LocalAppData%\DayflowerDemo\current\Dayflower.exe` rather than from Start, whose entry is named
   "Dayflower (DayflowerDemo)". **Quit your everyday Dayflower while recording** (Settings › Quit): its reminders
   would show up as "Dayflower" too, with your real events.
4. **A browser window that only knows the demo account**: a new browser profile signed in to it, opened last so that
   Dayflower's sign-in lands there, and Google Calendar open in a second tab of it.
5. **A clean screen**: close other windows, and turn on Do Not Disturb except for Dayflower (Settings › System ›
   Notifications › Set priority notifications), so only Dayflower's notifications show.
6. **Record** with Snipping Tool (Win+Shift+R, then drag over the whole screen) at 1920×1080 if you can.

### Shot list

| # | Time | Do | Caption |
|---|---|---|---|
| 1 | 0:00 | Show the homepage <https://kstawi.github.io/dayflower/>, scroll to "How Dayflower uses your Google data". | Dayflower, a Windows calendar for Google Calendar. OAuth client: Dayflower desktop (project dayflower-513910). |
| 2 | 0:15 | Start Dayflower. It's empty: "Add a Google account to get started". Click **Add account**. | Signing in with Google. |
| 3 | 0:25 | The browser opens Google's sign-in. Choose the demo account. On "Google hasn't verified this app", click **Advanced**, then **Go to Dayflower (unsafe)**. | The app is awaiting verification, so Google shows this warning. |
| 4 | 0:45 | On the consent screen, click into the address bar and move along the URL until `client_id=…` shows (it matches the client ID under Clients). Show the app name and the list of permissions; tick every box if Google shows boxes. Click **Continue**. | The consent screen for the Dayflower desktop client, asking for calendar.events. |
| 5 | 1:05 | The browser says "You're signed in". Switch back to Dayflower: the demo calendars and this week's events appear. | calendar.events (read): the user's events in Dayflower. |
| 6 | 1:20 | Switch between Day, Week, Month and Agenda. | Every view is read with calendar.events. |
| 7 | 1:40 | Open the event with Google Meet: its details and the **Join** button. Click **Join**: Meet opens in the browser. | Join uses the meeting link in the event (calendar.events). |
| 8 | 2:00 | Create an event: Ctrl+N, **Add title** "Demo planning", pick a time, **Add guests** (another address), **Check availability**, **Add Google Meet video conferencing**, add a notification. **Save**, then **Don't send** when asked about invitation emails. | calendar.events (write): creating an event with a guest and a Meet link. |
| 9 | 2:35 | Switch to Google Calendar in the browser and refresh: the new event is there, with its guest and Meet link. | The change is in the user's Google Calendar. |
| 10 | 2:50 | Back in Dayflower, drag the event to another time. Open it, change the title, **Save**, **Don't send**. Refresh Google Calendar: both changes are there. | calendar.events (write): editing and moving an event. |
| 11 | 3:20 | Open the invitation from the other account. Under **Going?** click **Yes**, add a note, **Send**. In Google Calendar, the demo account is now "Going". | calendar.events (write): replying to an invitation. |
| 12 | 3:45 | Create an event starting in two minutes, with a Google Meet link and a one-minute notification, and wait: Dayflower's reminder notification appears, with Join and Snooze. | Reminders come from the event's notification settings (calendar.events). |
| 13 | 4:05 | Open "Demo planning", **Delete event**, **Don't send**. Refresh Google Calendar: it's gone. | calendar.events (write): deleting an event. |
| 14 | 4:20 | Open Settings › Accounts › **Remove** for the demo account. Then show the privacy page <https://kstawi.github.io/dayflower/privacy.html>. | Removing the account deletes its data from this PC. Data goes only between Google and the user's PC. |

### After recording

1. Upload to YouTube as **Unlisted**, titled "Dayflower – Google OAuth verification demo".
2. Paste the link under Google Auth Platform › Data access › Demo video › YouTube link, and save.
3. Once the brand is verified: Verification Center › **Prepare for verification**, check the details, and submit.
   Google usually replies within a few business days, by email to the developer contact address.
4. Uninstall the DayflowerDemo test install (Windows Settings › Apps).
