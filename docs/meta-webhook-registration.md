# Meta webhook registration walkthrough — IG bridge (plan step 5)

**Prep doc — not yet executed.** Chris drives this in the Meta developer console. Gets the bridge
receiving Instagram DMs (dev-mode = Testers only, no App Review needed to test). Do this **after** the
bridge has a public HTTPS URL (step 4 / ngrok) so the verification handshake can succeed.

> **Decision on record — use a DEDICATED Meta app, NOT the FB Messenger app `27031932926443539`.**
> Meta reviews the *whole app*, one submission at a time, and a submission is irreversible once in
> review. A separate app keeps IG's `instagram_manage_messages` App Review clock independent of the
> in-flight FB `pages_messaging` review. (Resolved 2026-05-23.) Everything below is on the **new** app.

---

## 0. Prerequisites (Meta side)

- **Instagram professional account** (Business or Creator), **linked to the AWD Facebook Page** in
  Meta Business Suite → linked accounts. AWD already posts/receives on IG — verify the link exists.
- IG account setting **"Allow access to messages"** enabled (Instagram → Settings → connected tools / messaging).
- Admin on the IG account + linked FB Page; Business Admin in the AWD business portfolio
  (`Alloy Wheels Direct`, `605826456988689`).
- Bridge deployed with a reachable **HTTPS** webhook URL (step 4):
  `https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync` (live).

---

## 1. Create the dedicated Meta app

1. developers.facebook.com → **My Apps → Create App**.
2. Use case: choose the one that exposes **Instagram messaging** (Business-type app; "Other" → Business
   if prompted). Name it distinctly, e.g. **"AWD Instagram Bridge"** (so it's never confused with the
   FB Messenger app).
3. Attach it to the **Alloy Wheels Direct** business portfolio.
4. Note **App ID** and, from **App → Settings → Basic**, the **App Secret** → this is
   `InstagramAdapterSettings:AppSecret` (store in Key Vault, step 4).

---

## 2. Add the Instagram product + permissions

1. In the app dashboard, **Add Product → Instagram** (Instagram messaging / "Instagram API setup with
   Instagram login" or the Messenger-Instagram path, depending on the current console layout).
2. Connect the **IG professional account** (the one linked to the AWD Page).
3. Permissions this bridge needs:
   - **`instagram_manage_messages`** — send/receive IG DMs (the long-pole App Review permission).
   - **`instagram_basic`** — read basic IG account info.
   - **`pages_show_list`**, **`pages_manage_metadata`** — Page link + webhook subscription management.
4. **Dev mode = no App Review needed for Testers.** Real customers require Live mode + approved review
   (step 9, sequenced after the FB review clears).

---

## 3. Mint the access token

- Generate a **long-lived Page/IG access token**. **Prefer a System User token** (Business settings →
  Users → System users → add → generate token with the IG/Page assets + the permissions above) — it
  doesn't expire, which avoids silent outbound breakage.
- This token → `InstagramAdapterSettings:PageAccessToken` (Key Vault).
- The **IG business account id** (the recipient id of inbound webhooks / the `/{id}/messages` path) →
  `InstagramAdapterSettings:IgBusinessId`. You can read it via Graph API Explorer
  (`GET /me/accounts` → Page → `instagram_business_account`) or the IG product settings.

---

## 4. Register the webhook + subscribe to messages

1. App dashboard → **Webhooks** (or Instagram product → Configure webhooks).
2. **Object = Instagram.** Add a callback:
   - **Callback URL:** `https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync`
   - **Verify token:** the exact string you set as `InstagramAdapterSettings:VerifyToken`. Meta sends a
     GET with `hub.mode=subscribe&hub.verify_token=…&hub.challenge=…`; the bridge echoes the challenge
     when the token matches → **"verified"** tick. (If it fails: confirm the app is running, the URL is
     HTTPS and public, and the verify token matches byte-for-byte.)
3. **Subscribe to the `messages` field** on the Instagram object. (Optional later: `messaging_postbacks`,
   `message_reactions`, `messaging_seen` — the adapter ignores these for now and degrades gracefully.)
4. Ensure the IG/Page object is subscribed to the app (the `subscribed_apps` edge — governed by
   `pages_manage_metadata`).

---

## 5. Add Testers + dev-mode test (leads into step 7)

1. App Roles → **Roles → Testers** → add **Chris / Lachy / Tony** (and accept the invite from each
   account). In dev mode, only people with an app role can message the IG account.
2. From a Tester's personal IG account, **DM the AWD Instagram account**.
3. Expect: the DM lands in the **D365 agent workspace** (via the custom Direct Line channel, step 6),
   and an **agent reply returns to Instagram**. Watch App Insights / the bridge logs for the inbound
   POST (signature-validated) and the outbound Send API call.

---

## 6. What stays for Live mode (step 9 — later, after FB clears)

- Submit **`instagram_manage_messages` + `instagram_basic`** for **App Review** on this dedicated app:
  screencast of the permission grant + real send/receive, valid privacy-policy URL, reviewer
  instructions. Same shape as the FB submission. ~10-day queue.
- **Sequence after** the in-flight FB `pages_messaging` review clears — Meta reviews one submission per
  app at a time, and the dedicated app keeps the *clocks* separate, but be deliberate about timing.

---

## Field → config cross-reference

| Meta value | Where it goes |
|---|---|
| App Secret (Settings → Basic) | `InstagramAdapterSettings:AppSecret` |
| Verify token (you choose; type into Webhooks UI) | `InstagramAdapterSettings:VerifyToken` |
| System User / Page access token | `InstagramAdapterSettings:PageAccessToken` |
| IG business account id | `InstagramAdapterSettings:IgBusinessId` |
| Graph API version (e.g. v21.0) | `InstagramAdapterSettings:GraphApiVersion` |
| Callback URL | `https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync` |
