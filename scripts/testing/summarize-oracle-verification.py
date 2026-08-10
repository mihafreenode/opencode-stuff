#!/usr/bin/env python3
"""Validate one Oracle verification test and its structured phase evidence."""

import argparse
import json
import pathlib
import xml.etree.ElementTree as ET


SCHEMA_VERSION = "1"
EVIDENCE_KIND = "oracleVerificationPhaseEvidence"
SUMMARY_KIND = "oracleVerificationSummary"
REQUIRED_PHASE_IDS = (
    "provision",
    "discover-connect",
    "assistant-import-rollback-pull",
    "compiler-driven-repair",
    "cleanup",
)


def fail(message: str) -> None:
    raise SystemExit(message)


def read_phase_evidence(path: pathlib.Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"Could not read phase evidence '{path}': {error}")

    if not isinstance(data, dict):
        fail("Oracle phase evidence must be a JSON object.")
    if data.get("schemaVersion") != SCHEMA_VERSION:
        fail(f"Oracle phase evidence schemaVersion must be '{SCHEMA_VERSION}'.")
    if data.get("kind") != EVIDENCE_KIND:
        fail(f"Oracle phase evidence kind must be '{EVIDENCE_KIND}'.")

    phases = data.get("phases")
    if not isinstance(phases, list):
        fail("Oracle phase evidence phases must be an array.")

    phases_by_id = {}
    for phase in phases:
        if not isinstance(phase, dict) or not isinstance(phase.get("id"), str) or not phase["id"]:
            fail("Every Oracle verification phase must have a non-empty string id.")
        phase_id = phase["id"]
        if phase_id in phases_by_id:
            fail(f"Duplicate Oracle verification phase id '{phase_id}'.")
        phases_by_id[phase_id] = phase

    missing = [phase_id for phase_id in REQUIRED_PHASE_IDS if phase_id not in phases_by_id]
    if missing:
        fail(f"Missing required Oracle verification phases: {', '.join(missing)}.")
    if tuple(phases_by_id) != REQUIRED_PHASE_IDS:
        fail(f"Oracle verification phases must appear exactly once in required order: {', '.join(REQUIRED_PHASE_IDS)}.")

    not_passed = [
        phase_id
        for phase_id, phase in phases_by_id.items()
        if phase.get("status") != "passed"
    ]
    if not_passed:
        fail(f"Oracle verification phases did not pass: {', '.join(not_passed)}.")

    provisioning_count = data.get("provisioningCount")
    if isinstance(provisioning_count, bool) or provisioning_count != 1:
        fail(f"Oracle phase evidence provisioningCount must be 1, got {provisioning_count!r}.")
    if data.get("status") != "passed":
        fail(f"Oracle phase evidence overall status must be 'passed', got {data.get('status')!r}.")

    invariant = data.get("provisioningInvariant")
    if not isinstance(invariant, dict) or invariant.get("expectedCalls") != 1 or invariant.get("actualCalls") != 1 or invariant.get("passed") is not True:
        fail("Oracle phase evidence must contain a passing exactly-one-provision invariant.")
    if data.get("mcpStartup", {}).get("status") != "passed":
        fail("Oracle phase evidence must contain passed packaged MCP startup evidence.")
    provenance = data.get("packageProvenance")
    if not isinstance(provenance, dict) or not provenance.get("packagePath") or not provenance.get("sha256"):
        fail("Oracle phase evidence must contain package path and SHA-256 provenance.")
    if not data.get("startedAt") or not data.get("completedAt"):
        fail("Oracle phase evidence must contain run timestamps.")

    return data


def read_trx_counts(path: pathlib.Path) -> dict:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        fail(f"Could not read TRX '{path}': {error}")

    counters = next((element for element in root.iter() if element.tag.endswith("Counters")), None)
    if counters is None:
        fail(f"TRX '{path}' does not contain result counters.")

    values = {}
    for name in ("total", "executed", "passed", "failed"):
        try:
            values[name] = int(counters.attrib[name])
        except (KeyError, ValueError):
            fail(f"TRX '{path}' has a missing or invalid '{name}' counter.")

    counts = {
        "selected": values["total"],
        "executed": values["executed"],
        "passed": values["passed"],
        "failed": values["failed"],
        "skipped": values["total"] - values["executed"],
    }
    if (
        counts["selected"] != 1
        or counts["executed"] != 1
        or counts["passed"] != 1
        or counts["failed"] != 0
        or counts["skipped"] != 0
    ):
        fail(f"Required Oracle verification test did not pass exactly once: {counts}.")
    return counts


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--trx", action="append", required=True)
    parser.add_argument("--phase-evidence")
    parser.add_argument("--smoke", action="append", default=[])
    parser.add_argument("--expected-selected", type=int)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    if args.phase_evidence is None:
        return summarize_legacy_results(args)
    if args.smoke or args.expected_selected is not None or len(args.trx) != 1:
        fail("Phase verification requires exactly one --trx, one --phase-evidence, and no legacy smoke/count arguments.")

    evidence = read_phase_evidence(pathlib.Path(args.phase_evidence))
    tests = read_trx_counts(pathlib.Path(args.trx[0]))
    phases_by_id = {phase["id"]: phase for phase in evidence["phases"]}
    summary = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": SUMMARY_KIND,
        "status": "passed",
        "provisioningCount": evidence["provisioningCount"],
        "packageProvenance": evidence["packageProvenance"],
        **tests,
        "phases": [phases_by_id[phase_id] for phase_id in REQUIRED_PHASE_IDS],
    }

    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return 0


def summarize_legacy_results(args: argparse.Namespace) -> int:
    if args.expected_selected is None:
        fail("Legacy verification requires --expected-selected.")

    totals = {"selected": 0, "executed": 0, "passed": 0, "failed": 0, "skipped": 0}
    for path_text in args.smoke:
        data = json.loads(pathlib.Path(path_text).read_text(encoding="utf-8"))
        status = str(data.get("status", "")).lower()
        selected = 1 if data.get("kind") == "smokeRun" else len(data.get("selectedTemplates", []))
        passed = int(status == "passed") if data.get("kind") == "smokeRun" else int(data.get("passedCount", 0))
        failed = int(status == "failed") if data.get("kind") == "smokeRun" else int(data.get("failedCount", 0))
        skipped = int(status == "skipped") if data.get("kind") == "smokeRun" else int(data.get("skippedCount", 0))
        totals["selected"] += selected
        totals["executed"] += passed + failed
        totals["passed"] += passed
        totals["failed"] += failed
        totals["skipped"] += skipped

    for path_text in args.trx:
        root = ET.parse(path_text).getroot()
        counters = next(element for element in root.iter() if element.tag.endswith("Counters"))
        total = int(counters.attrib["total"])
        executed = int(counters.attrib["executed"])
        totals["selected"] += total
        totals["executed"] += executed
        totals["passed"] += int(counters.attrib["passed"])
        totals["failed"] += int(counters.attrib["failed"])
        totals["skipped"] += total - executed

    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(totals, indent=2) + "\n", encoding="utf-8")
    if totals["selected"] != args.expected_selected:
        fail(f"Expected {args.expected_selected} selected Oracle tests, got {totals['selected']}.")
    if totals["executed"] != totals["selected"] or totals["failed"] or totals["skipped"]:
        fail(f"Required Oracle verification did not pass cleanly: {totals}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
