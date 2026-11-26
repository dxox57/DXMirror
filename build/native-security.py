"""Inventory the native receiver and check advisories for the exact UxPlay build."""

import argparse
import json
import os
import re
from pathlib import Path
from urllib.request import Request, urlopen

MSYS2_GITHUB = "https://github.com/msys2"
UXPLAY_API = "https://api.github.com/repos/FDH2/UxPlay/security-advisories"
# GStreamer also publishes vulnerabilities against its monorepo product name.
GSTREAMER_CPE = "cpe:/a:gstreamer:gstreamer:"


def read_url(url, headers=None):
    with urlopen(Request(url, headers=headers or {}), timeout=60) as response:
        return json.load(response), response.headers.get("Link", "")


def native_sbom(receiver):
    lines = (receiver / "licenses/native-inventory.tsv").read_text().splitlines()
    uxplay, version, commit = lines[0].split("\t")
    if uxplay != "UxPlay" or not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Missing the pinned UxPlay commit")
    components = [{
        "type": "application", "name": uxplay, "version": version,
        "bom-ref": "UxPlay",
        "purl": f"pkg:github/fdh2/uxplay@{commit}",
        "properties": [{"name": "dxmirror:git-commit", "value": commit}],
    }]
    metadata = {}
    for repo in ("MINGW-packages", "MSYS2-packages"):
        url = f"{MSYS2_GITHUB}/{repo}/releases/download/srcinfo-cache/sbom.cdx.json"
        catalog, _ = read_url(url)
        if not catalog["components"]:
            raise ValueError(f"Empty MSYS2 catalog: {repo}")
        for component in catalog["components"]:
            for prop in component.get("properties", []):
                if prop["name"] == "msys2:pkgbase":
                    metadata.setdefault(prop["value"], []).append(component)

    if len(lines) < 2:
        raise ValueError("No MSYS2 runtime files were recorded")
    for line in lines[1:]:
        package, base, installed_version = line.split("\t")
        upstream = installed_version.split(":", 1)[-1].rsplit("-", 1)[0]
        upstream = upstream.split("+", 1)[0].split("~", 1)[-1]
        identifiers = set()
        for entry in metadata.get(base, []):
            if cpe := entry.get("cpe"):
                fields = cpe.split(":")
                if not cpe.startswith("cpe:/") or len(fields) < 5:
                    raise ValueError(f"Unexpected MSYS2 CPE for {base}: {cpe}")
                # The catalog describes the current recipe. Use the version actually shipped.
                if fields[4] in (entry["version"], "", "*"):
                    fields[4] = upstream
                    identifiers.add((entry["name"], "cpe", ":".join(fields)))
                else:
                    print(f"::warning::Cannot map version-specific CPE {cpe} to {package}")
            if purl := entry.get("purl"):
                prefix, _, rest = purl.partition("@")
                entry_version, marker, suffix = rest.partition("?")
                if entry_version == entry["version"]:
                    identifiers.add((entry["name"], "purl", f"{prefix}@{upstream}{marker}{suffix}"))
        if base == "mingw-w64-gstreamer" or base.startswith("mingw-w64-gst-"):
            identifiers.add(("gstreamer", "cpe", GSTREAMER_CPE + upstream))
        if not identifiers:
            print(f"::warning::No advisory identifier for {package} {installed_version}")
            identifiers.add((package, None, None))
        for index, (name, kind, identifier) in enumerate(sorted(identifiers)):
            component = {
                "type": "library", "name": name, "version": upstream,
                "bom-ref": f"{package}:{index}",
                "properties": [
                    {"name": "msys2:pkgbase", "value": base},
                    {"name": "dxmirror:msys2-package", "value": package},
                    {"name": "dxmirror:msys2-version", "value": installed_version},
                ],
            }
            if kind:
                component[kind] = identifier
            components.append(component)

    document = {"bomFormat": "CycloneDX", "specVersion": "1.5",
                "version": 1, "components": components}
    (receiver / "licenses/DXMirror-native.cdx.json").write_text(
        json.dumps(document, indent=2) + "\n", encoding="utf-8"
    )


def version_tuple(version):
    if not re.fullmatch(r"\d+(?:\.\d+){1,3}", version):
        raise ValueError(f"Unsupported UxPlay version: {version}")
    fields = tuple(map(int, version.split(".")))
    return fields + (0,) * (4 - len(fields))


def affects(version, version_range):
    if version_range in (None, "", "*"):
        return True
    for expression in version_range.split(","):
        match = re.fullmatch(r"\s*(<=|>=|<|>|=)?\s*v?(\d+(?:\.\d+){1,3})\s*", expression)
        if not match:
            raise ValueError(f"Unsupported UxPlay advisory range: {version_range}")
        comparator, limit = match.groups()
        limit = version_tuple(limit)
        comparisons = {"<": version < limit, "<=": version <= limit,
                       ">": version > limit, ">=": version >= limit,
                       "=": version == limit}
        if not comparisons[comparator or "="]:
            return False
    return True


def scan_uxplay(sbom, report):
    components = json.loads(sbom.read_text(encoding="utf-8"))["components"]
    version = version_tuple(next(item["version"] for item in components if item["bom-ref"] == "UxPlay"))
    headers = {"Accept": "application/vnd.github+json"}
    if token := os.getenv("GH_TOKEN"):
        headers["Authorization"] = f"Bearer {token}"
    findings = []
    for page in range(1, 100):
        advisories, link = read_url(f"{UXPLAY_API}?state=published&per_page=100&page={page}", headers)
        for advisory in advisories:
            if advisory.get("withdrawn_at"):
                continue
            if any(affects(version, entry.get("vulnerable_version_range"))
                   for entry in advisory.get("vulnerabilities") or [{}]):
                findings.append({key: advisory[key] for key in
                                 ("ghsa_id", "cve_id", "severity", "html_url")})
        if 'rel="next"' not in link:
            break
    else:
        raise ValueError("UxPlay advisory pagination limit reached")
    report.write_text(json.dumps(findings, indent=2) + "\n", encoding="utf-8")
    for finding in findings:
        print(f"UxPlay {finding['severity']}: {finding['html_url']}")
    return int(any(item["severity"] != "low" for item in findings))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)
    inventory = subcommands.add_parser("sbom")
    inventory.add_argument("receiver", type=Path)
    scan = subcommands.add_parser("scan-uxplay")
    scan.add_argument("sbom", type=Path)
    scan.add_argument("report", type=Path)
    args = parser.parse_args()
    if args.command == "sbom":
        native_sbom(args.receiver)
    else:
        raise SystemExit(scan_uxplay(args.sbom, args.report))
