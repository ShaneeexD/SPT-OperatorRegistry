# Privacy Disclosure - SPT-OperatorRegistry

SPT-OperatorRegistry is a community identity system. This document explains exactly what
data is collected, how it is stored, who can read it, and how to opt out.

## Summary

When you launch SPT with this mod installed, it anonymously contributes your PMC nickname
and level to a community registry so that other players can see real community operators
as PMC bot names in their raids. **No gameplay, inventory, location, or identifying account
data is ever collected.**

## What we collect

The mod sends the following to Firebase Realtime Database on startup:

| Field | Example | Purpose |
|---|---|---|
| `installationId` | `4f0d67b2a1c84e...` | Anonymous, persistent UUID. Generated once locally. **Not** your profile id, account id, or Discord id. |
| `nickname` | `Serenity` | Your PMC's displayed nickname. Sanitized and length-validated. |
| `level` | `43` | Your PMC's displayed level. Clamped to 1-79. |
| `sptVersion` | `4.0.13` | The SPT version you run. |
| `modVersion` | `1.0.0` | The mod version you run. |
| `firstSeen` | `1785500000` | Unix timestamp of first registration (set once, preserved). |
| `lastSeen` | `1785500000` | Unix timestamp of last launch (updated every launch). |

## What we NEVER collect

- IP addresses
- Discord IDs
- SPT profile IDs / account IDs
- Stash or inventory contents
- Equipment / loadouts
- Location / map / raid data
- Progression / quest / skill data
- Any gameplay telemetry

The `installationId` is a random UUID generated locally in the mod's config folder
(`config/installation_id.json`). It is not derived from any account or hardware
identifier. It survives profile changes, nickname changes, and level changes, and is never
regenerated. You can delete it to get a new one (see Opting Out).

## Where data is stored

- **Firebase Realtime Database** (`operators/{installationId}`) is the source of truth for
  registrations. Clients **only write** to it; they never read from it.
- **Oracle VM JSON cache** (`operators_cache.json`) is generated every 5 minutes by a
  Python cron job and served over HTTP. It contains only `{nickname, level}` per operator
  - no installation IDs, no timestamps, no versions are exposed publicly.
- **Your local machine** stores `config/installation_id.json` (your UUID) and
  `config/operator_cache.json` (the downloaded community cache used during raids).

## Who can read what

- **You** can read the public VM cache (nicknames + levels only).
- **The VM cron job** reads the full RTDB via the Firebase Admin SDK (server-side only).
- **Other players** can only read the public VM cache. They cannot read your installation
  ID, your `firstSeen`/`lastSeen`, or any other field - those never leave the database.
- **Clients cannot read Firebase RTDB directly.** The RTDB security rules forbid all
  client reads. Only validated, anonymous-auth writes to your own record are permitted.

## Data retention

The VM cron job removes operators whose `lastSeen` is older than **90 days**. If you stop
playing, your entry is automatically deleted after 90 days of inactivity.

## Data validation

Before upload, the mod rejects invalid data:

- Nickname: stripped of invalid characters, must be 3-32 characters.
- Level: must be an integer between 1 and 79.

Invalid data is not uploaded.

## Network behaviour

- **On startup / profile load**: one registration upload to Firebase + one cache download
  from the VM (if `cacheUrl` is set). Registration is throttled to at most once per 5 minutes.
- **During raids**: **no network calls at all.** No Firebase, no API, no HTTP. Only the
  local `operator_cache.json` file is read.
- The mod degrades gracefully: if Firebase or the VM is unreachable, registration/cache
  refresh is skipped and raids continue using whatever local cache exists.

## Opting out

- **Disable the mod**: set `"enabled": false` in `config/config.json`. This turns off both
  registration and bot replacement.
- **Remove the mod**: delete the mod folder. Your existing registry entry expires and is
  removed automatically after 90 days.
- **Get a new installation ID**: delete `config/installation_id.json`. A new UUID is
  generated on next launch (this creates a new registry entry; the old one still expires
  after 90 days).
- **Delete your entry immediately**: contact the mod author, or if you run your own VM with
  the Admin SDK, delete `operators/{yourInstallationId}` directly.

## Future compatibility

The database design is intentionally expandable. Possible future features (not implemented
yet) include operator statistics, encounters, community counters, and The Quartermaster
integration. Any future feature will continue to avoid collecting the data listed in the
"NEVER collect" section above.

## Contact

For privacy questions or data deletion requests, contact the mod author via the SPT Hub
release page.
