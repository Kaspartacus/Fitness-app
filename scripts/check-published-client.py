#!/usr/bin/env python3
"""Check the actual Release artifact and optionally serve its synthetic HTTP host.

No Docker, real accounts, staging storage or external services are used. This is
an asset-delivery regression, not a substitute for a real browser boot check.
"""
import argparse
import base64
import hashlib
from html.parser import HTMLParser
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request


class InvalidPublish(Exception):
    pass


def local_path(url):
    parsed = urllib.parse.urlsplit(url)
    path = urllib.parse.unquote(parsed.path).lstrip("/")
    if not path or parsed.scheme or parsed.netloc or parsed.query or parsed.fragment or "\\" in path or ".." in path.split("/"):
        raise InvalidPublish("Published client asset URL must be a plain local path")
    return str(PurePosixPath(path))


class Shell(HTMLParser):
    def __init__(self):
        super().__init__()
        self.assets = []
        self.import_maps = []
        self.in_import_map = False

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "script" and values.get("src"):
            self.assets.append(local_path(values["src"]))
        if tag == "link" and values.get("rel") in ("stylesheet", "preload"):
            if not values.get("href"):
                raise InvalidPublish("Published HTML contains an unresolved preload placeholder")
            self.assets.append(local_path(values["href"]))
        if tag == "script" and values.get("type") == "importmap":
            self.in_import_map = True
            self.import_maps.append("")

    def handle_data(self, data):
        if self.in_import_map:
            self.import_maps[-1] += data

    def handle_endtag(self, tag):
        if tag == "script":
            self.in_import_map = False


def published_assets(publish):
    web = publish / "wwwroot"

    def read(path):
        if not (web / path).resolve().is_relative_to(web.resolve()):
            raise InvalidPublish("Published client asset points outside its web root")
        return (web / path).read_bytes()

    html = read("index.html").decode()
    if "#[" in html or "{fingerprint}" in html:
        raise InvalidPublish("Published HTML still contains a fingerprint placeholder")
    shell = Shell()
    shell.feed(html)
    boot = [path for path in shell.assets if re.fullmatch(r"_framework/blazor\.webassembly(?:\.[a-z0-9]+)?\.js", path)]
    if len(boot) != 1:
        raise InvalidPublish("Published HTML must reference one resolvable Blazor bootstrap")
    imports = {}
    for value in shell.import_maps:
        if not value.strip():
            raise InvalidPublish("Published HTML contains an empty import map")
        imports.update({local_path(key): local_path(value) for key, value in json.loads(value).get("imports", {}).items()})
    loader = imports.get("_framework/dotnet.js", "_framework/dotnet.js")
    paths = {"index.html", *shell.assets, *imports.values(), loader}
    # .NET 10 embeds the boot resource graph into its generated JS loader.
    loader_text = read(loader).decode()
    match = re.search(r"/\*json-start\*/(.*?)/\*json-end\*/", loader_text, re.S)
    if not match:
        raise InvalidPublish("Published .NET loader has no boot resource configuration")
    config = json.loads(match[1])
    resources = config["resources"]
    for required in ("jsModuleNative", "jsModuleRuntime", "wasmNative", "assembly"):
        if not resources.get(required):
            raise InvalidPublish("Published boot resource graph is incomplete")
    hashes = {}

    def collect(value):
        if isinstance(value, dict):
            if "name" in value:
                path = "_framework/" + local_path(value["name"])
                paths.add(path)
                if value.get("hash", "").startswith("sha256-"):
                    hashes[path] = value["hash"]
            for child in value.values():
                collect(child)
        elif isinstance(value, list):
            for child in value:
                collect(child)

    collect(resources)
    assets = {path: read(path) for path in sorted(paths)}
    for path, contents in assets.items():
        if not contents:
            raise InvalidPublish("Published client contains an empty asset")
        if path.endswith(".wasm") and not contents.startswith(b"\0asm"):
            raise InvalidPublish("Published WebAssembly asset has invalid content")
        if path in hashes and "sha256-" + base64.b64encode(hashlib.sha256(contents).digest()).decode() != hashes[path]:
            raise InvalidPublish("Published boot resource integrity does not match its artifact")
    return assets


