"""Synthetic staging transactions: no Docker daemon, privileged writes or real data."""
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from contextlib import redirect_stdout, redirect_stderr
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
                            ("BACKUPS", self.root / "backups"), ("CONFIG", self.root / "config")):
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
        with patch.object(staging, "docker") as docker, patch.object(staging, "start") as start:
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
        with patch.object(staging, "docker"), patch.object(staging, "start") as start:
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
        with patch.object(staging, "docker", side_effect=docker), patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop"), patch.object(staging, "backup_state", return_value="b" * 32), patch.object(staging, "recover") as recover, patch.object(staging, "start") as start:
            with self.assertRaises(staging.Refused):
                staging.deploy(plan)
        recover.assert_called_once_with("b" * 32)
        start.assert_not_called()
        self.assertFalse((staging.ROOT / "pending.json").exists())

    def test_health_failure_restores_pair_instead_of_old_image_new_schema(self):
        plan = self.deploy_plan()
        with patch.object(staging, "docker", side_effect=self.fake_docker), patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop") as stop, patch.object(staging, "backup_state", return_value="b" * 32), patch.object(staging, "recover") as recover, patch.object(staging, "start", side_effect=staging.Refused("synthetic health failure")):
            with self.assertRaises(staging.Refused):
                staging.deploy(plan)
        recover.assert_called_once_with("b" * 32)
        self.assertGreaterEqual(stop.call_count, 2)

    def test_failed_recovery_preserves_pending_journal_for_explicit_recovery(self):
        plan = self.deploy_plan()
        with patch.object(staging, "docker", side_effect=self.fake_docker), patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop"), patch.object(staging, "backup_state", return_value="b" * 32), patch.object(staging, "recover", side_effect=staging.Refused("synthetic recovery failure")), patch.object(staging, "start", side_effect=staging.Refused("synthetic health failure")):
            with self.assertRaisesRegex(staging.Refused, "recovery incomplete"):
                staging.deploy(plan)
        self.assertEqual("b" * 32, json.loads((staging.ROOT / "pending.json").read_text())["backup"])

    def test_failed_backup_restarts_previous_without_running_migrations(self):
        plan = self.deploy_plan()
        with patch.object(staging, "docker", side_effect=self.fake_docker) as docker, patch.object(staging, "running", side_effect=[True, False]), patch.object(staging, "stop"), patch.object(staging, "backup_state", side_effect=staging.Refused("synthetic backup failure")), patch.object(staging, "start") as start:
            with self.assertRaises(staging.Refused):
                staging.deploy(plan)
        start.assert_called_once_with(plan["state"])
        self.assertFalse(any(call.args[0] == "run" for call in docker.call_args_list))

    def test_unsupported_feature_snapshot_rejected_before_build(self):
        plan = staging.make_plan("deploy", "feature/synthetic")
        with patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "does not support staging"):
                staging.deploy(plan)
        docker.assert_not_called()

    def test_changed_plan_refuses_before_mutation(self):
        plan = staging.make_plan("stop")
        self.git("checkout", "-qb", "feature/moved")
        with patch.object(staging, "private_directory"), patch.object(staging, "validate_target"), patch.object(staging, "make_plan", return_value={**plan, "state": {"image": "changed"}}), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "changed since human approval"):
                staging.execute(plan)
        docker.assert_not_called()

    def test_low_disk_space_refuses_before_build_or_stop(self):
        plan = staging.make_plan("deploy", "feature/synthetic")
        with patch.object(staging.shutil, "disk_usage", return_value=SimpleNamespace(free=0)), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "headroom"):
                staging.deploy(plan)
        docker.assert_not_called()

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
        with patch.object(staging.os, "rename", side_effect=interrupted), patch.object(staging, "docker"):
            with self.assertRaises(OSError):
                staging.recover(identifier)
        self.assertTrue((staging.ROOT / "restoring.json").exists())
        with patch.object(staging, "docker"):
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
        with patch.object(staging, "docker", side_effect=staging.Refused("synthetic recreate failure")):
            with self.assertRaises(staging.Refused):
                staging.recover(identifier)
        self.assertEqual("container", json.loads((staging.ROOT / "restoring.json").read_text())["phase"])
        start_plan = staging.make_plan("start")
        with patch.object(staging, "private_directory"), patch.object(staging, "validate_target"), patch.object(staging, "docker") as docker:
            with self.assertRaisesRegex(staging.Refused, "Interrupted update"):
                staging.execute(start_plan)
        docker.assert_not_called()
        with patch.object(staging, "docker") as docker:
            staging.recover(identifier)
        self.assertEqual("old-image", docker.call_args.kwargs["image"])
        self.assertFalse((staging.ROOT / "restoring.json").exists())
        self.assertEqual("old", (staging.ROOT / "data/db").read_text())

    def test_standalone_recovery_readiness_failure_retains_guard(self):
        state = {"image": "old-image", "config": "config", "compose": "compose", "active": True}
        identifier = staging.backup_state(state)
        with patch.object(staging, "docker"), patch.object(staging, "start", side_effect=staging.Refused("synthetic readiness failure")):
            with self.assertRaises(staging.Refused):
                staging.recover(identifier)
        self.assertTrue((staging.ROOT / "restoring.json").exists())

    def test_nonterminal_mutation_is_refused_without_docker(self):
        with patch.object(staging.sys, "argv", ["staging.py", "stop"]), patch.object(staging, "validate_target"), patch.object(staging.sys.stdin, "isatty", return_value=False), patch.object(staging, "execute") as execute, redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(1, staging.main())
        execute.assert_not_called()

    def test_wrong_confirmation_is_refused_without_docker(self):
        with patch.object(staging.sys, "argv", ["staging.py", "stop"]), patch.object(staging, "validate_target"), patch.object(staging.sys.stdin, "isatty", return_value=True), patch("builtins.input", return_value="yes"), patch.object(staging, "execute") as execute, redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(1, staging.main())
        execute.assert_not_called()

    def test_terminal_confirmation_binds_exact_operation_plan(self):
        plan = staging.make_plan("stop")
        phrase = f"stop {staging.sha(staging.encoded(plan))[:16]}"
        with patch.object(staging.sys, "argv", ["staging.py", "stop"]), patch.object(staging, "validate_target"), patch.object(staging.sys.stdin, "isatty", return_value=True), patch("builtins.input", return_value=phrase), patch.object(staging, "execute") as execute, redirect_stdout(io.StringIO()):
            self.assertEqual(0, staging.main())
        execute.assert_called_once_with(plan)

    def test_confirmation_cannot_authorize_a_changed_plan(self):
        plan = staging.make_plan("stop")
        phrase = f"stop {staging.sha(staging.encoded(plan))[:16]}"
        changed = {**plan, "state": {"commit": "changed"}}
        with patch.object(staging.sys.stdin, "isatty", return_value=True), patch("builtins.input", return_value=phrase), self.assertRaises(staging.Refused):
            staging.confirm(changed)

    def test_confirmation_eof_refuses(self):
        with patch.object(staging.sys.stdin, "isatty", return_value=True), patch("builtins.input", side_effect=EOFError), self.assertRaises(staging.Refused):
            staging.confirm(staging.make_plan("stop"))

    def test_status_is_direct_read_only_without_confirmation(self):
        with patch.object(staging.sys, "argv", ["staging.py", "status"]), patch.object(staging, "validate_target"), patch.object(staging, "docker", return_value=b"synthetic status\n") as docker, patch.object(staging, "confirm") as confirm, redirect_stdout(io.StringIO()):
            self.assertEqual(0, staging.main())
        self.assertEqual("ps", docker.call_args.args[0])
        confirm.assert_not_called()

    def test_plan_is_read_only_without_confirmation_or_docker(self):
        with patch.object(staging.sys, "argv", ["staging.py", "plan", "--operation", "stop"]), patch.object(staging, "validate_target"), patch.object(staging, "docker") as docker, patch.object(staging, "confirm") as confirm, redirect_stdout(io.StringIO()):
            self.assertEqual(0, staging.main())
        docker.assert_not_called()
        confirm.assert_not_called()

    def test_root_execution_is_refused(self):
        with patch.object(staging.os, "geteuid", return_value=0), patch.object(staging, "run") as run, self.assertRaises(staging.Refused):
            staging.git("status")
        run.assert_not_called()

    def test_apps_hostname_is_refused_without_docker(self):
        with patch.object(staging.socket, "gethostname", return_value="apps"), patch.object(staging, "docker") as docker, self.assertRaises(staging.Refused):
            staging.validate_target()
        docker.assert_not_called()

    def test_wrong_host_address_is_refused_without_docker(self):
        with patch.object(staging.socket, "gethostname", return_value="dev"), patch.object(staging.shutil, "which", return_value="/usr/sbin/ip"), patch.object(staging, "run", return_value=b'[{"addr_info":[{"local":"192.168.1.25"}]}]'), self.assertRaises(staging.Refused):
            staging.validate_target()

    def test_private_storage_rejects_public_directory(self):
        directory = self.root / "public-storage"
        directory.mkdir(mode=0o755)
        directory.chmod(0o755)
        with self.assertRaisesRegex(staging.Refused, "mode 0700"):
            staging.private_directory(directory)

    def test_private_storage_rejects_symlink(self):
        directory = self.root / "private-link"
        directory.symlink_to(staging.ROOT, target_is_directory=True)
        with self.assertRaises(staging.Refused):
            staging.private_directory(directory)

    def test_planned_transaction_names_exact_resources_and_recovery_actions(self):
        plan = self.deploy_plan()
        self.assertEqual("fitness-app-staging-app-1", plan["resources"]["container"])
        self.assertEqual("fitness-app-staging_default", plan["resources"]["network"])
        self.assertEqual(f"fitness-app-staging:{plan['source']['commit']}", plan["resources"]["image"])
        self.assertTrue(any("on failure" in action and "remove failed first-deploy" in action for action in plan["actions"]))

    def test_recovery_plan_shows_backup_deployment_not_current_deployment(self):
        config = staging.ROOT / "releases/old/config"
        config.mkdir(parents=True)
        (config / "settings.json").write_text("synthetic-private-old-settings")
        (config / "server.pfx").write_text("synthetic-private-old-certificate")
        old = {"branch": "feature/old", "commit": "a" * 40, "image": "sha256:" + "a" * 64,
               "config": str(config), "compose": "old-compose", "active": True}
        identifier = staging.backup_state(old)
        current = {"branch": "feature/current", "commit": "b" * 40, "image": "sha256:" + "b" * 64,
                   "config": "current-config", "compose": "current-compose", "active": False}
        staging.save_state(current)
        plan = staging.make_plan("recover", backup=identifier)
        self.assertEqual(old, plan["restore"]["deployment"])
        self.assertEqual(current, plan["state"])
        self.assertEqual(old["image"], plan["resources"]["image"])
        self.assertTrue(plan["restore"]["start_after_restore"])
        self.assertIn("start restored deployment and verify readiness", plan["actions"])
        self.assertEqual(staging.sha(b"synthetic-private-old-settings"), plan["restore"]["configuration_fingerprints"]["settings.json"])
        self.assertNotIn("synthetic-private-old-settings", staging.encoded(plan).decode())
        self.assertNotIn("synthetic-private-old-certificate", staging.encoded(plan).decode())

    def test_first_deploy_recovery_plan_explicitly_removes_candidate_without_running_image(self):
        identifier = staging.backup_state(None)
        staging.save_state({"image": "failed-candidate", "active": True})
        plan = staging.make_plan("recover", backup=identifier)
        self.assertIsNone(plan["resources"]["image"])
        self.assertIsNone(plan["restore"]["deployment"])
        self.assertFalse(plan["restore"]["start_after_restore"])
        self.assertIn("remove failed staging container; restore empty storage; do not start any image", plan["actions"])
        self.assertEqual("leave staging stopped", plan["actions"][-1])

class RetentionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in ("backups", "data", "keys", "releases", "failed"):
            (self.root / name).mkdir()
        for name, value in (("ROOT", self.root), ("BACKUPS", self.root / "backups")):
            mock = patch.object(staging, name, value); mock.start(); self.addCleanup(mock.stop)
        self.release = self.root / "releases" / ("a" * 32)
        self.release.mkdir()
        self.state = {"image": "sha256:live", "config": str(self.release / "config"),
                      "compose": str(self.release / "compose.yaml"), "active": True}
        staging.save_state(self.state)

    def backup(self, number, state=None):
        identifier = staging.backup_state(state)
        path = staging.BACKUPS / identifier
        staging.os.utime(path, (number, number))
        return path

    def test_five_latest_pairs_and_their_releases_and_images_survive(self):
        old = self.backup(1)
        for n in range(2, 7):
            self.backup(n, self.state)
        unused = self.root / "releases" / ("b" * 32); unused.mkdir()
        with patch.object(staging, "docker", return_value=b""):
            plan = staging.cleanup_plan()
            self.assertEqual(sorted([str(old), str(unused)]), plan["directories"])
            staging.cleanup(plan)
        self.assertTrue(self.release.exists())
        self.assertTrue((self.root / "data").exists())
        self.assertTrue((self.root / "keys").exists())
        self.assertEqual(5, len(list(staging.BACKUPS.iterdir())))

    def test_incomplete_or_corrupt_backup_blocks_all_deletion(self):
        path = self.backup(1)
        (path / "data" / "changed").write_text("synthetic")
        with patch.object(staging, "docker") as docker, self.assertRaises(staging.Refused):
            staging.cleanup_plan()
        docker.assert_not_called()

    def test_all_pending_transactions_block_cleanup(self):
        for name in ("pending.json", "restoring.json", "import-pending.json"):
            path = self.root / name; path.write_text("{}")
            with self.assertRaises(staging.Refused): staging.cleanup_plan()
            path.unlink()

    def test_symlink_cannot_escape_cleanup_root(self):
        (self.root / "failed" / ("c" * 32)).symlink_to(self.release, target_is_directory=True)
        with self.assertRaises(staging.Refused): staging.cleanup_plan()
        self.assertTrue(self.release.exists())

    def test_only_unused_staging_tags_removed_and_stopped_containers_protected(self):
        keep = "fitness-app-staging:" + "1" * 40
        remove = "fitness-app-staging:" + "2" * 40
        def docker(*args, **kwargs):
            if args[:2] == ("ps", "-aq"): return b"stopped"
            if args[0] == "inspect": return b'"sha256:stopped"'
            if args[:2] == ("image", "ls") and "dangling=true" in args: return b""
            if args[:2] == ("image", "ls"): return (keep + "\n" + remove + "\nother:latest").encode()
            if args[:2] == ("image", "inspect"):
                return b"sha256:stopped" if args[-1] == keep else b"sha256:unused"
            return b""
        with patch.object(staging, "docker", side_effect=docker) as mocked:
            plan = staging.cleanup_plan()
            self.assertEqual([{"tag": remove, "id": "sha256:unused"}], plan["images"])
            staging.cleanup(plan)
            mocked.assert_any_call("image", "rm", remove)
            self.assertFalse(any(call.args[:2] == ("image", "rm") and call.args[-1] == keep for call in mocked.call_args_list))

    def test_new_backup_invalidates_approved_cleanup_list(self):
        with patch.object(staging, "docker", return_value=b""):
            plan = staging.cleanup_plan()
            orphan = self.root / "releases" / ("d" * 32); orphan.mkdir()
            with self.assertRaises(staging.Refused): staging.cleanup(plan)
            self.assertTrue(orphan.exists())

    def test_owned_dangling_images_removed_but_unowned_images_not_queried(self):
        identity = "sha256:" + "e" * 64
        def docker(*args, **kwargs):
            return identity.encode() if "dangling=true" in args else b""
        with patch.object(staging, "docker", side_effect=docker):
            plan = staging.cleanup_plan()
        self.assertEqual([{"tag": identity, "id": identity}], plan["images"])

    def test_cleanup_plan_exposes_actions_without_source_changes(self):
        with patch.object(staging, "file_hashes", return_value={}), patch.object(staging, "docker", return_value=b""):
            plan = staging.make_plan("cleanup")
        self.assertIn("cleanup", plan)
        self.assertIn("preserve live state", plan["actions"][0])

    def test_maintenance_failure_does_not_fail_successful_operation(self):
        with patch.object(staging, "cleanup_plan", side_effect=staging.Refused("synthetic")), redirect_stderr(io.StringIO()):
            staging.maintain_retention()

if __name__ == "__main__":
    unittest.main()
