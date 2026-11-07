#!/usr/bin/env python3
"""Manual Dev staging. Mutations require an external human-approval broker.

The private worker is not an approval mechanism: root authority and Docker access
must be withheld from the agent outside that broker. See deploy/staging/README.md.
"""
import argparse
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import time
import uuid

PROJECT = "fitness-app-staging"
ROOT = Path("/var/lib/fitness-app-staging")
BACKUPS = Path("/var/backups/fitness-app-staging")
CONFIG = Path("/etc/fitness-app-staging")
BROKER = Path("/usr/local/libexec/fitness-staging-approved-exec")
SOCKET = "unix:///var/run/docker.sock"
REPO = Path(__file__).resolve().parent.parent
MUTATIONS = {"deploy", "start", "stop", "restart", "backup", "recover"}


class Refused(Exception):
    pass


def run(argv, *, cwd=None, data=None, env=None):
    result = subprocess.run(argv, cwd=cwd, input=data, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, env=env, check=False)
    if result.returncode:
        # Tool output may contain source/configuration/private data. Do not echo it.
        raise Refused(f"{Path(argv[0]).name} failed (exit {result.returncode}); staging may remain stopped")
    return result.stdout


def git(*args):
    if os.geteuid() == 0:
        raise Refused("Privileged Git must never open caller repository metadata")
    return run(["git", "-C", str(REPO), *args])


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def source(branch):
    if not re.fullmatch(r"feature/[A-Za-z0-9][A-Za-z0-9._/-]*", branch) or ".." in branch:
        raise Refused("Only named feature/* branches are eligible for Dev staging")
    if os.geteuid() == 0:
        commit, archive = privileged_archive(branch)
        return {"branch": branch, "commit": commit, "archive_sha256": sha(archive)}
    if git("symbolic-ref", "--quiet", "--short", "HEAD").decode().strip() != branch:
        raise Refused("Requested branch must be checked out; detached HEAD is ineligible")
    if git("status", "--porcelain", "--untracked-files=normal").strip():
        raise Refused("Source must be clean and committed before planning a deployment")
    commit = git("rev-parse", "--verify", f"refs/heads/{branch}^{{commit}}").decode().strip()
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise Refused("Cannot identify exact Git commit")
    archive = git("archive", "--format=tar", commit)
    return {"branch": branch, "commit": commit, "archive_sha256": sha(archive)}


def trusted_regular(path):
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
        raise Refused("Broker source files must be immutable administrator-owned regular files")


def privileged_archive(branch):
    # Never copy/open caller .git/config, hooks, index, filters, alternates, replace
    # refs or worktree metadata as root. The broker snapshots only object bytes and
    # a validated branch/commit descriptor into this protected directory.
    snapshot = REPO / "broker-source"
    protected_directory(snapshot)
    trusted_regular(snapshot / "source.json")
    identity = json.loads((snapshot / "source.json").read_text())
    commit = identity.get("commit", "")
    if identity.get("branch") != branch or not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise Refused("Broker source identity does not match the requested feature")
    objects = snapshot / "objects"
    protected_directory(objects)
    if not objects.is_dir():
        raise Refused("Broker source object snapshot missing")
    with tempfile.TemporaryDirectory(prefix="fitness-staging-git-") as temporary:
        bare = Path(temporary) / "source.git"
        (bare / "objects").mkdir(parents=True)
        ref = bare / "refs/heads" / branch
        ref.parent.mkdir(parents=True)
        ref.write_text(commit + "\n")
        (bare / "HEAD").write_text(f"ref: refs/heads/{branch}\n")
        (bare / "config").write_text("[core]\nrepositoryformatversion = 0\nbare = true\n")
        for entry in sorted(objects.rglob("*")):
            relative = str(entry.relative_to(objects))
            if entry.is_dir() and not entry.is_symlink():
                if not re.fullmatch(r"[0-9a-f]{2}|pack", relative):
                    raise Refused("Unexpected broker object directory; alternates/metadata forbidden")
                protected_directory(entry)
                (bare / "objects" / relative).mkdir(exist_ok=True)
                continue
            trusted_regular(entry)
            if not re.fullmatch(r"[0-9a-f]{2}/[0-9a-f]{38}|pack/pack-[0-9a-f]{40}\.(pack|idx|rev)", relative):
                raise Refused("Unexpected Git object file; executable configuration forbidden")
            shutil.copyfile(entry, bare / "objects" / relative)
        env = {"PATH": "/usr/bin:/bin", "HOME": temporary, "XDG_CONFIG_HOME": temporary,
               "GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_SYSTEM": "/dev/null", "GIT_CONFIG_GLOBAL": "/dev/null",
               "GIT_CONFIG_COUNT": "0", "GIT_NO_REPLACE_OBJECTS": "1", "GIT_NO_LAZY_FETCH": "1",
               "GIT_ALLOW_PROTOCOL": "", "GIT_TERMINAL_PROMPT": "0", "GIT_ATTR_NOSYSTEM": "1"}
        command = ["/usr/bin/git", "--git-dir", str(bare), "-c", "core.fsmonitor=false",
                   "-c", "core.hooksPath=/dev/null", "-c", "core.attributesFile=/dev/null",
                   "-c", "protocol.allow=never", "-c", "credential.helper="]
        run([*command, "fsck", "--full", "--strict", "--no-reflogs", "--no-dangling"], env=env)
        archive = run([*command, "archive", "--format=tar", commit], env=env)
    return commit, archive


