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
3. **Data access:** click **Add or remove scopes**, paste these into **Manually add scopes**, click **Add to table**,
   then **Update** and **Save**:

   ```
   openid
   https://www.googleapis.com/auth/userinfo.email
   https://www.googleapis.com/auth/userinfo.profile
   https://www.googleapis.com/auth/calendar.calendarlist.readonly
   https://www.googleapis.com/auth/calendar.events
   https://www.googleapis.com/auth/calendar.events.freebusy
   ```

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

Git ignores `google-oauth.local.props`, so the values stay on your PC and never reach the public repo.

## 7. Give the client to release builds

The release workflow (`.github/workflows/release.yml`) builds the installer on GitHub, so it needs the same two
values as repository secrets. Each command asks for the value; paste it there, not on the command line:

```powershell
gh secret set GOOGLE_OAUTH_CLIENT_ID --repo kstawi/dayflower
gh secret set GOOGLE_OAUTH_CLIENT_SECRET --repo kstawi/dayflower
```

A release fails straight away if either is missing, rather than shipping a Dayflower that can't sign in.

## 8. Homepage and domain, for Google's verification

Google's review of the Calendar permissions needs Dayflower's homepage and privacy policy on a domain you own,
verified in Google Search Console. The homepage is in `site/` and the Homepage workflow publishes it with GitHub
Pages.

### First try: GitHub's free address

Google's rules don't say whether a GitHub Pages address counts as a domain you own, so this is worth a try before
buying one. `github.io` is on the public suffix list, so `kstawi.github.io` is a domain of its own for Google.

1. **Switch on Pages for the repo:** repo **Settings** › **Pages** › Source **GitHub Actions**. The homepage appears
   at `https://kstawi.github.io/dayflower/`.
2. **Verify `kstawi.github.io` with Google:** in [Google Search Console](https://search.google.com/search-console),
   signed in as the Google account that owns the Dayflower Cloud project, add a **URL prefix** property for
   `https://kstawi.github.io/` and choose the **HTML file** method. Google's file has to be served from the top of
   that address, which is the user site: a repo named `kstawi.github.io` with that file (and an `index.html` that
   forwards to `/dayflower/`), published from its main branch. Then click **Verify**. Keep the file there.
3. **Fill in the consent screen's branding:** Google Cloud console › **Google Auth Platform** › **Branding**:
   - App home page: `https://kstawi.github.io/dayflower/`
   - Privacy policy: `https://kstawi.github.io/dayflower/privacy.html`
   - Authorized domains: `kstawi.github.io`

Then follow [google-verification.md](google-verification.md): the brand check, and the demo video for the data
access review. If Google won't accept the address, use a domain of your own instead:

### If Google refuses it: a domain of your own

1. **Buy a domain** at a registrar of your choice (below, `example.com` stands for it). If it came with parking or
   forwarding records, delete them.
2. **Verify it for your GitHub account**, so no one else can attach it to their site: on GitHub, click your profile
   picture › **Settings** › **Pages** (your profile's settings, not the repo's) › **Add a domain**, enter
   `example.com`, then add the TXT record it shows at your registrar: name `_github-pages-challenge-kstawi`,
   value as shown. Click **Verify** once it's in (DNS changes can take up to a day). Keep the record afterwards.
3. **Give the repo's Pages the domain:** repo **Settings** › **Pages** › Custom domain `example.com` › **Save**.
   A workflow-published site takes its domain from here, not from a CNAME file.
4. **Point the domain at GitHub** at your registrar:

   | Type | Name | Value |
   |---|---|---|
   | A | `@` | `185.199.108.153` |
   | A | `@` | `185.199.109.153` |
   | A | `@` | `185.199.110.153` |
   | A | `@` | `185.199.111.153` |
   | AAAA | `@` | `2606:50c0:8000::153` |
   | AAAA | `@` | `2606:50c0:8001::153` |
   | AAAA | `@` | `2606:50c0:8002::153` |
   | AAAA | `@` | `2606:50c0:8003::153` |
   | CNAME | `www` | `kstawi.github.io` |

   GitHub then serves the site at both names and redirects one to the other. Check with
   `Resolve-DnsName example.com`.
5. **HTTPS:** once GitHub has issued the certificate (usually minutes, sometimes up to a day), tick **Enforce
   HTTPS** in the repo's Pages settings. If it hasn't come after a while, remove the custom domain and save it again.
6. **Verify the domain with Google:** in [Google Search Console](https://search.google.com/search-console), signed
   in as the Google account that owns the Dayflower Cloud project, add a **Domain** property for `example.com`
   and add the TXT record (`google-site-verification=…`) it gives you at your registrar, then click **Verify**.
7. **Fill in the consent screen's branding:** Google Cloud console › **Google Auth Platform** › **Branding**:
   - App home page: `https://example.com`
   - Privacy policy: `https://example.com/privacy.html`
   - Authorized domains: `example.com`

After that the app is ready to submit for verification, with a demo video and a reason for each permission.

## Work accounts

A Workspace account run by someone else's admin may refuse to sign in with a message like **"Access blocked:
Dayflower has not completed the Google verification process"** or **"blocked by your admin"**. In that case the
Workspace admin has to trust the app:

1. Admin console › **Security › Access and data control › API controls**.
2. **Manage third-party app access › Add app › OAuth App Name Or Client ID**.
3. Paste Dayflower's client ID and set access to **Trusted**.
