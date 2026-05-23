# Official Documentation Index — D365 Contact Center channels + Meta integration

**Purpose:** canonical first-party URLs (Microsoft Learn + Meta for Developers) for the D365 CC channel build, with the key actionable facts extracted so we don't re-derive them by clicking. **Always prefer these official sources over guesswork.**

**Last refreshed:** 2026-05-21. Re-fetch a row if it's driving a live build step — Microsoft Learn pages carry an `ms.date`; Meta docs are undated.

---

## Microsoft Learn — D365 Contact Center / Customer Service

| Topic | Canonical URL | ms.date | Notes |
|---|---|---|---|
| **Configure a Facebook channel** | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/configure-facebook-channel | 2026-03-11 | **Live URL is the `customer-service` path** (applies to Contact Center embedded + standalone + Customer Service). The `contact-center/administer/configure-facebook-channel` path **404s** — don't use it. Extract below. |
| Facebook channel setup FAQ | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/facebook-channel-setup-faq | — | Troubleshooting companion to the above. |
| Use the Facebook channel (rep side) | https://learn.microsoft.com/en-us/dynamics365/customer-service/use/facebook | — | What the service rep sees in the agent desktop. |
| Configure WhatsApp via ACS | https://learn.microsoft.com/en-us/dynamics365/contact-center/administer/configure-whatsapp-acs | — | A3 WhatsApp path (deferred — SIM/OTP). |
| Configure Teams Phone in voice channel | https://learn.microsoft.com/en-us/dynamics365/contact-center/administer/configure-teams-phone-in-voice-channel | 2026-02-20 | C7 voice path. Service-numbers-only constraint. |
| **Supported cloud locations for voice channel** | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/voice-channel-region-availability | 2026-04-15 | **DECISIVE for C7.** Per-geo GA vs trial matrix. **UK (crm11): GA = Available, trial = "To be announced"** → voice not in UK-local trial. Extract below. |
| Install the voice channel | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/voice-channel-install | 2025-07-30 | Install = Provision channels → Voice → *Add voice* = Yes + Voice & SMS Terms. Has licensing prereq. |
| Provision channels (Contact Center) | https://learn.microsoft.com/en-us/dynamics365/contact-center/implement/provision-channels | — | The "Set up channels" toggles incl. *Add voice*. |
| Use trial phone numbers in the voice channel | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/voice-channel-trial-phone-numbers | — | Trial = up to 2 toll-free **US** numbers, 60 min total; **US-only, inbound calls only, no SMS, no outbound**. |
| Manage phone numbers (voice) | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/voice-channel-manage-phone-numbers | — | Add ACS number; US-only purchasable in-product; other regions via Azure + sync. |
| Contact Center trial FAQ | https://learn.microsoft.com/en-us/dynamics365/contact-center/implement/contact-center-trial-faq | 2026-05-08 | Trial "features available" lists voice — but region matrix above overrides for UK-local. New trial NOT in Brazil/GCC/Norway/South Africa. Can't bulk-delete sample data; convert-to-paid = fresh env recommended. |
| Understand and create workstreams | https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/create-workstreams | — | Routing + work distribution; referenced by every channel. |

### Extract — Configure a Facebook channel (the active C-channel task)

**Prerequisites (Meta side):** a Facebook Page with Messenger enabled; a Facebook **Business**-type app with the **Messenger** product added and the Page added under Messenger settings; `pages_messaging` permission; App ID + App Secret from **app → Settings → Basic**.

**Dev-mode vs Live:** *"If the Facebook app is in development mode, only Facebook users who have roles within the app can send messages to the Facebook page."* → real customers require the app **Live + `pages_messaging` App-Review-approved**. For testing, add testers under **App Roles → Roles → Testers**. (This is why AWD needs App Review to go live — confirmed first-party.)

**Optional:** **Business Asset User Profile Access** feature → lets D365 retrieve the customer's Facebook username into the contact record.

**Exact D365 fields (Copilot Service admin center → Customer support → Channels → Accounts → Messaging accounts → Manage → New account):**
1. **Channel details:** name + select **Facebook** in *Channels*.
2. **Add account:** **Application ID** + **Application secret** (from FB app → Settings → Basic).
3. **Add Facebook Page** pane:
   - **Page name** — the Page's name.
   - **Page ID** — FB Page → **About** → copy *Page ID*.
   - **Page access token** — FB app → **Messenger → Settings → Access Tokens** → select the page → copy **Page Access Token**. *(Tokens can expire randomly; refresh on a ≤60-day schedule.)*
4. **Callback information:** **Callback URL** + **Verify token** auto-populate **after you save**. *(Not generated if the same Page is reused across multiple channel instances.)*

**Wire the webhook back on Meta:** FB app → **Messenger settings** → add the **Callback URL** → **Add subscriptions for the page** → tick **`messages`** → save. Then in D365: Channels → Messaging Accounts → your FB account → **Facebook Pages** tab → confirm **Provisioning state = Running**.

**Workstream:** create a workstream → **Set up Facebook** → pick the Page → Language → Behaviors (automated messages, post-conversation survey) → **User features**: *File attachments* (yes/yes for two-way), *Facebook message tag* (Yes = reps can message >24h, up to 7 days; must ALSO enable the human-agent tag in the FB app) → Summary → Create. Then routing rules + work distribution; optionally add an AI agent.