def file_hashes(directory, names):
    result = {}
    for name in names:
        path = directory / name
        if path.is_symlink() or not path.is_file():
            raise Refused(f"Required regular file missing: {name}")
        result[name] = sha(path.read_bytes())
    return result


def read_state():
    path = ROOT / "state.json"
    return json.loads(path.read_text()) if path.exists() else None


def make_plan(operation, branch=None, backup=None):
    if any(os.environ.get(key) for key in
           ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "COMPOSE_FILE", "COMPOSE_PROJECT_NAME")):
        raise Refused("Caller-selected Docker/Compose targets are forbidden")
    plan = {
        "version": 1, "operation": operation, "target": "Dev-VM102-192.168.1.193",
        "docker_host": SOCKET, "project": PROJECT,
        "data": str(ROOT / "data"), "keys": str(ROOT / "keys"),
        "backups": str(BACKUPS), "state": read_state(),
        "pending": json.loads((ROOT / "pending.json").read_text()) if (ROOT / "pending.json").exists() else None,
        "restoring": json.loads((ROOT / "restoring.json").read_text()) if (ROOT / "restoring.json").exists() else None,
        "target_identity_sha256": sha((CONFIG / "target.json").read_bytes()) if (CONFIG / "target.json").is_file() else None,
        "tooling": file_hashes(REPO, ["scripts/staging.py", "deploy/staging/compose.yaml", "Dockerfile", ".dockerignore"]),
    }
    if operation == "deploy":
        plan["source"] = source(branch or "")
        plan["configuration"] = file_hashes(CONFIG, ["settings.json", "server.pfx"])
    if operation == "recover":
        if not backup or not re.fullmatch(r"[0-9a-f]{32}", backup):
            raise Refused("Recovery requires an exact backup ID from an earlier operation")
        plan["backup"] = backup
        plan["backup_manifest_sha256"] = sha((BACKUPS / backup / "manifest.json").read_bytes())
    return plan


def public_intent(operation, branch=None, backup=None):
    # The ordinary agent must not read private state, settings, PFXs or backups.
    # Privileged read-only preparation belongs to the external broker.
    if any(os.environ.get(key) for key in
           ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "COMPOSE_FILE", "COMPOSE_PROJECT_NAME")):
        raise Refused("Caller-selected Docker/Compose targets are forbidden")
    intent = {"operation": operation, "target": "Dev-VM102-192.168.1.193",
              "tooling": file_hashes(REPO, ["scripts/staging.py", "deploy/staging/compose.yaml", "Dockerfile", ".dockerignore"])}
    if operation == "deploy":
        intent["source"] = source(branch or "")
    if operation == "recover":
        if not backup or not re.fullmatch(r"[0-9a-f]{32}", backup):
            raise Refused("Recovery requires an exact backup ID")
        intent["backup"] = backup
    return intent