def check_http(base_url, assets):
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for path, contents in assets.items():
        route = "" if path == "index.html" else urllib.parse.quote(path)
        with opener.open(base_url + "/" + route, timeout=10) as response:
            if response.status != 200 or response.read() != contents:
                raise InvalidPublish("Published host does not serve the expected client artifact")
            if path == "index.html" and "no-cache" not in response.headers.get("Cache-Control", ""):
                raise InvalidPublish("Published HTML shell must revalidate after updates")
            if path.endswith(".js") and "javascript" not in response.headers.get("Content-Type", "").lower():
                raise InvalidPublish("Published host returned a non-JavaScript bootstrap/module response")
            if path.endswith(".wasm") and response.headers.get_content_type() != "application/wasm":
                raise InvalidPublish("Published host returned an invalid WebAssembly content type")


def serve_and_check(publish, assets):
    # Staging's optional absolute config mount must not inject a real host secret.
    if Path("/run/fitness-staging/settings.json").exists():
        raise InvalidPublish("Run the synthetic publish check outside a configured staging container")
    with tempfile.TemporaryDirectory(prefix="fitness-published-client-check-") as temporary:
        root = Path(temporary)
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            port = reservation.getsockname()[1]
        base = f"http://127.0.0.1:{port}"
        env = {"PATH": os.environ.get("PATH", os.defpath), "DOTNET_CLI_HOME": temporary,
               "ASPNETCORE_ENVIRONMENT": "Staging", "DOTNET_ENVIRONMENT": "Staging",
               "ASPNETCORE_URLS": base, "AllowedHosts": "127.0.0.1;localhost",
               "ConnectionStrings__DefaultConnection": f"Data Source={root / 'synthetic.db'}",
               "DataProtection__KeyRingPath": str(root / "keys"),
               "Email__PickupDirectory": str(root / "mail"),
               "Authentication__Jwt__SigningKey": base64.b64encode(os.urandom(48)).decode(),
               "PublicApp__BaseUrl": "https://localhost"}
        command = [shutil.which("dotnet") or "dotnet", str(publish / "FitnessApp.Server.dll")]
        with (root / "synthetic-host.log").open("wb") as log:
            subprocess.run([*command, "migrate-staging"], cwd=publish, env=env,
                           stdout=log, stderr=subprocess.STDOUT, check=True, timeout=90)
            host = subprocess.Popen(command, cwd=publish, env=env, stdout=log, stderr=subprocess.STDOUT)
            try:
                opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
                for _ in range(100):
                    if host.poll() is not None:
                        raise InvalidPublish("Synthetic published host exited before readiness")
                    try:
                        with opener.open(base + "/health/ready", timeout=1) as response:
                            if response.status == 200:
                                break
                    except (OSError, urllib.error.URLError):
                        time.sleep(0.1)
                else:
                    raise InvalidPublish("Synthetic published host did not become ready")
                check_http(base, assets)
            finally:
                host.terminate()
                try:
                    host.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    host.kill()
                    host.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("publish", type=Path)
    parser.add_argument("--serve", action="store_true", help="Serve the real published host with temporary synthetic SQLite/keys")
    args = parser.parse_args()
    try:
        publish = args.publish.resolve()
        assets = published_assets(publish)
        if args.serve:
            serve_and_check(publish, assets)
        print(f"Published client boot graph verified: {len(assets)} assets" + (" through the actual host" if args.serve else ""))
        return 0
    except (InvalidPublish, OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print(str(error) if isinstance(error, InvalidPublish) else "Published client boot verification failed (missing/invalid artifact or synthetic host failure)")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
