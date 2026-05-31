# Secret rotation runbook — IG → D365 bridge (`awd-ig-bridge`)

All of the bridge's secrets live in Key Vault **`awd-ig-bridge-kv`** (UK South), read via the App Service
**system-assigned managed identity** `b3429ddd-895d-4cc3-9ade-21313ecfb644` (access-policy mode; secret
**get/set/list** granted). The bridge falls back to the matching App Service app setting only when
`KeyVault__Uri` is unset (local/dev) — in production Key Vault is authoritative.

| Secret | KV secret name | Seed app setting | Consumed by | Auto-expires? |
|---|---|---|---|---|
| Instagram-user token | `IgUserAccessToken` | `InstagramAdapterSettings__PageAccessToken` | outbound Send API | **Yes** — ~60d; **auto-refreshed** (P1), no manual rotation |
| Meta app secret | `MetaAppSecret` | `InstagramAdapterSettings__AppSecret` | inbound `X-Hub-Signature-256` validation | No |
| Direct Line secret | `DirectLineSecret` | `RelayProcessorSettings__DirectLineSecret` | Direct Line relay/poller | No |

> The Instagram-user token (P1) slides itself forward indefinitely and needs **no** manual rotation — only
> re-mint it if it is ever fully revoked/expired (code `190`). This runbook covers the two **P5** secrets
> (`MetaAppSecret`, `DirectLineSecret`), which do **not** auto-expire.

## How loading works (why a restart is required to rotate)

Both P5 secrets are **loaded once at startup and cached in memory** (no background refresher — they don't
expire). So **writing a new value to Key Vault does not take effect until the App Service restarts.** A
restart is therefore *required* and *sufficient* to pick up a rotated value.

- **`MetaAppSecret`** — loaded lazily on the first inbound webhook by `AppSecretProvider`, then cached.
- **`DirectLineSecret`** — warmed at host start (`Program.Main`, before the host runs) by
  `KeyVaultDirectLineSecretProvider`, then served synchronously to the eager Direct Line gateway.

**Auto-seed:** on first boot with Key Vault configured, if a KV secret is missing the bridge seeds it from the
matching plaintext app setting and writes it to KV (best-effort; a write failure is logged but does not block
the boot — the in-memory seed still serves, and the next restart re-seeds). Once the KV secret exists, **KV is
authoritative forever** — the app setting is never read again, and the seed path never re-runs.

---

## First-time cutover to Key Vault (do once)

1. **Confirm the MI role.** The MI already has secret get/set/list on `awd-ig-bridge-kv` (P1). The **set**
   permission is needed for the first-boot auto-seed; **get** thereafter.
2. **Deploy** the P5 build with the plaintext app settings still present
   (`InstagramAdapterSettings__AppSecret`, `RelayProcessorSettings__DirectLineSecret`) and `KeyVault__Uri` set
   — the bridge seeds `MetaAppSecret` + `DirectLineSecret` into KV on boot.
3. **Verify the seed landed** (no secret values are exposed):
   ```
   GET https://awd-ig-bridge.azurewebsites.net/api/TokenHealth?token=<VerifyToken>
   ```
   Expect `appSecretStoreType: KeyVaultAppSecretStore`, `directLineSecretProviderType:
   KeyVaultDirectLineSecretProvider`, `appSecretStoreHasValue: true`, `directLineSecretHasValue: true`, and
   matching non-zero lengths. Cross-check with `az keyvault secret list --vault-name awd-ig-bridge-kv`.
4. **Prove the round-trip still works** — a real IG DM lands in D365 (inbound signature validates against
   `MetaAppSecret`) and an agent reply reaches Instagram (relay uses `DirectLineSecret`).
5. **Delete the plaintext app settings** so KV is the sole source (keep them one verified cycle as a safety
   net — the P1 playbook — then remove):
   ```
   az webapp config appsettings delete -n awd-ig-bridge -g awd-contactcenter-rg \
     --setting-names InstagramAdapterSettings__AppSecret RelayProcessorSettings__DirectLineSecret
   ```
   ⚠️ Delete only **after** step 3 confirms the seed persisted. Deleting before the seed lands leaves neither
   KV nor config populated → inbound 403s / the relay cannot start a conversation (recoverable by re-adding the
   app setting or `az keyvault secret set`).

