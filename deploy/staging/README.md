# Manual Fitness App staging on Dev

Phase 4 provides repository implementation only. No Docker permission, host,
certificate, firewall or container changes are activated by these files. Production,
GHCR publishing and Cloudflare remain Phase 5 design only.

## Access and approval

The owner requests **full Docker access for Codex on Dev**, accepts that it is
root-equivalent, and does not want a separate broker. The script runs directly as
`codex` (UID/GID 1001), never as root or through sudo. Docker-group membership and
execution-environment socket access still require separately reviewed administrator
changes. An administrator can perform the one-time setup; do not change permissions
or weaken security controls silently. No installer or privilege-changing command
is run by the staging tooling.

**Codex must show the exact operation, branch/commit and affected resources and
ask the human for approval before every Docker mutation**, including builds,
deploy/update, start, stop, restart, backup, recovery and deletion. Read-only
plans/status/log inspection need no further approval. Repository implementation
approval is not approval to deploy. Do not run a raw Docker command to bypass a
refused or unapproved operation.

Mutating commands print their plan and require a terminal response containing the
operation and plan fingerprint. There is no `--yes`, `--approved` or environment
shortcut. After receiving the human's chat approval, Codex may complete that
terminal confirmation and verify the result. The fingerprint binds the exact
plan; state/config/source changes trigger refusal and require fresh approval.
A terminal can be controlled by the agent: **this confirmation is an operator
safety check, not technical enforcement against a full-Docker user**. Neither
AGENTS.md nor a script can constrain that user's root-equivalent authority.
Use human-reviewed execution approvals when supported by the actual Codex harness;
automatic approval review does not itself constitute the owner's human approval.

## Strict staging and production separation

- Only a clean, committed, checked-out `feature/*` branch may deploy to staging.
  Main, detached HEAD, dirty source and remote Docker/Compose target overrides are
  rejected. No push/commit watcher, automatic deployment or Git merge/pull exists.
- Target is the local Dev host `dev` with assigned address `192.168.1.193`, fixed
  socket `unix:///var/run/docker.sock`, project `fitness-app-staging`, and fixed
  user-private paths. Docker environment overrides/proxies are discarded; an
  isolated staging client-config path avoids implicit production contexts.
- Apps VM and production secrets/credentials are absent from this tooling. The
  checks prevent accidental targeting, not deliberate bypass by a privileged user.
- Never merge feature branches into main on Dev or bypass GitHub PR review and
  human approval. The existing dedicated clean main checkout may continue its
  enabled fast-forward synchronization to already-approved `origin/main`; leave
  that host timer/configuration unchanged. Staging scripts never update main.

## Storage and runtime

The fixed host root is `/home/codex/.local/share/fitness-app-staging`, outside Git.
Its `data`, `keys`, `config`, `backups`, `releases` and `failed` directories are
owned by codex and private (0700). State, journals, manifests and secret snapshots
are private (0600). Existing ancestors must not redirect through symlinks or allow
other users to write. These directories/configuration are provisioned only during
separately approved commissioning; the scripts refuse unsafe/missing storage.

One non-root application container hosts the API and Blazor WebAssembly client.
SQLite needs no database service. Compose explicitly uses UID/GID **1001:1001**,
matching codex so private bind mounts and paired backup/restore need no root helper.
The image's default non-root UID remains 1654 for other explicitly configured uses;
never run staging as root. Data is mounted at `/var/lib/fitness-staging/data`, and
keys at `/var/lib/fitness-staging/keys`; neither is in public assets/build context.

`config/settings.json` contains a separate staging signing key and certificate
password; use `settings.example.json` as an invalid-placeholder template and replace
values privately. `config/server.pfx` contains only the server certificate/private
key, not the CA private key. Each release retains a mode-0600 configuration/PFX
snapshot for recovery. Plans contain fingerprints and paths, never secret values.
Secrets are not Docker environment variables, command arguments or Git files.
Do not print configuration, passwords, account data or reset links into chat/logs.

Resource limits: 1 CPU, 768 MiB RAM, 128 PIDs, read-only root filesystem, dropped
capabilities, no-new-privileges, 32 MiB temporary storage, and three 10 MiB Docker
logs. SDK builds, image caches, database/mail and backups are not covered by the
runtime memory/disk limits. Deploy preflights at least 2 GiB free under the state
root and 512 MiB in temporary storage; separately assess actual image/backup size
and Dev's roughly 4 GiB RAM before approval. No automatic prune or backup deletion.

## Commands through Codex

From the selected feature checkout containing the reviewed tooling:

```bash
python3 scripts/staging.py plan --branch feature/example
python3 scripts/staging.py deploy --branch feature/example
python3 scripts/staging.py status
python3 scripts/staging.py logs
python3 scripts/staging.py plan --operation stop
python3 scripts/staging.py stop
python3 scripts/staging.py start
python3 scripts/staging.py restart
python3 scripts/staging.py backup
python3 scripts/staging.py plan --operation recover --backup BACKUP_ID
python3 scripts/staging.py recover --backup BACKUP_ID
```