def prepare(intent):
    if os.geteuid() != 0:
        raise Refused("Private plan preparation requires broker authority")
    for directory in (ROOT, BACKUPS, CONFIG):
        protected_directory(directory)
        if not directory.is_dir():
            raise Refused("Administrator provisioning has not been completed")
    validate_target()
    if intent.get("operation") not in MUTATIONS:
        raise Refused("Unsupported mutation")
    expected = public_intent(intent["operation"], intent.get("source", {}).get("branch"), intent.get("backup"))
    if encoded(expected) != encoded(intent):
        raise Refused("Intent/tooling/source changed; request a fresh plan")
    return make_plan(intent["operation"], intent.get("source", {}).get("branch"), intent.get("backup"))


def docker(*args, image=None, config=None, compose=None):
    env = {"PATH": "/usr/bin:/bin", "HOME": "/root", "STAGING_IMAGE": image or "unused",
           "STAGING_CONFIG_DIR": str(config or CONFIG)}
    argv = ["/usr/bin/docker", "--host", SOCKET, "--config", str(CONFIG / "docker-client")]
    if compose:
        argv += ["compose", "--project-name", PROJECT, "--file", str(compose)]
    return run([*argv, *args], env=env)


def protected_directory(path):
    # Reject redirection and agent-writable state, including every existing ancestor.
    for parent in [path, *path.parents]:
        if not parent.exists():
            continue
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise Refused(f"Administrator-owned, non-writable directory required: {parent}")


def validate_target():
    identity_path = CONFIG / "target.json"
    identity = json.loads(identity_path.read_text())
    info = identity_path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
        raise Refused("Target identity must be administrator-owned and immutable to the agent")
    if identity.get("role") != "Dev-VM102" or identity.get("machine_id") != Path("/etc/machine-id").read_text().strip():
        raise Refused("This host is not the commissioned Dev staging target")
    ip = shutil.which("ip", path="/usr/sbin:/usr/bin:/sbin:/bin")
    if not ip:
        raise Refused("Cannot establish the host's private Dev address")
    interfaces = json.loads(run([ip, "-json", "-4", "address", "show"], env={"PATH": "/usr/bin:/bin"}))
    if not any(address.get("local") == "192.168.1.193" for interface in interfaces for address in interface.get("addr_info", [])):
        raise Refused("Dev private address is not assigned to this host")


def regular_tree(path):
    if path.is_symlink() or not path.is_dir():
        raise Refused("Persistent storage must be an existing real directory")
    result = {}
    for entry in sorted(path.rglob("*")):
        info = entry.lstat()
        if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
            raise Refused("Persistent storage contains a link or unsupported file")
        if entry.is_file():
            result[str(entry.relative_to(path))] = sha(entry.read_bytes())
    return result


def save_state(state):
    temporary = ROOT / "state.tmp"
    temporary.write_bytes(encoded(state))
    os.chmod(temporary, 0o600)
    os.replace(temporary, ROOT / "state.json")


def container(state):
    ids = docker("ps", "-aq", "--filter", f"label=com.docker.compose.project={PROJECT}",
                 "--filter", "label=com.docker.compose.service=app").decode().split()
    if len(ids) > 1:
        raise Refused("Multiple staging writers found; manual inspection required")
    return ids[0] if ids else None


def running(state):
    identifier = container(state)
    return bool(identifier and docker("inspect", "--format", "{{.State.Running}}", identifier).strip() == b"true")


def stop(state):
    identifier = container(state)
    if identifier:
        docker("stop", "--time", "30", identifier)


def start(state, *, recreate=False):
    if not state:
        raise Refused("No deployed staging version; deploy a feature branch first")
    if recreate:
        docker("up", "-d", "--no-build", "--pull", "never", "--force-recreate", "app",
               image=state["image"], config=state["config"], compose=state["compose"])
    else:
        # Start the existing container: does not reset a deliberately stopped lifecycle on boot.
        identifier = container(state)
        if not identifier:
            raise Refused("Recorded staging container missing; use approved recovery/deploy")
        docker("start", identifier)
    for _ in range(40):
        identifier = container(state)
        if identifier:
            health = docker("inspect", "--format", "{{if .State.Health}}{{.State.Health.Status}}{{end}}", identifier).strip()
            if health == b"healthy":
                return
            if health == b"unhealthy" or not running(state):
                break
        time.sleep(3)
    stop(state)
    raise Refused("Staging failed readiness; container stopped")


