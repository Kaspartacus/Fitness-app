#!/usr/bin/env python3
"""Manual Dev staging. Ask the human before every Docker mutation.

The interactive plan confirmation is an operator guard, not a security boundary
against a user with full Docker access. See deploy/staging/README.md.
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
import socket
import subprocess
import sys
import tarfile
import tempfile
import time
import uuid

PROJECT = "fitness-app-staging"
ROOT = Path("/home/codex/.local/share/fitness-app-staging")
BACKUPS = ROOT / "backups"
CONFIG = ROOT / "config"
APP_UID = APP_GID = 1001
SOCKET = "unix:///var/run/docker.sock"
REPO = Path(__file__).resolve().parent.parent
MUTATIONS = {"deploy", "start", "stop", "restart", "backup", "recover", "cleanup"}


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
    require_user()
    return run(["git", "-C", str(REPO), *args])


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def source(branch):
    if not re.fullmatch(r"feature/[A-Za-z0-9][A-Za-z0-9._/-]*", branch) or ".." in branch:
        raise Refused("Only named feature/* branches are eligible for Dev staging")
    if git("symbolic-ref", "--quiet", "--short", "HEAD").decode().strip() != branch:
        raise Refused("Requested branch must be checked out; detached HEAD is ineligible")
    if git("status", "--porcelain", "--untracked-files=normal").strip():
        raise Refused("Source must be clean and committed before planning a deployment")
    commit = git("rev-parse", "--verify", f"refs/heads/{branch}^{{commit}}").decode().strip()
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise Refused("Cannot identify exact Git commit")
    archive = git("archive", "--format=tar", commit)
    return {"branch": branch, "commit": commit, "archive_sha256": sha(archive)}


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


def cleanup_plan():
    """Fail closed on unresolved recovery; never use daemon-wide prune."""
    if any((ROOT / name).exists() for name in
           ("pending.json", "restoring.json", "import-pending.json")):
        raise Refused("Resolve pending deployment/import/recovery before cleanup")
    state = read_state()
    protected_images = {state["image"]} if state else set()
    protected_releases = set()
    def protect(deployment):
        if deployment:
            protected_images.add(deployment["image"])
            for name in ("config", "compose"):
                path = Path(deployment[name])
                try:
                    release = path.relative_to(ROOT / "releases").parts[0]
                except (ValueError, IndexError):
                    raise Refused("Unrecognized recovery release path")
                protected_releases.add(release)
    protect(state)
    backups = []
    for path in BACKUPS.iterdir():
        if not re.fullmatch(r"[0-9a-f]{32}", path.name):
            raise Refused("Unknown backup requires manual inspection")
        regular_tree(path)  # reject links, devices and incomplete backup manifests
        manifest = json.loads((path / "manifest.json").read_text())
        for name in ("data", "keys"):
            if regular_tree(path / name) != manifest["trees"][name]:
                raise Refused("Corrupt backup requires inspection before cleanup")
        backups.append((path, manifest))
    backups.sort(key=lambda item: (item[0].stat().st_mtime_ns, item[0].name), reverse=True)
    for path, manifest in backups[:5]:
        protect(manifest["state"])
    directories = [str(path) for path, _ in backups[5:]]
    for area, keep in (("failed", 1), ("releases", None)):
        parent = ROOT / area
        paths = list(parent.iterdir()) if parent.exists() else []
        for path in paths:
            if not re.fullmatch(r"[0-9a-f]{32}", path.name):
                raise Refused("Unknown retained directory requires inspection")
            regular_tree(path)
        paths.sort(key=lambda p: (p.stat().st_mtime_ns, p.name), reverse=True)
        directories.extend(str(p) for p in paths if
                           (p.name not in protected_releases if keep is None else p not in paths[:keep]))
    # Protect images used by ANY container, including deliberately stopped ones.
    containers = docker("ps", "-aq").decode().split()
    if containers:
        protected_images.update(json.loads(docker("inspect", "--format", "{{json .Image}}", c))
                                for c in containers)
    tags = docker("image", "ls", "--filter", "reference=fitness-app-staging:*",
                  "--format", "{{.Repository}}:{{.Tag}}").decode().split()
    images = []
    for tag in sorted(set(tags)):
        if not re.fullmatch(r"fitness-app-staging:[0-9a-f]{40}", tag):
            continue
        identity = docker("image", "inspect", "--format", "{{.Id}}", tag).decode().strip()
        if identity not in protected_images:
            images.append({"tag": tag, "id": identity})
    dangling = docker("image", "ls", "--filter", "label=com.fitness-app.staging=true",
                      "--filter", "dangling=true", "--no-trunc", "--quiet").decode().split()
    for identity in sorted(set(dangling)):
        if not re.fullmatch(r"sha256:[0-9a-f]{64}", identity):
            raise Refused("Unexpected owned image identity")
        if identity not in protected_images:
            images.append({"tag": identity, "id": identity})
    return {"keep_complete_backups": 5, "keep_failed_snapshots": 1,
            "directories": sorted(directories), "images": images}


def cleanup(expected):
    if cleanup_plan() != expected:
        raise Refused("Cleanup targets changed; request a fresh plan")
    # Untag only this application's exact tags, never force removal or prune globally.
    for image in expected["images"]:
        docker("image", "rm", image["tag"])
    for name in expected["directories"]:
        shutil.rmtree(name)
    print(f"Cleanup: removed {len(expected['directories'])} directories and {len(expected['images'])} image tags")


def maintain_retention():
    try:
        cleanup(cleanup_plan())
    except (Refused, OSError, ValueError, KeyError):
        # Maintenance failure must not roll back or stop a successful deployment.
        print("Staging succeeded; retention cleanup deferred for operator inspection", file=sys.stderr)


def make_plan(operation, branch=None, backup=None):
    if any(os.environ.get(key) for key in
           ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "COMPOSE_FILE", "COMPOSE_PROJECT_NAME")):
        raise Refused("Caller-selected Docker/Compose targets are forbidden")
    plan = {
        "version": 1, "operation": operation, "target": "Dev-VM102-192.168.1.193",
        "docker_host": SOCKET, "project": PROJECT,
        "resources": {"container": "fitness-app-staging-app-1", "network": "fitness-app-staging_default",
                      "image": (read_state() or {}).get("image")},
        "data": str(ROOT / "data"), "keys": str(ROOT / "keys"),
        "backups": str(BACKUPS), "state": read_state(),
        "pending": json.loads((ROOT / "pending.json").read_text()) if (ROOT / "pending.json").exists() else None,
        "restoring": json.loads((ROOT / "restoring.json").read_text()) if (ROOT / "restoring.json").exists() else None,
        "tooling": file_hashes(REPO, ["scripts/staging.py", "deploy/staging/compose.yaml", "Dockerfile", ".dockerignore"]),
    }
    if operation == "cleanup":
        plan["cleanup"] = cleanup_plan()
    if operation in ("deploy", "backup"):
        plan["retention"] = {"keep_complete_backups": 5, "keep_failed_snapshots": 1,
                             "after_success_only": True, "scope": "staging backups, failed snapshots, unreferenced releases and staging image tags"}
    if operation == "deploy":
        plan["source"] = source(branch or "")
        plan["resources"]["image"] = f"fitness-app-staging:{plan['source']['commit']}"
        plan["configuration"] = file_hashes(CONFIG, ["settings.json", "server.pfx"])
    if operation == "recover":
        if not backup or not re.fullmatch(r"[0-9a-f]{32}", backup):
            raise Refused("Recovery requires an exact backup ID from an earlier operation")
        plan["backup"] = backup
        manifest_bytes = (BACKUPS / backup / "manifest.json").read_bytes()
        plan["backup_manifest_sha256"] = sha(manifest_bytes)
        restored = json.loads(manifest_bytes)["state"]
        deployment = None if restored is None else {
            name: restored.get(name) for name in ("branch", "commit", "image", "config", "compose", "active")
        }
        fingerprints = None
        if deployment and deployment["config"] and Path(deployment["config"]).is_dir():
            fingerprints = file_hashes(Path(deployment["config"]), ["settings.json", "server.pfx"])
        plan["restore"] = {
            "deployment": deployment, "configuration_fingerprints": fingerprints,
            "start_after_restore": bool(deployment and deployment["active"]),
            "container_action": "recreate selected backup deployment" if deployment else "remove failed staging container; restore empty storage; do not start any image",
        }
        plan["resources"]["image"] = deployment["image"] if deployment else None
    plan["actions"] = {
        "deploy": ["build exact commit locally", "stop staging writers", "back up paired database/keys and old deployment",
                   "run explicit migrations", "recreate application and verify health",
                   "on failure restore paired backup and previous image/config; remove failed first-deploy container if no previous deployment"],
        "cleanup": ["delete only the listed staging directories and unused image tags; preserve live state"],
        "start": ["start recorded staging container", "verify health; stop on failed readiness"],
        "stop": ["stop staging container; retain all data, keys and configuration"],
        "restart": ["stop and start recorded staging container", "verify health; stop on failed readiness"],
        "backup": ["stop staging writers", "copy and verify paired database/keys", "resume only if previously active"],
        "recover": ["stop staging writers", "preserve failed state", "restore selected database/key pair",
                    "recreate matching image/config or remove failed first-deploy container", "verify readiness if previously active"],
    }[operation]
    if operation == "recover":
        plan["actions"] = ["stop staging writers", "preserve failed state", "restore selected database/key pair",
                           plan["restore"]["container_action"],
                           "start restored deployment and verify readiness" if plan["restore"]["start_after_restore"] else "leave staging stopped"]
    return plan


def docker(*args, image=None, config=None, compose=None):
    env = {"PATH": "/usr/bin:/bin", "HOME": "/home/codex", "STAGING_IMAGE": image or "unused",
           "STAGING_CONFIG_DIR": str(config or CONFIG)}
    argv = ["/usr/bin/docker", "--host", SOCKET, "--config", str(CONFIG / "docker-client")]
    if compose:
        argv += ["compose", "--project-name", PROJECT, "--file", str(compose)]
    return run([*argv, *args], env=env)


def require_user():
    if os.geteuid() != APP_UID or os.getegid() != APP_GID:
        raise Refused("Run staging as the non-root codex account (UID/GID 1001); never sudo this script")


def private_directory(path):
    if not path.is_dir() or path.is_symlink():
        raise Refused("Staging private directories must be provisioned before mutation")
    info = path.lstat()
    if info.st_uid != APP_UID or info.st_mode & 0o077:
        raise Refused("Staging directories must be codex-owned and mode 0700")
    for parent in path.parents:
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or info.st_uid not in (0, APP_UID) or info.st_mode & 0o022:
            raise Refused("Staging storage has an unsafe ancestor directory")


def validate_target():
    require_user()
    if socket.gethostname() != "dev":
        raise Refused("Staging is restricted to the Dev host")
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
    archive = git("archive", "--format=tar", plan["source"]["commit"])
    if sha(archive) != plan["source"]["archive_sha256"]:
        raise Refused("Source snapshot changed after approval")
    release = ROOT / "releases" / uuid.uuid4().hex
    release.mkdir(parents=True, mode=0o700)
    config = release / "config"
    config.mkdir(mode=0o700)
    for name in ("settings.json", "server.pfx"):
        shutil.copy2(CONFIG / name, config / name)
        os.chmod(config / name, 0o600)
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
        docker("build", "--label", "com.fitness-app.staging=true", "--label", f"org.opencontainers.image.revision={plan['source']['commit']}",
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
    require_user()
    if plan["operation"] not in MUTATIONS:
        raise Refused("Unsupported mutation")
    for path in (ROOT, BACKUPS, CONFIG):
        private_directory(path)
    if plan["operation"] in ("deploy", "start", "restart", "backup") and not plan["restoring"]:
        for path in (ROOT / "data", ROOT / "keys"):
            private_directory(path)
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
        elif operation == "cleanup":
            cleanup(fresh["cleanup"])
        elif operation == "recover":
            stop(state)
            recover(plan["backup"])
            (ROOT / "pending.json").unlink(missing_ok=True)
        if operation in ("deploy", "backup"):
            maintain_retention()


def confirm(plan):
    # A TTY can be driven by Codex. This is deliberately an operator guard, not
    # an authorization service. Codex must obtain the human's chat approval first.
    if not sys.stdin.isatty():
        raise Refused("A terminal confirmation is required; ask the human before running this mutation")
    phrase = f"{plan['operation']} {sha(encoded(plan))[:16]}"
    try:
        answer = input(f"After explicit human approval, type '{phrase}' to execute exactly this plan: ")
    except EOFError:
        raise Refused("No confirmation received; no Docker mutation performed")
    if answer != phrase:
        raise Refused("Operation cancelled; no Docker mutation performed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["plan", *sorted(MUTATIONS), "status", "logs"])
    parser.add_argument("--operation", choices=sorted(MUTATIONS), default="deploy")
    parser.add_argument("--branch")
    parser.add_argument("--backup")
    args = parser.parse_args()
    try:
        validate_target()
        if args.command == "status":
            print(docker("ps", "-a", "--filter", f"label=com.docker.compose.project={PROJECT}",
                         "--format", "{{.Names}}\t{{.Status}}").decode(), end="")
        elif args.command == "logs":
            identifier = container(read_state())
            if not identifier:
                raise Refused("No staging container found")
            print(docker("logs", "--tail", "100", identifier).decode(), end="")
        else:
            operation = args.operation if args.command == "plan" else args.command
            plan = make_plan(operation, args.branch, args.backup)
            print(json.dumps(plan, indent=2, sort_keys=True), flush=True)
            if args.command != "plan":
                confirm(plan)
                execute(plan)
        return 0
    except (Refused, OSError, ValueError, KeyError) as error:
        print(str(error) if isinstance(error, Refused) else "Staging operation refused; inspect private state locally", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