`plan`, `status` and bounded `logs` are read-only. Deployment plans need committed
source and provisioned config. Review the entire transaction: build, quiesce,
paired backup, explicit migrations, recreate, health checks and recovery if needed.
The plan names the fixed container/network, proposed image, storage and exact
commit. Re-run planning and obtain fresh approval when a requested target changes.
The selected feature must include staging command/settings support; older branches
are rejected. Reviewed deployment tooling and its fingerprints are part of the
plan alongside the exact source archive/commit. The built immutable image ID is
recorded; a mutable tag alone is not the deployed artifact identity.

## Lifecycle, health and recovery

`unless-stopped` keeps active staging running across application crashes and Docker/
VM restarts; deliberate stop stays stopped after reboot. `stop` retains the
container, database, keys and configuration. `start` reuses the recorded container.
Deploy explicitly activates the selected feature version. No boot timer recreates
stopped staging and no command resets data or deletes volumes. See
[Docker restart policies](https://docs.docker.com/engine/containers/start-containers-automatically/).

Staging-only live/ready checks return constant responses without private details.
Readiness requires database access and no pending migrations. The container's HTTP
health listener is bound only to its loopback and is not published. Docker health
alone does not restart a still-running unhealthy process; deployment waits for
readiness and stops a failing candidate.

Operations serialize under a host lock. Deploy builds before stopping the old
application, then stops writers and verifies a paired backup of all database/WAL
files plus the complete Data Protection key ring. Its manifest records hashes,
previous image/branch/commit/config and running/stopped state. Backup failure
prevents migration. A one-shot command in the candidate image applies migrations
without starting HTTP or notification workers; ordinary Staging startup never
applies migrations implicitly.

Failure stops the candidate, preserves failed data/keys, restores the matching
backup pair, and recreates the previous immutable image/config. Never assume an
old image can safely use a newly migrated database. Previously stopped staging
remains stopped. Empty first-deploy recovery restores empty storage and removes
only that failed staging container. Approval for this transaction must include
these possible recovery actions; unrelated deletion is never authorized implicitly.

`pending.json` records interrupted deployments. `restoring.json` protects paired
restore until compatible container recreation/removal and required readiness both
succeed. Start/restart/deploy remain blocked while recovery is unresolved. Approve
`recover` again with the **same recorded backup ID** after an interrupted restore,
even if a live data/key directory is temporarily absent. Replacements are verified
and resumed safely; failed state is preserved. If a previous readiness attempt
wrote data, it is preserved as another failed snapshot before restoring the approved
pair again. Do not remove journals, substitute a different backup or create empty
missing directories. A pending update without a complete backup requires explicit
operator inspection rather than automatic reset.

Backups are local, not disaster recovery. Define an encrypted off-host copy and
retention policy separately. Real SQLite/encrypted-diary restoration, permissions,
reboot behavior and container networking require an approved commissioning exercise;
synthetic transaction tests do not establish those runtime results.

## Private HTTPS and safe external effects

Only `192.168.1.193:8443` is published. Use `https://192.168.1.193:8443` over LAN or
the existing Tailscale gateway host route. Review Docker/firewall/router behavior
before activation; no public forwarding, Cloudflare or Tailscale installation is
performed by this phase.

Separately approve a dedicated private staging CA and a server certificate with IP
SAN `192.168.1.193`, server-auth usage and sensible expiry. Keep the CA private key
offline and outside application mounts. Place only the password-protected server
PFX in private config. No CA creation, key copying, trust installation or DNS/
firewall changes are performed by repository implementation.

Install only the CA certificate through the owner's trusted channel. Trust it for
SSL in Mac Keychain; on iPhone, install the profile and enable full trust under
Settings → General → About → Certificate Trust Settings. Follow
[Apple's certificate trust instructions](https://support.apple.com/en-us/102390).
Never bypass TLS warnings, use `curl -k` or reuse a localhost development certificate.
Verify Safari login/refresh/cookies and LAN/Tailscale access on both actual devices
before claiming HTTPS works there. Renewal/trust removal remains an operator task.

Email defaults to private pickup files under persistent `data/mail`, which may
contain reset links and must never be published. External SMTP is forcibly
replaced with Pickup unless `Staging:AllowExternalEmail` is explicitly true. Push
configuration is cleared unless `Staging:AllowWebPush` is explicitly true. Controlled
external tests also need valid staging credentials and explicit owner approval for
delivery. Do not import real users/live push subscriptions. Production defaults
are not silently changed.

## Verification and Phase 5

Checks: `./scripts/verify.sh verify`,
`node --test tests/FitnessApp.IntegrationTests/*.test.cjs`, and
`python3 -m unittest discover -s tests -p staging_test.py` (included in Verify).
No Docker invocation is needed by these tests. Actual Docker build/Compose, mount
permissions, migration/restore, crash/reboot, private exposure and device trust
checks require separately approved commissioning.

Future production: feature → PR → human review and required tests → approved
GitHub merge into main → GitHub Actions verification → GHCR immutable commit/digest
image → Apps VM101 health-verified deployment. Only main provenance is eligible.
Use protected environments, least-privilege registry/deployment credentials,
separate database/keys/config/secrets and paired recovery. Cloudflare is a later
access decision. No production workflow, registry publishing or production
credential is created in Phase 4.
