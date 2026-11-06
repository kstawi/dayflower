# Google Cloud setup

Dayflower signs in to Google with an OAuth client that belongs to a Google Cloud project. You set this up once.
It takes about 10 minutes and costs nothing. Google renames console pages now and then, so the labels below may
differ slightly.

Use the personal Google account that should own the app, not a work account.

## 1. Create the project

1. Open <https://console.cloud.google.com> and sign in.
2. In the project picker at the top, choose **New project**, name it `Dayflower`, and create it.
3. Make sure the new project is selected in the project picker.

## 2. Turn on the Google Calendar API

1. Go to **APIs & Services › Library**.
2. Search for **Google Calendar API**, open it and click **Enable**.

## 3. Set up the consent screen

This is what people see when Dayflower asks for access to their calendar. It lives under **Google Auth Platform**
(older consoles call it **OAuth consent screen**).

1. Click **Get started** and fill in:
   - **App name:** `Dayflower`
   - **User support email:** your email
   - **Audience:** **External**, so Gmail and Workspace accounts from any organisation can sign in
   - **Contact information:** your email
2. Agree to Google's API Services User Data Policy and finish.
3. **Data access:** nothing to add yet. Phase 1 adds the calendar permissions here, once it's clear which ones
   Dayflower needs.

## 4. Publish the app

Google only publishes an app whose branding has a homepage and a privacy policy. Under **Branding**, fill in:

- **App home page:** `https://github.com/kstawi/dayflower`
- **App privacy policy link:** `https://github.com/kstawi/dayflower/blob/main/PRIVACY.md`
- **Authorized domains:** `github.com`

Save, then go to **Audience** and click **Publish app**, so the status changes from **Testing** to
**In production**.

Don't skip this. In Testing status Google ends every sign-in after 7 days, and you'd have to sign in again each week.

Publishing doesn't make anything public and doesn't need Google's review yet. Until Google verifies the app
(planned for the public release), sign-in shows a **"Google hasn't verified this app"** page: click **Advanced**,
then **Go to Dayflower**. That's expected, and there's a limit of 100 users until verification.

## 5. Create the OAuth client

1. Go to **Clients** and click **Create client**.
2. **Application type:** **Desktop app**. **Name:** `Dayflower desktop`.
3. Click **Create**. Copy the **client ID** and **client secret** straight away, or click **Download JSON**.
   Google may show the secret only this once; if you lose it, add a new secret to the same client.

## 6. Give the client to your local build

1. In the repo folder, copy `google-oauth.local.props.example` to `google-oauth.local.props`.
2. Put the client ID and secret in it.

Git ignores `google-oauth.local.props`, so the values stay on your PC and never reach the public repo. You don't
need to paste them anywhere else.

## Work accounts

A Workspace account run by someone else's admin may refuse to sign in with a message like **"Access blocked:
Dayflower has not completed the Google verification process"** or **"blocked by your admin"**. In that case the
Workspace admin has to trust the app:

1. Admin console › **Security › Access and data control › API controls**.
2. **Manage third-party app access › Add app › OAuth App Name Or Client ID**.
3. Paste Dayflower's client ID and set access to **Trusted**.

Phase 1 tests this with your work account early.