def backup_state(state):
    identifier = uuid.uuid4().hex
    destination = BACKUPS / identifier
    destination.mkdir(mode=0o700)
    try:
        manifest = {"version": 1, "state": state, "trees": {}}
        for name in ("data", "keys"):
            manifest["trees"][name] = regular_tree(ROOT / name)
            shutil.copytree(ROOT / name, destination / name, copy_function=shutil.copy2)
            if regular_tree(destination / name) != manifest["trees"][name]:
                raise Refused("Backup verification failed")
        (destination / "manifest.json").write_bytes(encoded(manifest))
        os.chmod(destination / "manifest.json", 0o600)
        return identifier
    except BaseException:
        # Incomplete backups are deliberately retained but have no valid manifest.
        raise Refused(f"Backup incomplete ({identifier}); no migration was authorized to proceed")


def recover(identifier):
    destination = BACKUPS / identifier
    manifest = json.loads((destination / "manifest.json").read_text())
    for name in ("data", "keys"):
        if regular_tree(destination / name) != manifest["trees"][name]:
            raise Refused("Backup is incomplete or corrupt; staging remains stopped")
    # Prepare BOTH replacements before touching live directories. A durable journal
    # makes every subsequent rename resumable with the same paired backup.
    journal = ROOT / "restoring.json"
    restore = None
    if journal.exists():
        restore = json.loads(journal.read_text())
        if restore.get("backup") != identifier or not re.fullmatch(r"[0-9a-f]{32}", restore.get("failed", "")):
            raise Refused("An interrupted paired restore must resume with its recorded backup ID")
        failed = ROOT / "failed" / restore["failed"]
    if restore is None or restore.get("phase") == "container":
        # A previous container/readiness attempt may have written compatible old
        # state before failing. Re-restore the approved pair, retaining those writes,
        # rather than assuming the live trees still match the original backup.
        failed = ROOT / "failed" / uuid.uuid4().hex
        failed.mkdir(parents=True, mode=0o700)
        for name in ("data", "keys"):
            staging = failed / (name + ".restore")
            shutil.copytree(destination / name, staging, copy_function=shutil.copy2)
            for entry in [staging, *staging.rglob("*")]:
                os.chown(entry, 1654, 1654)
            if regular_tree(staging) != manifest["trees"][name]:
                raise Refused("Prepared restore verification failed; live state unchanged")
        temporary = ROOT / "restoring.tmp"
        temporary.write_bytes(encoded({"backup": identifier, "failed": failed.name, "phase": "restore"}))
        os.replace(temporary, journal)
    for name in ("data", "keys"):
        staging, saved, live = failed / (name + ".restore"), failed / name, ROOT / name
        if staging.exists():
            if regular_tree(staging) != manifest["trees"][name]:
                raise Refused("Interrupted restore replacement is corrupt; staging remains stopped")
            if live.exists():
                if saved.exists():
                    raise Refused("Conflicting interrupted restore state; staging remains stopped")
                os.rename(live, saved)
            os.rename(staging, live)
        if regular_tree(live) != manifest["trees"][name]:
            raise Refused("Interrupted restore cannot verify the paired live state")
    state = manifest["state"]
    temporary = ROOT / "restoring.tmp"
    temporary.write_bytes(encoded({"backup": identifier, "failed": failed.name, "phase": "container"}))
    os.replace(temporary, journal)
    save_state(state)
    if state:
        docker("up", "--no-start", "--no-build", "--pull", "never", "--force-recreate", "app",
               image=state["image"], config=state["config"], compose=state["compose"])
        if state["active"]:
            start(state)
    else:
        identifier = container(None)
        if identifier:
            docker("rm", identifier)
    # Ordinary start/restart/deploy remain blocked until the compatible container
    # is recreated/removed AND any required readiness check has succeeded.
    journal.unlink()
    return state


def extract_snapshot(archive, destination):
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:") as stream:
        # No symlinks, hardlinks, devices or absolute/traversing paths in build context.
        for member in stream.getmembers():
            if not (member.isfile() or member.isdir()) or member.name.startswith("/") or ".." in Path(member.name).parts:
                raise Refused("Source archive contains unsupported paths or links")
        stream.extractall(destination, filter="data")


