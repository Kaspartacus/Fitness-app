"""Synthetic staging transactions: no Docker daemon, privileged writes or real data."""
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import shutil
import tarfile
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("staging", Path(__file__).resolve().parents[1] / "scripts/staging.py")
staging = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(staging)


class StagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        subprocess.run(["git", "init", "-q", "-b", "feature/synthetic", str(self.repo)], check=True)
        self.git("config", "user.name", "Synthetic")
        self.git("config", "user.email", "synthetic@example.test")
        for name in ("scripts/staging.py", "deploy/staging/compose.yaml", "Dockerfile", ".dockerignore"):
            path = self.repo / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("synthetic\n")
        self.git("add", ".")
        self.git("commit", "-qm", "Synthetic baseline")
        for name in ("runtime/data", "runtime/keys", "backups", "config"):
            (self.root / name).mkdir(parents=True, exist_ok=True)
        for name in ("settings.json", "server.pfx"):
            (self.root / "config" / name).write_text("synthetic")
        for name, value in (("REPO", self.repo), ("ROOT", self.root / "runtime"),
                            ("BACKUPS", self.root / "backups"), ("CONFIG", self.root / "config"),
                            ("BROKER", self.root / "missing-broker")):
            mock = patch.object(staging, name, value)
            mock.start()
            self.addCleanup(mock.stop)
        clean = patch.dict(staging.os.environ, {}, clear=True)
        clean.start()
        self.addCleanup(clean.stop)
        disk = patch.object(staging.shutil, "disk_usage", return_value=SimpleNamespace(free=10 * 1024 ** 3))
        disk.start()
        self.addCleanup(disk.stop)

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.repo), *args])

    def test_main_and_nonfeature_and_ref_injection_rejected(self):
        for branch in ("main", "hotfix/test", "feature/../main", "--help", "feature/x^{tree}"):
            with self.subTest(branch=branch), self.assertRaises(staging.Refused):
                staging.source(branch)

    def test_exact_commit_and_archive_digest(self):
        result = staging.source("feature/synthetic")
        self.assertEqual(self.git("rev-parse", "HEAD").decode().strip(), result["commit"])
        self.assertEqual(staging.sha(self.git("archive", "--format=tar", "HEAD")), result["archive_sha256"])

    def test_dirty_source_and_untracked_source_rejected(self):
        (self.repo / "untracked").write_text("synthetic")
        with self.assertRaises(staging.Refused):
            staging.source("feature/synthetic")

    def test_detached_head_rejected(self):
        self.git("checkout", "--detach", "-q")
        with self.assertRaises(staging.Refused):
            staging.source("feature/synthetic")

    def test_other_checked_out_feature_rejected(self):
        self.git("branch", "feature/other")
        with self.assertRaises(staging.Refused):
            staging.source("feature/other")

    def test_remote_docker_or_compose_injection_rejected(self):
        for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CERT_PATH", "COMPOSE_FILE", "COMPOSE_PROJECT_NAME"):
            with self.subTest(key=key), patch.dict(staging.os.environ, {key: "synthetic"}), self.assertRaises(staging.Refused):
                staging.make_plan("stop")

    def test_missing_broker_never_runs_docker(self):
        with patch.object(staging, "docker") as docker, self.assertRaises(staging.Refused):
            staging.request_approval(staging.make_plan("stop"))
        docker.assert_not_called()

    def test_unprivileged_worker_refuses_before_any_operation(self):
        with patch.object(staging.os, "geteuid", return_value=1001), patch.object(staging, "docker") as docker:
            with self.assertRaises(staging.Refused):
                staging.execute(staging.make_plan("stop"))
            docker.assert_not_called()

    def test_backup_database_and_keys_are_paired_and_verified(self):
        (staging.ROOT / "data/fitnessapp.db").write_bytes(b"synthetic database")
        (staging.ROOT / "data/fitnessapp.db-wal").write_bytes(b"synthetic wal")
        (staging.ROOT / "keys/key-synthetic.xml").write_text("synthetic key")
        state = {"image": "synthetic-image", "active": False}
        identifier = staging.backup_state(state)
        manifest = json.loads((staging.BACKUPS / identifier / "manifest.json").read_text())
        self.assertEqual(state, manifest["state"])
        self.assertEqual(staging.regular_tree(staging.ROOT / "data"), manifest["trees"]["data"])
        self.assertEqual(staging.regular_tree(staging.ROOT / "keys"), manifest["trees"]["keys"])

    def test_storage_symlink_rejected_without_copying_target(self):
        (staging.ROOT / "keys/link").symlink_to(staging.CONFIG / "settings.json")
        with self.assertRaises(staging.Refused):
            staging.backup_state(None)

    def test_recovery_rejects_corrupt_backup_before_touching_live_data(self):
        identifier = staging.backup_state(None)
        (staging.BACKUPS / identifier / "data/extra").write_text("corrupt")
        (staging.ROOT / "data/keep").write_text("synthetic live")
        with self.assertRaises(staging.Refused):
            staging.recover(identifier)
        self.assertEqual("synthetic live", (staging.ROOT / "data/keep").read_text())

    def test_recovery_restores_matching_state_and_preserves_failed_data(self):
        (staging.ROOT / "data/db").write_text("old")
        (staging.ROOT / "keys/key").write_text("old key")
        state = {"image": "old-image", "config": "old-config", "compose": "old-compose", "active": False}
        identifier = staging.backup_state(state)
        (staging.ROOT / "data/db").write_text("migrated")
        (staging.ROOT / "keys/key").write_text("new key")
        with patch.object(staging.os, "chown"), patch.object(staging, "docker") as docker, patch.object(staging, "start") as start:
            staging.recover(identifier)
        self.assertEqual("old", (staging.ROOT / "data/db").read_text())
        self.assertEqual("old key", (staging.ROOT / "keys/key").read_text())
        self.assertEqual("migrated", next((staging.ROOT / "failed").glob("*/data/db")).read_text())
        self.assertEqual(state, staging.read_state())
        self.assertIn("--no-start", docker.call_args.args)
        self.assertEqual("old-image", docker.call_args.kwargs["image"])
        start.assert_not_called()

    def test_active_recovery_starts_only_after_paired_restore(self):
        state = {"image": "old", "config": "config", "compose": "compose", "active": True}
        identifier = staging.backup_state(state)
        with patch.object(staging.os, "chown"), patch.object(staging, "docker"), patch.object(staging, "start") as start:
            staging.recover(identifier)
        start.assert_called_once_with(state)

    def test_source_tar_traversal_and_symlinks_rejected(self):
        for name, kind in (("../escape", tarfile.REGTYPE), ("link", tarfile.SYMTYPE)):
            data = io.BytesIO()
            with tarfile.open(fileobj=data, mode="w") as stream:
                member = tarfile.TarInfo(name)
                member.type = kind
                member.linkname = "/synthetic"
                stream.addfile(member)
            with self.assertRaises(staging.Refused):
                staging.extract_snapshot(data.getvalue(), self.root / "extract")

    def test_failed_health_stops_instead_of_serving_bad_version(self):
        state = {"image": "synthetic", "config": "config", "compose": "compose"}
        with patch.object(staging, "container", return_value="synthetic"), patch.object(staging, "docker", return_value=b"unhealthy"), patch.object(staging, "stop") as stop:
            with self.assertRaises(staging.Refused):
                staging.start(state)
        stop.assert_called_once_with(state)

    def test_source_commit_mismatch_refuses_before_build(self):
        plan = staging.make_plan("deploy", "feature/synthetic")
        plan["source"]["archive_sha256"] = "wrong"
        with patch.object(staging, "docker") as docker, self.assertRaises(staging.Refused):
            staging.deploy(plan)
        docker.assert_not_called()

    def test_recovery_id_cannot_traverse_directories(self):
        with self.assertRaises(staging.Refused):
            staging.make_plan("recover", backup="../synthetic")

    def test_docker_client_discards_environment_and_pins_socket(self):
        with patch.dict(staging.os.environ, {"DOCKER_CONTEXT": "production", "COMPOSE_FILE": "/bad", "HTTP_PROXY": "bad"}), patch.object(staging, "run", return_value=b"") as run:
            staging.docker("ps")
        self.assertIn(staging.SOCKET, run.call_args.args[0])
        self.assertNotIn("DOCKER_CONTEXT", run.call_args.kwargs["env"])
        self.assertNotIn("HTTP_PROXY", run.call_args.kwargs["env"])

    def deploy_plan(self):
        for name in ("src/FitnessApp.Server/Staging/StagingOperations.cs", "src/FitnessApp.Server/appsettings.Staging.json"):
            path = self.repo / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("synthetic")
        self.git("add", ".")
        self.git("commit", "-qm", "Synthetic staging support")
        state = {"image": "old-image", "config": "old-config", "compose": "old-compose", "active": True}
        staging.save_state(state)
        return staging.make_plan("deploy", "feature/synthetic")

    @staticmethod
    def fake_docker(*args, **kwargs):
        return ("sha256:" + "a" * 64).encode() if args[:2] == ("image", "inspect") else b""

    def test_migration_failure_restores_pair_and_never_starts_candidate(self):
        plan = self.deploy_plan()
        def docker(*args, **kwargs):
            if args[0] == "run":
                raise staging.Refused("synthetic migration failure")
            return self.fake_docker(*args, **kwargs)
        with patch.object(staging.os, "chown"), patch.object(staging, "docker", side_effect=docker), patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop"), patch.object(staging, "backup_state", return_value="b" * 32), patch.object(staging, "recover") as recover, patch.object(staging, "start") as start:
            with self.assertRaises(staging.Refused):
                staging.deploy(plan)
        recover.assert_called_once_with("b" * 32)
        start.assert_not_called()
        self.assertFalse((staging.ROOT / "pending.json").exists())

    def test_health_failure_restores_pair_instead_of_old_image_new_schema(self):
        plan = self.deploy_plan()
        with patch.object(staging.os, "chown"), patch.object(staging, "docker", side_effect=self.fake_docker), patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop") as stop, patch.object(staging, "backup_state", return_value="b" * 32), patch.object(staging, "recover") as recover, patch.object(staging, "start", side_effect=staging.Refused("synthetic health failure")):
            with self.assertRaises(staging.Refused):
                staging.deploy(plan)
        recover.assert_called_once_with("b" * 32)
        self.assertGreaterEqual(stop.call_count, 2)

    def test_failed_recovery_preserves_pending_journal_for_explicit_recovery(self):
        plan = self.deploy_plan()
        with patch.object(staging.os, "chown"), patch.object(staging, "docker", side_effect=self.fake_docker), patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop"), patch.object(staging, "backup_state", return_value="b" * 32), patch.object(staging, "recover", side_effect=staging.Refused("synthetic recovery failure")), patch.object(staging, "start", side_effect=staging.Refused("synthetic health failure")):
            with self.assertRaisesRegex(staging.Refused, "recovery incomplete"):
                staging.deploy(plan)
        self.assertEqual("b" * 32, json.loads((staging.ROOT / "pending.json").read_text())["backup"])

    def test_failed_backup_restarts_previous_without_running_migrations(self):
        plan = self.deploy_plan()
        with patch.object(staging.os, "chown"), patch.object(staging, "docker", side_effect=self.fake_docker) as docker, patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop"), patch.object(staging, "backup_state", side_effect=staging.Refused("synthetic backup failure")), patch.object(staging, "start") as start:
            with self.assertRaises(staging.Refused):
                staging.deploy(plan)
        start.assert_called_once_with(plan["state"])
        self.assertFalse(any(call.args[0] == "run" for call in docker.call_args_list))

    def test_unsupported_feature_snapshot_rejected_before_build(self):
        plan = staging.make_plan("deploy", "feature/synthetic")
        with patch.object(staging.os, "chown"), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "does not support staging"):
                staging.deploy(plan)
        docker.assert_not_called()

    def test_changed_plan_refuses_before_mutation(self):
        plan = staging.make_plan("stop")
        self.git("checkout", "-qb", "feature/moved")
        with patch.object(staging.os, "geteuid", return_value=0), patch.object(staging, "protected_directory"), patch.object(staging, "validate_target"), patch.object(staging, "make_plan", return_value={**plan, "state": {"image": "changed"}}), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "changed since human approval"):
                staging.execute(plan)
        docker.assert_not_called()

    def test_wrong_host_identity_refused_without_docker(self):
        (staging.CONFIG / "target.json").write_text(json.dumps({"role": "Apps-VM101", "machine_id": "synthetic"}))
        with patch.object(staging, "docker") as docker, self.assertRaises(staging.Refused):
            staging.validate_target()
        docker.assert_not_called()

    def test_low_disk_space_refuses_before_build_or_stop(self):
        plan = staging.make_plan("deploy", "feature/synthetic")
        with patch.object(staging.shutil, "disk_usage", return_value=SimpleNamespace(free=0)), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "headroom"):
                staging.deploy(plan)
        docker.assert_not_called()

    def test_public_intent_does_not_read_private_configuration_or_state(self):
        with patch.object(staging, "read_state", side_effect=AssertionError("private state accessed")):
            intent = staging.public_intent("recover", backup="a" * 32)
        self.assertNotIn("state", intent)
        self.assertNotIn("configuration", intent)
        self.assertEqual("a" * 32, intent["backup"])

    def test_public_status_uses_read_only_broker_not_direct_docker(self):
        with patch.object(staging.sys, "argv", ["staging.py", "status"]), patch.object(staging, "request_broker", return_value=b"synthetic status\n") as broker, patch.object(staging, "docker") as docker:
            self.assertEqual(0, staging.main())
        self.assertEqual("inspect", broker.call_args.args[0])
        docker.assert_not_called()

    def test_public_plan_uses_privileged_read_only_preparation(self):
        with patch.object(staging.sys, "argv", ["staging.py", "plan", "--operation", "stop"]), patch.object(staging, "request_broker", return_value=b"synthetic plan\n") as broker, patch.object(staging, "make_plan", side_effect=AssertionError("private plan accessed")):
            self.assertEqual(0, staging.main())
        self.assertEqual("prepare", broker.call_args.args[0])

    def test_private_prepare_cannot_run_as_agent(self):
        with patch.object(staging.os, "geteuid", return_value=1001), self.assertRaises(staging.Refused):
            staging.prepare(staging.public_intent("stop"))

    def test_privileged_source_never_executes_caller_fsmonitor_or_filters(self):
        marker = self.root / "unexpected-command"
        payload = self.root / "hostile-git-feature"
        payload.write_text(f"#!/bin/sh\ntouch '{marker}'\ncat\n")
        payload.chmod(0o700)
        # Commit attributes first, before enabling a malicious clean filter.
        (self.repo / ".gitattributes").write_text("* filter=hostile\n")
        self.git("add", ".gitattributes")
        self.git("commit", "-qm", "Synthetic attributes")
        commit = self.git("rev-parse", "HEAD").decode().strip()
        expected = self.git("archive", "--format=tar", commit)
        self.git("config", "core.fsmonitor", str(payload))
        self.git("config", "filter.hostile.clean", str(payload))
        self.git("config", "filter.hostile.smudge", str(payload))
        snapshot = self.repo / "broker-source"
        snapshot.mkdir()
        (snapshot / "source.json").write_text(json.dumps({"branch": "feature/synthetic", "commit": commit}))
        shutil.copytree(self.repo / ".git/objects", snapshot / "objects")
        # Empty Git info/pack directories are not part of broker object protocol.
        for directory in (snapshot / "objects/info",):
            if directory.exists():
                directory.rmdir()
        # Simulate root authority, but run actual Git in an ordinary temporary fixture.
        # Ownership checks are independently required in production; no real elevation.
        with patch.object(staging.os, "geteuid", return_value=0), patch.object(staging, "protected_directory"), patch.object(staging, "trusted_regular"):
            result = staging.source("feature/synthetic")
            self.assertEqual(commit, result["commit"])
            self.assertEqual(staging.sha(expected), result["archive_sha256"])
            intent = {"operation": "deploy", "target": "Dev-VM102-192.168.1.193",
                      "tooling": staging.file_hashes(self.repo, ["scripts/staging.py", "deploy/staging/compose.yaml", "Dockerfile", ".dockerignore"]),
                      "source": result}
            with patch.object(staging, "validate_target"):
                plan = staging.prepare(intent)
            self.assertEqual(commit, plan["source"]["commit"])
        self.assertFalse(marker.exists(), "Caller Git features executed during privileged source preparation")

    def test_privileged_ordinary_git_is_forbidden(self):
        with patch.object(staging.os, "geteuid", return_value=0), patch.object(staging, "run") as run, self.assertRaises(staging.Refused):
            staging.git("status")
        run.assert_not_called()

    def interrupted_restore(self, fail_at):
        (staging.ROOT / "data/db").write_text("old")
        (staging.ROOT / "keys/key").write_text("old key")
        state = {"image": "old-image", "config": "config", "compose": "compose", "active": False}
        identifier = staging.backup_state(state)
        (staging.ROOT / "data/db").write_text("migrated")
        (staging.ROOT / "keys/key").write_text("new key")
        rename = staging.os.rename
        calls = 0
        def interrupted(*args):
            nonlocal calls
            calls += 1
            if calls == fail_at:
                raise OSError("synthetic interruption between paired renames")
            return rename(*args)
        with patch.object(staging.os, "chown"), patch.object(staging.os, "rename", side_effect=interrupted), patch.object(staging, "docker"):
            with self.assertRaises(OSError):
                staging.recover(identifier)
        self.assertTrue((staging.ROOT / "restoring.json").exists())
        with patch.object(staging.os, "chown"), patch.object(staging, "docker"):
            staging.recover(identifier)
        self.assertEqual("old", (staging.ROOT / "data/db").read_text())
        self.assertEqual("old key", (staging.ROOT / "keys/key").read_text())
        self.assertFalse((staging.ROOT / "restoring.json").exists())
        self.assertEqual("migrated", next((staging.ROOT / "failed").glob("*/data/db")).read_text())

    def test_resume_restore_after_data_directory_was_moved(self):
        self.interrupted_restore(2)

    def test_resume_restore_after_keys_directory_was_moved(self):
        self.interrupted_restore(4)

    def test_standalone_recovery_docker_failure_blocks_next_start_and_can_resume(self):
        (staging.ROOT / "data/db").write_text("old")
        (staging.ROOT / "keys/key").write_text("old key")
        state = {"image": "old-image", "config": "old-config", "compose": "old-compose", "active": False}
        identifier = staging.backup_state(state)
        (staging.ROOT / "data/db").write_text("new schema")
        self.assertFalse((staging.ROOT / "pending.json").exists())
        with patch.object(staging.os, "chown"), patch.object(staging, "docker", side_effect=staging.Refused("synthetic recreate failure")):
            with self.assertRaises(staging.Refused):
                staging.recover(identifier)
        self.assertEqual("container", json.loads((staging.ROOT / "restoring.json").read_text())["phase"])
        start_plan = staging.make_plan("start")
        with patch.object(staging.os, "geteuid", return_value=0), patch.object(staging, "protected_directory"), patch.object(staging, "validate_target"), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "Interrupted update"):
                staging.execute(start_plan)
        docker.assert_not_called()
        with patch.object(staging.os, "chown"), patch.object(staging, "docker") as docker:
            staging.recover(identifier)
        self.assertEqual("old-image", docker.call_args.kwargs["image"])
        self.assertFalse((staging.ROOT / "restoring.json").exists())
        self.assertEqual("old", (staging.ROOT / "data/db").read_text())

    def test_standalone_recovery_readiness_failure_retains_guard(self):
        state = {"image": "old-image", "config": "config", "compose": "compose", "active": True}
        identifier = staging.backup_state(state)
        with patch.object(staging.os, "chown"), patch.object(staging, "docker"), patch.object(staging, "start", side_effect=staging.Refused("synthetic readiness failure")):
            with self.assertRaises(staging.Refused):
                staging.recover(identifier)
        self.assertTrue((staging.ROOT / "restoring.json").exists())


if __name__ == "__main__":
    unittest.main()
