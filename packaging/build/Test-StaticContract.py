#!/usr/bin/env python3
"""Cross-platform, non-mutating static checks for Agent 2 packaging artifacts."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
ISS = ROOT / "installer" / "WindowsSshEnabler.iss"
BUILD = ROOT / "build" / "Build-Package.ps1"
README = ROOT / "README.md"
INSTALLED_CHECK = ROOT / "build" / "Test-InstalledState.ps1"


def require(text: str, needle: str, source: Path, errors: list[str]) -> None:
    if needle not in text:
        errors.append(f"{source.name}: missing required text: {needle}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--agent1-root",
        type=Path,
        help="Optional read-only Agent 1 root for basic application contract checks.",
    )
    args = parser.parse_args()
    errors: list[str] = []

    for path in (ISS, BUILD, INSTALLED_CHECK, README):
        if not path.is_file():
            errors.append(f"missing deliverable: {path}")
    if errors:
        print("\n".join(f"ERROR: {error}" for error in errors))
        return 1

    iss = ISS.read_text(encoding="utf-8")
    build = BUILD.read_text(encoding="utf-8")
    readme = README.read_text(encoding="utf-8")

    iss_requirements = (
        "AppId={{B1D84CE8-E5D1-4B27-89E8-A72F1A0A6365}",
        "DefaultDirName={autopf}\\Windows SSH Enabler",
        "ArchitecturesAllowed=x64compatible",
        "ArchitecturesInstallIn64BitMode=x64compatible",
        "PrivilegesRequired=admin",
        "OutputBaseFilename=WindowsSshEnabler-Setup-{#AppVersion}-x64",
        'Name: "{group}\\Windows SSH Enabler"',
        'Name: "{autodesktop}\\Windows SSH Enabler"',
        'Source: "{#AppStageDir}\\{#ProductExe}"',
    )
    for item in iss_requirements:
        require(iss, item, ISS, errors)

    forbidden_sections = (
        "Run",
        "Registry",
        "Tasks",
        "Code",
        "UninstallRun",
        "UninstallDelete",
    )
    for section in forbidden_sections:
        if re.search(rf"(?m)^\s*\[{re.escape(section)}\]\s*$", iss):
            errors.append(f"{ISS.name}: forbidden active section [{section}]")
    source_lines = re.findall(r"(?m)^\s*Source:.*$", iss)
    if len(source_lines) != 1 or "{#ProductExe}" not in source_lines[0]:
        errors.append("installer must have exactly one explicitly named payload")

    build_requirements = (
        "--runtime', 'win-x64'",
        "--self-contained', 'true'",
        "/p:PublishSingleFile=true",
        "Assert-ManifestRequiresAdministrator",
        "Get-PeSubsystem",
        "Assert-AuthenticodeState",
        "usesTestHost",
        "'run', '--project'",
        "artifact-inventory.json",
        "SHA256SUMS.txt",
        "UNSIGNED DEVELOPMENT ARTIFACTS",
    )
    for item in build_requirements:
        require(build, item, BUILD, errors)

    readme_requirements = (
        "Unsigned development status",
        "Windows prerequisites",
        "Uninstall behavior",
        "Future signing integration",
        "Real Windows validation",
    )
    for item in readme_requirements:
        require(readme, item, README, errors)

    installed_check = INSTALLED_CHECK.read_text(encoding="utf-8")
    for item in (
        "requireAdministrator",
        "Get-AuthenticodeSignature",
        "Windows SSH Enabler.lnk",
        "Uninstall\\{B1D84CE8-E5D1-4B27-89E8-A72F1A0A6365}_is1",
    ):
        require(installed_check, item, INSTALLED_CHECK, errors)

    if args.agent1_root:
        root = args.agent1_root.resolve()
        csproj_files = list(root.rglob("*.csproj")) if root.is_dir() else []
        manifest_files = list(root.rglob("*.manifest")) if root.is_dir() else []
        if not csproj_files:
            errors.append(f"Agent 1 root contains no .csproj: {root}")
        else:
            app_projects = [
                path
                for path in csproj_files
                if re.search(
                    r"<OutputType>\s*WinExe\s*</OutputType>",
                    path.read_text(encoding="utf-8-sig", errors="replace"),
                )
            ]
            tests = [
                path
                for path in csproj_files
                if re.search(r"(?i)(test|tests)\.csproj$", path.name)
            ]
            if len(app_projects) != 1:
                errors.append(f"expected one Agent 1 WinExe project, found {len(app_projects)}")
            if not tests:
                errors.append("Agent 1 output has no test project")
            joined_projects = "\n".join(
                path.read_text(encoding="utf-8-sig", errors="replace")
                for path in app_projects
            )
            for required in ("net10.0-windows", "WinExe", "UseWindowsForms"):
                if required not in joined_projects:
                    errors.append(f"Agent 1 app project missing {required}")
        if not manifest_files:
            errors.append("Agent 1 output has no application manifest")
        else:
            manifest_text = "\n".join(
                path.read_text(encoding="utf-8-sig", errors="replace")
                for path in manifest_files
            )
            if 'level="requireAdministrator"' not in manifest_text:
                errors.append("Agent 1 manifest does not request requireAdministrator")

        lock_files = list(root.rglob("packages.lock.json")) if root.is_dir() else []
        if len(lock_files) < len(csproj_files):
            errors.append(
                f"Agent 1 output has {len(lock_files)} lock files for {len(csproj_files)} projects"
            )

        csharp_files = list(root.rglob("*.cs")) if root.is_dir() else []
        csharp = "\n".join(
            path.read_text(encoding="utf-8-sig", errors="replace")
            for path in csharp_files
        )
        for forbidden in (
            "Process.Start(",
            "ProcessStartInfo",
            "System.Management.Automation",
            "cmd.exe",
            "powershell.exe",
        ):
            if forbidden.lower() in csharp.lower():
                errors.append(f"Agent 1 source contains forbidden shell surface: {forbidden}")
        if len(re.findall(r"\bnew\s+Button\b", csharp)) != 1:
            errors.append("Agent 1 UI must construct exactly one primary Button")
        for required in (
            "ReadOnly = true",
            "OpenSSH.Server~~~~0.0.1.0",
            'RuleName = "WindowsSshEnabler.LanOpenSsh.Tcp22"',
            '"LocalSubnet"',
            'ServiceName = "sshd"',
            "EdgeTraversal = false",
            "AfInet6",
        ):
            if required not in csharp:
                errors.append(f"Agent 1 source missing security contract marker: {required}")

    if errors:
        print("Static contract validation FAILED")
        for error in errors:
            print(f"ERROR: {error}")
        return 1

    print("Static contract validation PASSED")
    print(f"Reviewed: {ISS}")
    print(f"Reviewed: {BUILD}")
    print(f"Reviewed: {INSTALLED_CHECK}")
    print(f"Reviewed: {README}")
    if args.agent1_root:
        print(f"Reviewed Agent 1 root (read-only): {args.agent1_root.resolve()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