def deploy(plan):
    previous = plan["state"]
    if shutil.disk_usage(ROOT).free < 2 * 1024 ** 3 or shutil.disk_usage(tempfile.gettempdir()).free < 512 * 1024 ** 2:
        raise Refused("Insufficient build/backup disk headroom; no cleanup performed")
    if os.geteuid() == 0:
        commit, archive = privileged_archive(plan["source"]["branch"])
        if commit != plan["source"]["commit"]:
            raise Refused("Source commit changed after approval")
    else:
        # Unprivileged synthetic tests/public preparation never grant Docker authority.
        archive = git("archive", "--format=tar", plan["source"]["commit"])
    if sha(archive) != plan["source"]["archive_sha256"]:
        raise Refused("Source snapshot changed after approval")
    release = ROOT / "releases" / uuid.uuid4().hex
    release.mkdir(parents=True, mode=0o700)
    config = release / "config"
    config.mkdir(mode=0o700)
    for name in ("settings.json", "server.pfx"):
        shutil.copy2(CONFIG / name, config / name)
        os.chmod(config / name, 0o640)
        os.chown(config / name, 0, 1654)
    os.chmod(config, 0o750)
    os.chown(config, 0, 1654)
    if file_hashes(config, ["settings.json", "server.pfx"]) != plan["configuration"]:
        raise Refused("Configuration changed after approval")
    compose = release / "compose.yaml"
    shutil.copy2(REPO / "deploy/staging/compose.yaml", compose)
    with tempfile.TemporaryDirectory(prefix="fitness-staging-build-") as build:
        extract_snapshot(archive, Path(build))
        expected = Path(build) / "src/FitnessApp.Server/Staging/StagingOperations.cs"
        if not expected.is_file() or not (Path(build) / "src/FitnessApp.Server/appsettings.Staging.json").is_file():
            raise Refused("Selected feature snapshot does not support staging; bring staging support into that feature through review first")
        # Deployment tooling is the reviewed runner, not arbitrary tooling from the selected feature.
        for name in ("Dockerfile", ".dockerignore"):
            shutil.copy2(REPO / name, Path(build) / name)
        tag = f"fitness-app-staging:{plan['source']['commit']}"
        docker("build", "--label", f"org.opencontainers.image.revision={plan['source']['commit']}",
               "--tag", tag, build)
    image = docker("image", "inspect", "--format", "{{.Id}}", tag).decode().strip()
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", image):
        raise Refused("Cannot record immutable built image identity")
    was_running = running(previous)
    if previous is None and container(None):
        raise Refused("Unrecorded staging container exists; inspect before deployment")
    if previous:
        previous["active"] = was_running
    (ROOT / "pending.json").write_bytes(encoded({"previous": previous, "candidate": plan["source"], "backup": None}))
    stop(previous)
    if running(previous):
        raise Refused("Staging writers did not stop")
    try:
        identifier = backup_state(previous)
    except BaseException:
        if previous and was_running:
            start(previous)
        (ROOT / "pending.json").unlink(missing_ok=True)
        raise
    (ROOT / "pending.json").write_bytes(encoded({"previous": previous, "candidate": plan["source"], "backup": identifier}))
    print(f"Verified pre-migration backup: {identifier}", flush=True)
    candidate = {**plan["source"], "image": image, "config": str(config), "compose": str(compose), "active": True}
    try:
        docker("run", "--rm", "--no-deps", "--pull", "never", "app", "migrate-staging",
               image=image, config=config, compose=compose)
        start(candidate, recreate=True)
        save_state(candidate)
    except BaseException:
        stop(candidate)
        try:
            recover(identifier)
            (ROOT / "pending.json").unlink(missing_ok=True)
        except BaseException:
            raise Refused(f"Update failed; recovery incomplete; staging stopped. Restore paired backup {identifier}")
        raise Refused(f"Update failed; paired state restored from {identifier}")
    (ROOT / "pending.json").unlink(missing_ok=True)
    print(f"Healthy: {candidate['branch']} {candidate['commit']} {image}")


