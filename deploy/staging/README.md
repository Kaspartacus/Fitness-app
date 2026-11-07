# Manual Fitness App staging on Dev

Phase 4 is repository implementation only. It does not provision Dev, grant Docker
access, install certificates or activate staging. Production deployment, GHCR and
Cloudflare are Phase 5 and are not implemented. Dev must never merge a feature
branch into main or bypass GitHub PR review and human approval. The existing
dedicated clean main checkout may continue fast-forward synchronization to
already-approved `origin/main`; leave its enabled host timer unchanged.

## Boundaries and commissioning blockers

- Only a clean, committed, checked-out `feature/*` branch is eligible for staging.
  No deployment follows a push/commit automatically. Detached HEAD, main and
  caller-selected Docker/Compose targets are rejected. Scripts never merge/pull.
- The only target is the local Dev Docker socket; project `fitness-app-staging`.
  No Apps VM address, production SSH key, registry credential or deployment code
  is present. Branch checks protect against mistakes, not a root-equivalent actor.
- **Every mutation, including build, start, stop, backup and recovery, needs human
  approval.** Repository approval is not approval to run Docker. Status/logs are
  read-only but still depend on existing sandbox access and may need redaction.
- The current environment cannot reach the daemon and uses automatic approval
  review. Mandatory human approval is **not commissioned**. Commands fail closed
  until an administrator separately provisions the external boundary below.
- No automatic deployment timers, boot scripts, Docker group changes, firewall
  rules, trust-store changes or administrator installers are included.

## External approval integration contract

The CLI sends a JSON envelope (`action`, `intent`, `repository`) to the fixed executable
`/usr/local/libexec/fitness-staging-approved-exec`. That executable does not exist
in this repository and is not installed by these changes. An approved administrator
must supply/verify it, or choose another supported equivalent execution setup.
This contract is an explicit activation dependency, not a claim of enforcement by
the repository itself.

The unprivileged CLI reads only public Git/tooling bytes. It never reads private
configuration, PFXs, state or backup manifests. For action `prepare`, the broker
runs its immutable runner's root-only `_broker-prepare` with the public intent on
stdin and returns the resulting sanitized full plan (fingerprints, never secret
values). For action `inspect`, it runs `_broker-inspect` with a status/logs intent;
this has no Docker mutations or human approval requirement. For action `execute`,
it prepares the full plan, displays it for human approval, and only then passes
that approved full plan to `_broker-worker`. Read-only preparation/inspection must
also use immutable trusted tooling and enforce the fixed Dev target. The broker
must redact logs if necessary and never return private configuration or tool errors.

The broker must:

1. Be administrator-owned, non-writable by Codex, with protected parent directories.
   Its privileged planner/inspector/worker must use a previously administrator-approved
   immutable runner revision, with an administrator-controlled digest allowlist.
   Never execute newly supplied agent-writable Python code as root merely to prepare
   a plan. Unknown tooling hashes fail closed and require separate human review and
   activation before any privileged preparation. All root-executed helpers, including
   read-only helpers, belong to that approved release.
   Keep *all* direct Docker/socket/TCP/API access and independent root/sudo authority
   blocked from the agent. Do not grant unrestricted group access and then claim a
   wrapper enforces approval. Configure eligible harness approvals to the human
   user, not auto-review, and test enforcement across shell/Python/SDK clients.
2. Copy approved tooling (runner, Dockerfile, ignore file and Compose), exact Git
   snapshot and configuration into immutable administrator-controlled storage
   before the decision. Verify their hashes against the plan. **Never copy caller
   `.git/config`, hooks, index, alternates, replace refs, linked-worktree metadata,
   Git environment variables or executable filter/fsmonitor settings into privileged
   Git execution.** While still unprivileged, snapshot the source object bytes and
   validated branch/commit descriptor. Supply only `broker-source/source.json`
   (`branch`, `commit`) and `broker-source/objects` in the immutable runner directory.
   Objects may be loose SHA-1 objects or regular pack/idx/rev files; no symlinks,
   hard-link aliases to mutable user files, alternates or object-store metadata.
   Files/directories must be root-owned and agent-nonwritable before privileged
   preparation. The runner constructs a fresh bare repository with canonical config,
   one approved ref, no hooks/filters, isolated Git environment, disabled lazy fetching
   and protocols, and verifies objects with fsck before archiving. It never executes
   caller Git status or reads caller configuration as root. Never check a
   hash then execute the original agent-writable file (TOCTOU). Reject stale state,
   changed config, changed runner/source or different target; obtain fresh approval.
