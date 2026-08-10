#!/usr/bin/env python3
"""Load the RC5 Oracle toolchain contract and verify operator-supplied inputs."""

import argparse
import hashlib
import json
import pathlib
import subprocess


SCHEMA_VERSION = "1"
KIND = "oracleVerificationToolchain"


def fail(message: str) -> None:
    raise SystemExit(message)


def require_string(value: object, field: str) -> str:
    if not isinstance(value, str) or not value:
        fail(f"Oracle toolchain provenance field '{field}' must be a non-empty string.")
    return value


def load_provenance(path: pathlib.Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"Could not read Oracle toolchain provenance '{path}': {error}")
    if not isinstance(data, dict) or data.get("schemaVersion") != SCHEMA_VERSION or data.get("kind") != KIND:
        fail(f"Oracle toolchain provenance must be {KIND} schema version {SCHEMA_VERSION}.")

    required = {
        "sqlcl": ("version", "build", "archiveFilename", "downloadUrl", "sha256"),
        "apex": ("version", "mediaFilename", "downloadUrl", "sha256"),
        "oracleDatabase": ("version", "image", "tag", "digest"),
        "ords": ("version", "image", "tag", "digest"),
    }
    for section_name, fields in required.items():
        section = data.get(section_name)
        if not isinstance(section, dict):
            fail(f"Oracle toolchain provenance section '{section_name}' must be an object.")
        for field in fields:
            require_string(section.get(field), f"{section_name}.{field}")
    return data


def image_reference(section: dict) -> str:
    return f"{section['image']}:{section['tag']}@{section['digest']}"


def verify_catalog(provenance: dict, repository_root: pathlib.Path) -> None:
    expected = {
        repository_root / "catalog/services/oracle-demo.yaml": image_reference(provenance["oracleDatabase"]),
        repository_root / "catalog/services/oracle-ords.yaml": image_reference(provenance["ords"]),
    }
    for path, reference in expected.items():
        if f"image: {reference}" not in path.read_text(encoding="utf-8"):
            fail(f"Catalog service '{path}' does not use pinned image '{reference}'.")


def resolve_remote_digest(section: dict) -> str:
    reference = f"{section['image']}:{section['tag']}"
    command = ["docker", "buildx", "imagetools", "inspect", reference, "--format", "{{json .Manifest}}"]
    result = subprocess.run(command, check=False, capture_output=True, text=True, timeout=120)
    if result.returncode != 0:
        fail(f"Could not resolve pinned container image '{reference}': {result.stderr.strip()}")
    try:
        digest = json.loads(result.stdout)["digest"]
    except (json.JSONDecodeError, KeyError, TypeError):
        fail(f"Container image '{reference}' did not return a manifest digest.")
    return require_string(digest, f"{reference}.digest")


def verify_images(provenance: dict) -> None:
    for section_name in ("oracleDatabase", "ords"):
        section = provenance[section_name]
        actual = resolve_remote_digest(section)
        if actual.lower() != section["digest"].lower():
            fail(f"Container image '{section['image']}:{section['tag']}' resolved to {actual}, expected {section['digest']}.")


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def locate_apex_media(provenance: dict, roots: list[pathlib.Path]) -> pathlib.Path:
    expected = provenance["apex"]["mediaFilename"]
    matches = [root / expected for root in roots if (root / expected).is_file()]
    if not matches:
        fail(f"Pinned APEX media '{expected}' was not found in any configured media root.")
    media = matches[0]
    actual = sha256(media)
    if actual.lower() != provenance["apex"]["sha256"].lower():
        fail(f"Pinned APEX media '{expected}' has SHA-256 {actual}, expected {provenance['apex']['sha256']}.")
    return media


def environment_values(provenance: dict) -> dict[str, str]:
    sqlcl = provenance["sqlcl"]
    apex = provenance["apex"]
    return {
        "OPENCODE_ORACLE_SQLCL_VERSION": sqlcl["version"],
        "OPENCODE_ORACLE_SQLCL_BUILD": sqlcl["build"],
        "OPENCODE_ORACLE_SQLCL_ARCHIVE_FILENAME": sqlcl["archiveFilename"],
        "OPENCODE_ORACLE_SQLCL_DOWNLOAD_URL": sqlcl["downloadUrl"],
        "OPENCODE_ORACLE_SQLCL_SHA256": sqlcl["sha256"],
        "OPENCODE_ORACLE_APEX_VERSION": apex["version"],
        "OPENCODE_ORACLE_APEX_MEDIA_FILENAME": apex["mediaFilename"],
        "OPENCODE_ORACLE_APEX_MEDIA_SHA256": apex["sha256"],
        "OPENCODE_ORACLE_DATABASE_VERSION": provenance["oracleDatabase"]["version"],
        "OPENCODE_ORACLE_DATABASE_IMAGE": image_reference(provenance["oracleDatabase"]),
        "OPENCODE_ORACLE_ORDS_VERSION": provenance["ords"]["version"],
        "OPENCODE_ORACLE_ORDS_IMAGE": image_reference(provenance["ords"]),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--provenance", required=True)
    parser.add_argument("--repository-root", required=True)
    parser.add_argument("--apex-search-root", action="append", default=[])
    parser.add_argument("--verify-live", action="store_true")
    parser.add_argument("--github-env")
    args = parser.parse_args()

    provenance = load_provenance(pathlib.Path(args.provenance))
    verify_catalog(provenance, pathlib.Path(args.repository_root))
    media = None
    if args.verify_live:
        verify_images(provenance)
        media = locate_apex_media(provenance, [pathlib.Path(root) for root in args.apex_search_root if root])

    values = environment_values(provenance)
    if args.github_env:
        with pathlib.Path(args.github_env).open("a", encoding="utf-8", newline="\n") as stream:
            for name, value in values.items():
                stream.write(f"{name}={value}\n")

    summary = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "oracleVerificationToolchainCheck",
        "status": "passed",
        "liveInputsVerified": args.verify_live,
        "apexMedia": str(media) if media else None,
        "versions": {
            "sqlcl": f"{provenance['sqlcl']['version']} build {provenance['sqlcl']['build']}",
            "apex": provenance["apex"]["version"],
            "oracleDatabase": provenance["oracleDatabase"]["version"],
            "ords": provenance["ords"]["version"],
        },
    }
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