def execute(plan):
    # This is defense in depth, NOT proof of human approval. Only the external broker
    # may grant this authority; the agent must never have independent root/socket access.
    if os.geteuid() != 0:
        raise Refused("Worker requires externally approved authority; no agent-side approval bypass")
    if plan["operation"] not in MUTATIONS:
        raise Refused("Unsupported mutation")
    for path in (ROOT, BACKUPS, CONFIG):
        protected_directory(path)
        if not path.is_dir():
            raise Refused("Administrator provisioning has not been completed")
    validate_target()
    os.umask(0o077)
    with (ROOT / "operation.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fresh = make_plan(plan["operation"], plan.get("source", {}).get("branch"), plan.get("backup"))
        if encoded(fresh) != encoded(plan):
            raise Refused("Plan changed since human approval; request a fresh approval")
        if (fresh["pending"] or fresh["restoring"]) and plan["operation"] not in ("recover", "stop"):
            raise Refused("Interrupted update recorded; inspect pending.json and approve recovery before proceeding")
        state = fresh["state"]
        operation = plan["operation"]
        if operation == "deploy":
            deploy(fresh)
        elif operation == "stop":
            stop(state)
            if state:
                state["active"] = False
                save_state(state)
        elif operation in ("start", "restart"):
            if operation == "restart":
                stop(state)
            start(state)
            state["active"] = True
            save_state(state)
        elif operation == "backup":
            active = running(state)
            stop(state)
            try:
                if state:
                    state["active"] = active
                identifier = backup_state(state)
                print(f"Verified backup: {identifier}")
            finally:
                if active:
                    start(state)
        elif operation == "recover":
            stop(state)
            recover(plan["backup"])
            (ROOT / "pending.json").unlink(missing_ok=True)


def request_broker(action, intent):
    if not BROKER.is_file():
        raise Refused("Human approval broker is not commissioned; no Docker mutation performed")
    protected_directory(BROKER.parent)
    info = BROKER.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
        raise Refused("Approval broker must be an administrator-owned executable")
    # Broker must copy/verify tooling and source BEFORE approval, display the plan,
    # obtain a human decision outside the agent, then execute its immutable worker.
    # It must never simply execute this writable checkout after a hash comparison.
    result = subprocess.run([str(BROKER)], input=encoded({"action": action, "intent": intent, "repository": str(REPO)}),
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if result.returncode:
        raise Refused("Human approval broker refused or failed the operation")
    return result.stdout


def request_approval(intent):
    return request_broker("execute", intent)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["plan", *sorted(MUTATIONS), "status", "logs", "_broker-worker", "_broker-prepare", "_broker-inspect"])
    parser.add_argument("--operation", choices=sorted(MUTATIONS), default="deploy")
    parser.add_argument("--branch")
    parser.add_argument("--backup")
    args = parser.parse_args()
    try:
        if args.command == "_broker-worker":
            execute(json.load(sys.stdin))
        elif args.command == "_broker-prepare":
            print(json.dumps(prepare(json.load(sys.stdin)), indent=2, sort_keys=True))
        elif args.command == "_broker-inspect":
            if os.geteuid() != 0:
                raise Refused("Read-only Docker inspection requires broker authority")
            protected_directory(CONFIG)
            validate_target()
            intent = json.load(sys.stdin)
            if intent != public_intent(intent.get("operation")):
                raise Refused("Inspection intent changed")
            if intent["operation"] == "status":
                print(docker("ps", "-a", "--filter", f"label=com.docker.compose.project={PROJECT}",
                             "--format", "{{.Names}}\t{{.Status}}").decode(), end="")
            elif intent["operation"] == "logs":
                identifier = container(read_state())
                if not identifier:
                    raise Refused("No staging container found")
                print(docker("logs", "--tail", "100", identifier).decode(), end="")
            else:
                raise Refused("Unsupported read-only inspection")
        elif args.command in ("status", "logs"):
            print(request_broker("inspect", public_intent(args.command)).decode(), end="")
        else:
            operation = args.operation if args.command == "plan" else args.command
            intent = public_intent(operation, args.branch, args.backup)
            if args.command == "plan":
                print(request_broker("prepare", intent).decode(), end="")
            else:
                print(request_approval(intent).decode(), end="")
        return 0
    except (Refused, OSError, ValueError, KeyError) as error:
        # Errors originating outside our constant messages may contain private paths.
        print(str(error) if isinstance(error, Refused) else "Staging operation refused; inspect protected state locally", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