3. Display operation, commit, configuration fingerprints, storage, image/state and
   affected project/container/network to the human through an external trusted
   channel. Approval authorizes this operation's build, quiesce, paired backup,
   migration, recreate, health check and, if needed, paired recovery. The complete
   transaction must be visible before approval. Do not treat a JSON boolean, env
   variable, CLI flag or editable receipt as authorization. Require a new decision
   for each invocation; no reusable blanket Docker allow-rule.
4. After approval only, run its immutable runner with isolated Python (`python3 -I`),
   a sanitized environment and fixed working directory, as the authority that can
   reach the daemon. Invoke `_broker-worker`, passing the approved `plan` JSON on
   stdin. Do not use shell interpolation or attacker-controlled imports/PATH. The
   worker's root check is defense in depth, **not evidence of human approval**.
   Its plan is revalidated under an operation lock before mutation.
5. Return the result and retain a protected audit record. Confirm cancellation,
   denial and absence of approval all prevent operations. A modified plan must not
   inherit approval. Test these properties before enabling any real deployment.

Do not disable the sandbox or select a never-ask/full-access profile. If the desktop
or remote execution harness cannot establish this boundary, leave staging blocked.
Full root-equivalent Docker access and mandatory human approval cannot be guaranteed
by AGENTS.md, executable-name rules or writable scripts alone.

## Approved host preparation, performed separately

Before asking to activate staging, review the exact required changes with the owner:

- Docker Engine/Compose availability and boot enablement; actual socket authorization
  under the real host identity (namespace-rendered owner/group is not sufficient).
- `/var/lib/fitness-app-staging`, its `releases` and `failed` subdirectories, and
  `/var/backups/fitness-app-staging`: root-owned private storage, protected ancestors.
  Create dedicated `data` and `keys` subdirectories owned by UID/GID **1654:1654**,
  mode 0700, for the non-root runtime. Bind mounts deliberately refuse auto-creation.
  The backup root is 0700; backup manifests and deployment state are private.
- `/etc/fitness-app-staging`, root-owned and protected. Store separate `settings.json`
  and `server.pfx` here, readable by the provisioning authority. Start with
  `settings.example.json`; replace invalid placeholders privately. Runtime snapshots
  are root:1654 mode 0640, with a 0750 config directory. No secrets enter command
  arguments, Docker environment inspection, Git, build context or deployment plans.
- A staging-only signing key, staging-only certificate password and no production
  secrets. The complete config snapshot is retained outside Git for paired recovery;
  plans contain fingerprints, never values. Restrict host access to these files.
- A root-owned `target.json` in that configuration directory must record
  `{"role":"Dev-VM102","machine_id":"<actual Dev /etc/machine-id>"}`. The worker
  checks that identity and the locally assigned `192.168.1.193` interface address
  before any Docker mutation. Never provision that identity on Apps. The descriptor's
  fingerprint is bound to the approved plan; machine identifiers need not be pasted
  into chat or committed.
- Review RAM/disk headroom before a build: the runtime is capped at 1 CPU, 768 MiB,
  128 PIDs, 32 MiB `/tmp` and three 10 MiB Docker logs. SDK build memory/cache and
  persistent database/mail/backups are **not** bounded by those limits. Stop before
  low space; retain backups/images until explicitly approved for cleanup. No prune.
- Inspect Docker/firewall/router behavior and existing Tailscale gateway routes.
  Only `192.168.1.193:8443` is published; do not forward a public port. The container's
  HTTP health listener is bound to its own loopback and is not published.

### Trusted HTTPS on Mac and iPhone

Use `https://192.168.1.193:8443` on LAN and through the existing gateway host route.
Separately approve creation of a dedicated private staging CA and a server
certificate with IP SAN `192.168.1.193`, server-auth usage and sensible expiry.
Keep the CA private key offline, never mounted into the application. Export the
password-protected server PFX to the protected configuration location. The repository
does not create a CA, copy keys, install trust or change DNS/networking.

