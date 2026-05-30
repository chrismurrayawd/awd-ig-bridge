# Meta App Review submission — AWD Social Messaging (Instagram)

Paste-ready content for the Instagram App Review (Advanced Access) submission, so the package is
complete the moment the screencast is ready. App: **AWD Social Messaging** (FB app id
`1531544178609736`, Instagram app id `1493427132185394`). Connected IG account:
**@alloywheelsdirect** (`17841440469975661`).

> **⚠️ STATUS (UPDATED 2026-05-30): THIS IS LIKELY NOT NEEDED — retained for reference only.**
> Empirically confirmed 2026-05-30: a **non-Tester** IG account DM'd @alloywheelsdirect → it created an
> open D365 conversation AND the agent's reply was delivered back. So **Standard Access already serves
> real customers both ways** for AWD's first-party use of its own account. Per Meta's Instagram-platform
> docs (`/docs/instagram-platform/overview/` + `…/messaging-api/`), *"if your app only serves your
> Instagram professional account or an account you manage, Standard Access is all your app needs."*
> Advanced Access / App Review / **Tech Provider** are for apps serving accounts you **don't** own/manage
> (SaaS / multi-business). **DO NOT submit App Review and DO NOT become a Tech Provider** unless AWD later
> needs to access OTHER businesses' Instagram accounts. The dashboard funnels "Add to App Review" into an
> irreversible-sounding Tech Provider gate — avoid it. See RESUME.md (session 3).

---

## 1. Permissions to request Advanced Access for

Request Advanced Access **only** for what the integration actually uses (Meta rejects requests for
unused permissions):

| Permission | Request? | Why |
|---|---|---|
| `instagram_business_manage_messages` | **YES — core** | Receive customer DMs (webhook) + send agent replies (Send API). The entire purpose. |
| `instagram_business_basic` | **YES** | Identify the connected professional account and basic context needed to receive/route messages. |
| `instagram_business_manage_comments` | **Only if** we will handle Instagram **comments** in D365 | The current bridge handles **DMs only**, not comments. Leave at Standard unless/until comment-handling is built — otherwise it's an unused permission and risks rejection. |
| `instagram_business_content_publish` / `…manage_insights` | **NO** | Not used by this integration. |

---

## 2. App / use-case description (paste into the overall description)

Alloy Wheels Direct Limited is a UK e-commerce retailer of alloy wheels and tyres
(alloywheelsdirect.net, company no. 05069800). This app powers our **customer-service messaging on
Instagram**: it lets our human customer-service agents receive and reply to customer Instagram Direct
Messages from inside **Microsoft Dynamics 365 Contact Center**, our agent workspace, alongside our
other support channels (web chat, Facebook Messenger, WhatsApp, email and phone).

When a customer sends a Direct Message to our Instagram professional account **@alloywheelsdirect**,
the Instagram `messages` webhook delivers it to our integration service, which relays it into Dynamics
365 Contact Center as a conversation in the agent queue. A **human agent** reads the message and types
a reply; the reply is sent back to the customer via the Instagram Send API. There is no automated
bulk messaging — every message is a 1:1 customer-service interaction initiated by the customer and
answered by a human agent within the standard customer-service window.

---

## 3. Per-permission justification (paste into each permission's "how will you use" field)

**`instagram_business_manage_messages`** (core)
> Used to (a) **receive** customer Direct Messages via the `messages` webhook and surface them to our
> agents in Microsoft Dynamics 365 Contact Center, and (b) **send the agent's reply** back to the
> customer via the Instagram Send API (`graph.instagram.com /me/messages`) within the customer-service
> messaging window. This is two-way human-agent customer support over Instagram DMs — the whole
> purpose of the integration. We do not send unsolicited or automated bulk messages.

**`instagram_business_basic`**
> Used to identify our connected Instagram professional account (@alloywheelsdirect) and read the
> basic account context (account id/username) required to associate inbound conversations with the
> correct business account and route them to the right agent queue in Dynamics 365. We do not use it
> to access any other accounts' data.

---

## 4. Reviewer test instructions (paste into "Steps to test")

> This is a **server-to-server, human-agent** messaging integration — there is no end-user-facing app
> screen for the reviewer to log into; the demonstration is the attached screencast. To reproduce:
>
> 1. From any Instagram account, send a Direct Message to **@alloywheelsdirect** (e.g. "Hello, testing").
> 2. The message is delivered via the `messages` webhook to our integration and appears as a new
>    conversation in our Microsoft Dynamics 365 Contact Center agent workspace (Instagram / Custom
>    Messaging channel, messaging queue).
> 3. A customer-service agent opens the conversation and sends a reply.
> 4. The reply is delivered back to the sender's Instagram inbox from @alloywheelsdirect.
>
> The attached video demonstrates this full inbound → agent → outbound flow end-to-end.

---

## 5. Screencast — what it must show (the recording shot list)

~60–90s, narrated or captioned:
1. Title: "Alloy Wheels Direct — Instagram DMs handled in Microsoft Dynamics 365 Contact Center."
2. **Customer (Instagram):** DM @alloywheelsdirect, e.g. "Hi, do you stock 19″ alloys for a BMW 3 Series?"
3. **Agent (D365):** conversation arrives → agent opens it; customer message visible; **Instagram /
   Custom Messaging channel label visible**.
4. **Agent (D365):** types + sends a reply.
5. **Customer (Instagram):** reply appears in the thread ("Seen").
6. End: "Inbound + outbound, handled by agents — using instagram_business_manage_messages."

Tips: < 2 min; clean test data; show the @alloywheelsdirect name clearly (must match the connected
account); sequential phone → D365 → phone clips are fine (no split-screen needed).

---

## 6. Pre-submission checklist

- [x] App **Published/Live** (done 2026-05-30).
- [x] **Privacy Policy URL** set (`…/information/privacy_policy`).
- [x] **Data deletion** instructions URL set (`…/privacy_policy#data-deletion`, §13).
- [x] App **Category** set (Messaging).
- [ ] **Business / access verification** — the dashboard's **"Become a Tech Provider"** prompt confirms
      App Review submission requires **access verification**. AWD's use is **first-party** (managing its
      own @alloywheelsdirect account), so this should be **Business Verification of the AWD business
      portfolio** (business_id `605826456988689`), NOT full Tech-Provider verification (that's for
      accessing *other* businesses' data). **Can take several days — start in parallel with the video.**
      Check at business.facebook.com → Security Centre / Business verification.
- [x] **No blocking "Required actions"** on the app dashboard (confirmed 2026-05-30). The "1 unread"
      Alert-Inbox item is a notification, not a blocker.
- [ ] **Screencast** recorded (Chris — in progress).
- [ ] Platform Terms / Developer Policies accepted (usually auto on app create).

---

## 7. Notes

- This is a **dedicated IG app**, separate from the Facebook Messenger app — its App Review clock is
  **independent** of the in-flight FB `pages_messaging` review. Submitting this does **not** wait behind FB.
- Without Advanced Access, the integration works **only for accounts with a role on the app**
  (admins/developers/**Testers**). Today's round-trip used Tester `chris_murray_`. Advanced Access lifts
  that to the general public.
- Review typically takes a few days to ~2 weeks. No code/config change is needed when it's granted —
  Advanced Access simply widens who can message the account.
