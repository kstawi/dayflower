# Dayflower privacy policy

Effective 19 November 2026.

Dayflower is a calendar app that runs on your Windows PC. It has no servers of its own: the developer never
receives, stores or sees your data.

## What Dayflower accesses

When you add a Google account, Google asks you to let Dayflower:

- read and change your Google Calendar data: your calendars, events, invitations, guests, reminders and the
  free/busy times of people you invite, so Dayflower can show your calendar and make the changes you ask for;
- see your account's email address, so Dayflower can tell your accounts apart.

Dayflower only uses this data to show and manage your calendar on your PC. It doesn't use Google user data for
advertising, and doesn't use it to develop, improve or train AI or machine learning models.

## Who Google user data is shared with

Dayflower does not share, transfer or disclose Google user data to any third party. The developer never receives it,
so it isn't shared with advertisers, data brokers or anyone else, and there is none the developer could disclose on
request. Dayflower doesn't sell or rent it either.

What leaves your PC, and when:

- **Your calendar goes only to Google.** Dayflower talks directly from your PC to Google's APIs. Your calendar data,
  the changes you make, your replies to invitations and free/busy lookups travel only between your PC and your
  Google account.
- **A meeting link opens in your browser when you click Join.** Your default web browser then takes you to that
  meeting service, such as Google Meet, Zoom or Microsoft Teams.
- **Logs stay on your PC unless you share one.** Dayflower never uploads its logs. If you attach one to a bug report
  on GitHub, it becomes public. Logs can contain your account's email address and account, calendar and event IDs.

Dayflower's other connections carry no Google user data:

- **Updates come from GitHub.** Every few hours Dayflower asks GitHub, where its releases are published, whether
  there's a new version, and downloads it from there. These requests contain no calendar or account data; GitHub
  sees your IP address, as with any download (see
  [GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)).
- **No analytics, telemetry or ads.** Dayflower doesn't track how you use it.

Dayflower's use and transfer of information received from Google APIs adheres to the
[Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including the Limited Use requirements.

## How your data is protected

- **No central copy.** Dayflower has no servers, so there is no developer database of users' data that could leak.
- **Encrypted in transit.** Every connection to Google uses HTTPS, and Windows checks Google's certificates.
- **Your password stays with Google.** You sign in on Google's own page in your browser, using OAuth 2.0 with PKCE.
  Dayflower never sees your Google password.
- **Sign-in tokens encrypted on your PC.** Google sign-in tokens are encrypted with Windows Data Protection (DPAPI)
  for your Windows user account, so other Windows users and other PCs can't read them.
- **Kept in your user profile.** Your cached calendar data, settings and logs are kept in Dayflower's folder in your
  Windows user profile, which Windows limits to your account and the PC's administrators. To also protect them if
  your PC is lost or stolen, turn on device encryption or BitLocker in Windows.
- **Only the access it needs.** Dayflower asks only for the calendar permissions its features use, and seeing your
  guests' free/busy times is optional.

## Keeping and deleting your data

- Removing an account in Dayflower deletes that account's sign-in tokens and cached calendar data from your PC, and
  asks Google to withdraw Dayflower's access to it. The event IDs Dayflower keeps so it doesn't repeat a reminder
  or an invitation alert stay until you uninstall.
- Dayflower keeps only its 7 most recent daily log files and deletes older ones.
- Uninstalling Dayflower deletes all of its data from your PC.
- You can withdraw Dayflower's access to your Google account at any time at
  <https://myaccount.google.com/permissions>.

## Changes

If this policy changes, the new version is published here with a new effective date. The history of this file on
GitHub shows every earlier version.

## Contact

Questions about this policy: open an issue at <https://github.com/kstawi/dayflower/issues>.
