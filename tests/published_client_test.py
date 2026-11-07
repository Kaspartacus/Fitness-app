"""Published-artifact regressions; no source-property assertions or Docker."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("published", Path(__file__).resolve().parents[1] / "scripts/check-published-client.py")
published = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(published)


class PublishedClientTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.web = self.root / "wwwroot"
        (self.web / "_framework").mkdir(parents=True)
        self.wasm = b"\0asmSynthetic"
        config = {"resources": {
            "jsModuleNative": [{"name": "native.abc.js"}],
            "jsModuleRuntime": [{"name": "runtime.abc.js"}],
            "wasmNative": [{"name": "native.abc.wasm", "hash": "sha256-" + base64.b64encode(hashlib.sha256(self.wasm).digest()).decode()}],
            "assembly": [{"name": "Client.abc.wasm"}]}}
        self.write("index.html", '<script src="_framework/blazor.webassembly.js"></script>')
        self.write("_framework/blazor.webassembly.js", "synthetic-bootstrap")
        self.write("_framework/dotnet.js", "/*json-start*/" + json.dumps(config) + "/*json-end*/")
        self.write("_framework/native.abc.js", "synthetic-native")
        self.write("_framework/runtime.abc.js", "synthetic-runtime")
        (self.web / "_framework/native.abc.wasm").write_bytes(self.wasm)
        (self.web / "_framework/Client.abc.wasm").write_bytes(self.wasm)

    def write(self, name, contents):
        (self.web / name).write_text(contents)

    def test_complete_runtime_and_fingerprinted_assemblies_are_followed(self):
        assets = published.published_assets(self.root)
        self.assertIn("_framework/Client.abc.wasm", assets)
        self.assertIn("_framework/native.abc.js", assets)
        self.assertEqual(7, len(assets))

    def test_literal_placeholder_that_broke_staging_is_rejected(self):
        self.write("index.html", '<script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>')
        with self.assertRaises(published.InvalidPublish):
            published.published_assets(self.root)

    def test_missing_import_map_target_cannot_be_hidden_by_html_success(self):
        (self.web / "_framework/dotnet.js").unlink()
        with self.assertRaises(FileNotFoundError):
            published.published_assets(self.root)

    def test_valid_import_map_can_resolve_fingerprinted_entrypoints(self):
        (self.web / "_framework/blazor.webassembly.js").rename(self.web / "_framework/blazor.webassembly.abc.js")
        (self.web / "_framework/dotnet.js").rename(self.web / "_framework/dotnet.abc.js")
        self.write("index.html", '<script type="importmap">{"imports":{"./_framework/dotnet.js":"./_framework/dotnet.abc.js"}}</script>'
                   '<script src="_framework/blazor.webassembly.abc.js"></script>')
        self.assertIn("_framework/dotnet.abc.js", published.published_assets(self.root))

    def test_missing_transitive_wasm_resource_is_rejected(self):
        (self.web / "_framework/Client.abc.wasm").unlink()
        with self.assertRaises(FileNotFoundError):
            published.published_assets(self.root)

    def test_resource_integrity_mismatch_is_rejected(self):
        (self.web / "_framework/native.abc.wasm").write_bytes(b"\0asmWrong")
        with self.assertRaises(published.InvalidPublish):
            published.published_assets(self.root)

    def test_empty_importmap_and_preload_placeholders_are_rejected(self):
        for placeholder in ('<script type="importmap"></script>', '<link rel="preload" id="webassembly" />'):
            self.write("index.html", placeholder + '<script src="_framework/blazor.webassembly.js"></script>')
            with self.assertRaises(published.InvalidPublish):
                published.published_assets(self.root)

    def test_asset_urls_cannot_fetch_external_hosts_or_traverse(self):
        for url in ("https://example.test/asset.js", "//example.test/asset.js", "../private", "%2e%2e/private", "file:///private"):
            with self.assertRaises(published.InvalidPublish):
                published.local_path(url)