---

## Rotating `MetaAppSecret` (Meta app secret)

A bad/stale value fails **loud**: every inbound webhook returns **403** (signature mismatch) — the only 403
path in the controller — visible as a spike in the App Insights `Http403`/`requests` metrics. There is no
partial/degraded mode.

1. **Meta App Dashboard** → the dedicated Instagram app (`1493427132185394`) → **App settings → Basic → App
   secret → Reset**. Copy the new value (shown once). *(Note: resetting invalidates the old secret immediately —
   inbound webhooks will 403 until the new value is live, so do this in a short maintenance window.)*
2. **Write to Key Vault** (avoid shell-history echo — use the portal or a value file):
   ```
   az keyvault secret set --vault-name awd-ig-bridge-kv --name MetaAppSecret --value "<NEW>"
   ```
3. **Restart** so the cache reloads:
   ```
   az webapp restart -n awd-ig-bridge -g awd-contactcenter-rg
   ```
4. **Verify:** send a real DM to `@alloywheelsdirect` (or a correctly IG-secret-signed synthetic webhook) →
   expect HTTP **200** and a D365 conversation. `/api/TokenHealth` should show `appSecretStoreHasValue: true`
   with the new length. If still 403, the new value didn't reach KV/cache — re-check steps 2–3.

---

## Rotating `DirectLineSecret` (Azure Bot Direct Line channel)

A bad/stale value fails **loud**: `StartConversationAsync` throws *"Direct Line StartConversation returned no
conversation. Verify the Direct Line secret"* — logged to App Insights, no `Conversations` row advances. The
provider does **not** silently fall back to the plaintext app setting once KV is authoritative.

1. **Azure Portal** → Azure Bot **`awd-instagram-bot`** → **Channels → Direct Line → Edit site →
   regenerate key** (Direct Line sites expose two keys; regenerating invalidates the old key immediately, so do
   this in a maintenance window — single-key rotation, brief relay outage until step 3 completes). Copy the key.
2. **Write to Key Vault:**
   ```
   az keyvault secret set --vault-name awd-ig-bridge-kv --name DirectLineSecret --value "<NEW>"
   ```
3. **Restart:**
   ```
   az webapp restart -n awd-ig-bridge -g awd-contactcenter-rg
   ```
   On boot, `Program.Main` warms the new value; a bad/missing value logs **Critical** and the gateway fails to
   build (the bridge won't serve until corrected — never silent).
4. **Verify:** a real IG DM creates a `Conversations` row and reaches the D365 agent workspace, and an agent
   reply is relayed back to Instagram. `/api/TokenHealth` should show `directLineSecretHasValue: true` with the
   new length.

> Zero-downtime variant (optional, not implemented): rotate the **unused** Direct Line site key, write+restart,
> then regenerate the other on the next cycle. The single-key flow above is fine for the B1 single instance.

---

## Deploy gotchas (don't relearn)

- Build the deploy zip with **Python `zipfile` + forward-slash arcnames** (GNU `tar` can't write a usable zip;
  a PowerShell-built zip has backslash paths → the Linux App Service rsync fails). Verify ~60 entries, no `\`.
  Deploy: `dotnet publish Microsoft.OmniChannel.Adaptors.Service -c Release -o publish` → zip → `az webapp
  deploy -n awd-ig-bridge -g awd-contactcenter-rg --src-path publish.zip --type zip`.
- The **first `az webapp deploy` may 502** on SCM cold-start → just retry.
- `az monitor app-insights query -o table` renders empty → use **`-o json`**. App Insights filters to
  Warning+, so `Information` startup logs (e.g. the seed log) won't appear — confirm via `/api/TokenHealth`.
- Keep the App Service at **one B1 instance** (the secret caches + the P3 conversation store assume single-instance).