Install **only the CA certificate** on Mac/iPhone using the device owner's trusted
channel. On Mac, explicitly trust that CA for SSL in Keychain. On iPhone, install
the profile, then enable its full trust under Settings → General → About →
Certificate Trust Settings. Follow [Apple's certificate trust instructions](https://support.apple.com/en-us/102390).
Do not use `curl -k`, ignore browser warnings, install a CA private key or reuse a
localhost .NET development certificate. Verify certificate identity/expiry, Safari
login/refresh/cookies and LAN/Tailscale access on **both actual devices** before
claiming HTTPS works there. Certificate renewal/trust removal remains an operator task.

## Commands through Codex

Run from the isolated feature checkout containing this tooling:

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

`plan` is read-only. A deployment plan needs a clean committed source branch and
provisioned config. `deploy` requests a fresh human decision; it cannot currently
activate anything without the broker. Source must include the staging command and
settings support; older branches are rejected. The reviewed deployment tooling is
used alongside the exact selected source commit and its immutable image ID is
recorded. Review tooling/config fingerprints as well as the commit; a tag alone is
not a complete artifact identity.

Codex should translate natural language to this interface, display the plan, ask
approval and report branch, full commit, immutable image and readiness result.
Read-only plans should also describe persistent storage and possible recovery.
Never run any `_broker-*` entry point directly from Codex or bypass a missing/denied broker.
Never run a manual `docker run`/Compose command without a fresh human approval.

`stop` preserves the container, database, keys and config. The `unless-stopped`
policy keeps active staging running across crashes and daemon/VM restarts, and
keeps a deliberately stopped container stopped after reboot. Start reuses that
container. A deploy explicitly activates a selected new version. No command deletes
volumes or resets data. Health failure alone does not make Docker restart a live
process; health verification gates updates and stops a failed candidate. See
[Docker restart policy](https://docs.docker.com/engine/containers/start-containers-automatically/).

## Update, backups and recovery

Operations serialize on a host lock. Deployment builds before stopping the current
application, then stops writers and verifies a paired backup of **all database/WAL
files and the complete key ring**. The manifest records hashes and the old immutable
image, branch/commit, exact config snapshot and previous running/stopped state.
Missing/corrupt backups prevent migration. The migration command uses the candidate
image without HTTP or hosted workers, then starts the app and waits for readiness.

On migration/start/health failure the candidate is stopped, failed data/keys are
preserved under `failed`, the matching database/key backup is restored together,
and the previous image/config is recreated. A previously stopped deployment remains
stopped. Never put an old image against a newly migrated schema. Empty first-deploy
recovery restores empty storage and removes only the failed staging container.
Failed or partial recovery leaves staging stopped and reports the backup ID.

`pending.json` records interruption recovery context. A hard interruption or reboot
can leave staging stopped; subsequent start/deploy/backup refuses until explicit
recovery/inspection. A pending record without a completed backup requires operator
inspection before any further change. No automatic reset of uncertain state.
During paired restore, `restoring.json` records the backup ID and preserved failed
state directory before live renames. If interrupted between renames (even with a
temporarily absent live `data` or `keys` directory), approve `recover` again with
**the same recorded backup ID**. It verifies/resumes staged replacements and checks
both trees before recreating any container. Do not manually create empty missing
directories, substitute a different backup, delete the journal or start the app.
The journal remains until compatible container recreation/removal and required
readiness succeed, including standalone recovery with no deployment pending record.
Recreation/readiness failure blocks ordinary start/restart. Approve recovery again
with the same backup ID; any writes from an earlier recovery attempt are preserved
as another failed-state snapshot before restoring the approved pair again.
Backup/restore code is tested with synthetic files and mocked Docker; real SQLite
restore, encrypted diary readability, ownership and reboot behavior still require
an approved commissioning exercise. Current backups are local, not disaster recovery.
Define an encrypted off-host copy/retention policy separately; no backup deletion or
off-host upload is automated.

Staging defaults to private email pickup files under persistent `data/mail`; they
can contain reset links, so never publish or paste them into logs. External SMTP
is forcibly replaced with Pickup unless `Staging:AllowExternalEmail` is explicitly
true. Web Push configuration is cleared unless `Staging:AllowWebPush` is explicitly
true. Any controlled external test also needs valid staging credentials and
explicit owner authorization for delivery. Do not import real users or live push
subscriptions. Delete test mail only with an approved data-handling operation.

## Verification and Phase 5

Repository checks: `./scripts/verify.sh verify`,
`node --test tests/FitnessApp.IntegrationTests/*.test.cjs`, and
`python3 -m unittest discover -s tests -p staging_test.py` (also included in Verify).
No Docker invocation is needed by these tests. Container build, Compose execution,
non-root mounts, migration/restore exercise, network exposure, human gate denial,
Mac/iPhone trust and actual reboot tests require separate commissioning approval.

Future production is a separate pipeline: feature → PR → human review and required
tests → approved GitHub merge into main → GitHub Actions verification → GHCR immutable
commit/digest image → Apps VM101 health-verified deployment. Only main provenance
is eligible. Use a protected environment and least-privilege registry/deployment
credentials, separate database/keys/config/secrets, and paired recovery appropriate
to the migration. Cloudflare is a later separate access decision. No production
workflow, publish job or production credential is created here.