**Privacy note (first-party):** enabling FB shares data with Facebook, flowing outside the org's compliance/geo boundary. Customer is responsible for end-user monitoring/recording notices + consent.

---

## Meta for Developers

| Topic | Canonical URL | Notes |
|---|---|---|
| App Review process (overview) | https://developers.facebook.com/docs/resp-plat-initiatives/app-review/ | Top-level App Review hub. |
| **App Review tutorial / common rejection reasons** | https://developers.facebook.com/docs/resp-plat-initiatives/appreview/tutorial | Extract below. (Note: the trailing-slash `…/tutorial#common-mistakes/` anchor variant **404s** — use the no-slash `…/appreview/tutorial`.) |
| **`pages_messaging` permission reference** | https://developers.facebook.com/docs/permissions/reference/pages_messaging/ | Extract below. |
| Messenger Platform (Pages) | https://developers.facebook.com/docs/messenger-platform/ | Webhooks, message tags, 24h window. |
| Tech Providers (irreversible status) | https://developers.facebook.com/docs/development/release/tech-providers/ | AWD committed to Tech Provider 2026-05-21 — required to submit any permission to App Review. |
| Business verification | https://developers.facebook.com/docs/development/release/business-verification | AWD = Verified (2026-05-20). |
| App roles (testers) | https://developers.facebook.com/docs/development/build-and-test/app-roles/ | Dev-mode testing path (add Chris/Lachy/Tony as testers). |
| Page access tokens (60-day refresh) | https://developers.facebook.com/docs/pages/access-tokens | Token rotation discipline. **NB: the doc's `/me/accounts → Page token` route does NOT work for New-Pages-Experience / business-claimed Pages** — see operational playbook below. |
| **`pages_manage_metadata` permission reference** | https://developers.facebook.com/docs/permissions/reference/pages_manage_metadata/ | *"Allows your app to subscribe and receive Webhooks about activity on your Page, and update settings on your Page."* = governs the `subscribed_apps` webhook subscription → why it's the hard dependency of `pages_messaging`. |
| Page `subscribed_apps` edge (webhooks) | https://developers.facebook.com/docs/graph-api/reference/page/subscribed_apps/ | GET/POST the app↔Page webhook field subscription (e.g. `messages`). GET is the `pages_manage_metadata` API-test call we used. |
| New Pages Experience | https://developers.facebook.com/docs/pages/new-pages-experience | Business-managed Pages; changes how Page tokens are obtained (need `business_management` or MANAGE task). Root of the token-retrieval gotcha. |
| Graph API Explorer (tool) | https://developers.facebook.com/tools/explorer/ | Where we mint the user→Page token + fire API-test calls. |

### Extract — `pages_messaging` (the permission AWD is submitting)

- **Allows:** manage + access Page conversations in Messenger — read threads, send messages for customer support, confirm transactions (purchases/bookings).
- **Allowed-usage description Meta expects:** interactive experiences the *user initiates*, customer support via messaging, and confirming customer interactions (orders/reservations). **Not** unsolicited marketing.
- **App Review screencast for `pages_messaging` must show:** (1) the full Facebook Login flow with the user *granting* the permission; (2) either receiving an incoming message **or** sending one, with the corresponding message visible in the **native Messenger client**; (3) a **cURL request example** (use Meta's **API Integration Helper**) demonstrating send capability.
- **Paired dependencies:** `pages_manage_metadata` + `pages_show_list` (both already bundled in AWD's submission).

### Extract — App Review submission requirements + top rejection reasons

**Each submission must include:**
- **Allowed usage** — certify the app uses each requested permission within allowed usage + give the reason each is needed. Remove anything unneeded.
- **Data handling** — secure processing of Meta personal data, data-sharing disclosure, deletion procedures, security; compliance with Platform Terms + Developer Policies.
- **Reviewer/test instructions** — app/website access details, **valid privacy-policy URL**, working credentials/codes if needed. Verify the app works *before* submitting.
- **Screen recording** — show the user granting **each** requested permission + real feature usage. ≥1080p, recorded at ≤1440px width, captions, visible mouse cursor, **no audio**.

**Top rejection reasons:** (1) app inaccessible/non-functional; (2) missing screencast for a requested permission; (3) app looks unfinished; (4) requesting permissions not yet needed; (5) fake FB accounts used in the demo; (6) broken Facebook Login during review.

**Submission is irreversible once in review** — Meta now reviews the *entire* app (icon, display name, privacy + data-deletion URLs), and you **cannot edit or cancel** a submission while it's in review. Get it right before clicking Submit.

---

## AWD-specific state (as of 2026-05-21)

- FB app **AWD Contact Center**, App ID **`27031932926443539`**, business portfolio **Alloy Wheels Direct** (`605826456988689`), use case **Engage with customers on Messenger from Meta**, mode **In development / Unpublished**.
- **Tech Provider = committed (irreversible)** 2026-05-21.
- App Review **draft** `27043210481982450` open with `pages_messaging`, `pages_manage_metadata`, `pages_show_list`, `business_management`, `public_profile` — **not yet submitted** (paused at the submission checklist: Verification / App settings / Allowed usage / Data handling / Reviewer instructions + screencast).
- Full build log: [`phase1-execution-log-2026-05-20.md`](../logs/phase1-execution-log-2026-05-20.md). Connector strategy: [`meta-channel-connectors.md`](meta-channel-connectors.md).
</content>
</invoke>
