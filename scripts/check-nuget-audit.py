#!/usr/bin/env python3
"""Validate structured `dotnet package list --vulnerable` output."""

from __future__ import annotations

import json
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import NoReturn

CLEAN = 0
FINDINGS = 2
INCOMPLETE = 3


def incomplete(message: str) -> NoReturn:
    print(f"Dependency audit incomplete: {message}", file=sys.stderr)
    raise SystemExit(INCOMPLETE)


def project_paths(solution_path: Path) -> set[Path]:
    try:
        root = ET.parse(solution_path).getroot()
    except (OSError, ET.ParseError) as error:
        incomplete(f"could not read the solution: {error}")

    paths = [
        (solution_path.parent / element.attrib["Path"]).resolve()
        for element in root.iter()
        if element.tag.endswith("Project") and "Path" in element.attrib
    ]
    if not paths:
        incomplete("the solution contains no projects")
    if len(paths) != len(set(paths)):
        incomplete("the solution contains duplicate project paths")
    return set(paths)


def audited_project_paths(document: dict, solution_path: Path) -> set[Path]:
    projects = document.get("projects")
    if not isinstance(projects, list) or not projects:
        incomplete("structured output contains no projects")

    paths: list[Path] = []
    for project in projects:
        if not isinstance(project, dict) or not isinstance(project.get("path"), str):
            incomplete("structured output contains a project without a path")
        path = Path(project["path"])
        paths.append((path if path.is_absolute() else solution_path.parent / path).resolve())

    if len(paths) != len(set(paths)):
        incomplete("structured output contains duplicate project paths")
    return set(paths)


def has_vulnerabilities(value: object) -> bool:
    if isinstance(value, dict):
        for key, child in value.items():
            if key.lower() == "vulnerabilities":
                if not isinstance(child, list):
                    incomplete("structured output contains an invalid vulnerabilities value")
                if child:
                    return True
            if has_vulnerabilities(child):
                return True
    elif isinstance(value, list):
        return any(has_vulnerabilities(child) for child in value)
    return False


def validate(solution_path: Path, audit_path: Path, diagnostics_path: Path) -> int:
    solution_path = solution_path.resolve()

    try:
        diagnostics = diagnostics_path.read_text(encoding="utf-8")
    except OSError as error:
        incomplete(f"could not read audit diagnostics: {error}")

    if diagnostics.strip():
        incomplete("the package command emitted diagnostics")

    try:
        document = json.loads(audit_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        incomplete(f"structured output is unavailable or invalid: {error}")

    if not isinstance(document, dict):
        incomplete("structured output is not a JSON object")

    if document.get("version") != 1:
        incomplete("structured output has an unexpected version")

    parameters = str(document.get("parameters", "")).split()
    if "--vulnerable" not in parameters or "--include-transitive" not in parameters:
        incomplete("structured output does not confirm the required audit parameters")

    sources = document.get("sources")
    if not isinstance(sources, list) or not sources or not all(isinstance(source, str) for source in sources):
        incomplete("structured output does not identify an advisory source")

    expected = project_paths(solution_path)
    audited = audited_project_paths(document, solution_path)
    if expected != audited:
        solution_root = solution_path.parent

        def safe_display(path: Path) -> str:
            try:
                return path.relative_to(solution_root).as_posix()
            except ValueError:
                return f"<outside-solution>/{path.name}"

        missing = sorted(safe_display(path) for path in expected - audited)
        unexpected = sorted(safe_display(path) for path in audited - expected)
        details = []
        if missing:
            details.append(f"missing projects: {', '.join(missing)}")
        if unexpected:
            details.append(f"unexpected projects: {', '.join(unexpected)}")
        incomplete("project coverage mismatch (" + "; ".join(details) + ")")

    if has_vulnerabilities(document):
        print("Dependency audit found vulnerable packages.", file=sys.stderr)
        return FINDINGS

    print(
        f"Dependency audit complete: no known vulnerable packages in all {len(expected)} solution projects."
    )
    return CLEAN


def self_test() -> int:
    with tempfile.TemporaryDirectory(prefix="fitnessapp-audit-self-test-") as directory:
        root = Path(directory)
        solution = root / "Fixture.slnx"
        solution.write_text(
            '<Solution><Project Path="One/One.csproj" /><Project Path="Two/Two.csproj" /></Solution>',
            encoding="utf-8",
        )
        diagnostics = root / "diagnostics.txt"
        diagnostics.write_text("", encoding="utf-8")
        audit = root / "audit.json"
        base_document = {
            "version": 1,
            "parameters": "--vulnerable --include-transitive",
            "sources": ["https://example.invalid/v3/index.json"],
            "projects": [
                {"path": "One/One.csproj"},
                {"path": "Two/Two.csproj"},
            ],
        }

        audit.write_text(json.dumps(base_document), encoding="utf-8")
        if validate(solution, audit, diagnostics) != CLEAN:
            return 1

        # The pinned SDK omits framework/package collections for projects with
        # no vulnerability findings; paths plus the audit metadata are the
        # complete clean-result shape.

        audit.write_text("{", encoding="utf-8")
        try:
            validate(solution, audit, diagnostics)
        except SystemExit as error:
            if error.code != INCOMPLETE:
                return 1
        else:
            return 1

        audit.write_text(json.dumps(base_document), encoding="utf-8")

        findings_document = json.loads(json.dumps(base_document))
        findings_document["projects"][0]["frameworks"] = [
            {"topLevelPackages": [{"vulnerabilities": [{"severity": "High"}]}]}
        ]
        audit.write_text(json.dumps(findings_document), encoding="utf-8")
        if validate(solution, audit, diagnostics) != FINDINGS:
            return 1

        invalid_findings_document = json.loads(json.dumps(base_document))
        invalid_findings_document["projects"][0]["vulnerabilities"] = {}
        audit.write_text(json.dumps(invalid_findings_document), encoding="utf-8")
        try:
            validate(solution, audit, diagnostics)
        except SystemExit as error:
            if error.code != INCOMPLETE:
                return 1
        else:
            return 1

        audit.write_text(
            json.dumps({**base_document, "projects": base_document["projects"][:1]}),
            encoding="utf-8",
        )
        try:
            validate(solution, audit, diagnostics)
        except SystemExit as error:
            if error.code != INCOMPLETE:
                return 1
        else:
            return 1

        diagnostics.write_text("unrecognized package diagnostic", encoding="utf-8")
        audit.write_text(json.dumps(base_document), encoding="utf-8")
        try:
            validate(solution, audit, diagnostics)
        except SystemExit as error:
            if error.code != INCOMPLETE:
                return 1
        else:
            return 1

    print("Audit parser self-test passed: findings and incomplete results fail nonzero.")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    if len(sys.argv) != 4:
        print(
            f"Usage: {sys.argv[0]} SOLUTION AUDIT_JSON DIAGNOSTICS",
            file=sys.stderr,
        )
        return 64
    return validate(Path(sys.argv[1]).resolve(), Path(sys.argv[2]), Path(sys.argv[3]))


if __name__ == "__main__":
    raise SystemExit(main())
