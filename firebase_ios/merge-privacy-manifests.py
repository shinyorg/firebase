#!/usr/bin/env python3
"""
Merges the privacy manifests of every SPM dependency statically linked into a framework and writes the
result into the framework as its own PrivacyInfo.xcprivacy.

Why: the Firebase SPM products are static, so their code is merged into our framework binary, but SPM
builds each dependency's PrivacyInfo.xcprivacy into a sibling resource bundle that never makes it into
the archived framework. App Store Connect then sees e.g. GoogleDataTransport inside our binary with no
manifest and rejects the upload (ITMS-91061). See shinyorg/firebase#1.

Usage: merge-privacy-manifests.py <search-dir> <framework> [<framework> ...]
"""
import os
import plistlib
import subprocess
import sys


def find_manifests(search_dir):
    for root, dirs, files in os.walk(search_dir):
        # never read back a manifest we generated into one of our own frameworks
        dirs[:] = [d for d in dirs if not d.startswith("Shiny")]
        if "PrivacyInfo.xcprivacy" in files:
            yield os.path.join(root, "PrivacyInfo.xcprivacy")


def union(target, items):
    for item in items or []:
        if item not in target:
            target.append(item)


def merge(paths):
    tracking = False
    domains = []
    collected = {}  # data type -> merged entry
    accessed = {}   # api category -> merged entry

    for path in paths:
        with open(path, "rb") as f:
            m = plistlib.load(f)

        tracking = tracking or bool(m.get("NSPrivacyTracking", False))
        union(domains, m.get("NSPrivacyTrackingDomains"))

        for entry in m.get("NSPrivacyCollectedDataTypes") or []:
            key = entry["NSPrivacyCollectedDataType"]
            merged = collected.setdefault(key, {
                "NSPrivacyCollectedDataType": key,
                "NSPrivacyCollectedDataTypeLinked": False,
                "NSPrivacyCollectedDataTypeTracking": False,
                "NSPrivacyCollectedDataTypePurposes": [],
            })
            merged["NSPrivacyCollectedDataTypeLinked"] |= bool(entry.get("NSPrivacyCollectedDataTypeLinked", False))
            merged["NSPrivacyCollectedDataTypeTracking"] |= bool(entry.get("NSPrivacyCollectedDataTypeTracking", False))
            union(merged["NSPrivacyCollectedDataTypePurposes"], entry.get("NSPrivacyCollectedDataTypePurposes"))

        for entry in m.get("NSPrivacyAccessedAPITypes") or []:
            key = entry["NSPrivacyAccessedAPIType"]
            merged = accessed.setdefault(key, {
                "NSPrivacyAccessedAPIType": key,
                "NSPrivacyAccessedAPITypeReasons": [],
            })
            union(merged["NSPrivacyAccessedAPITypeReasons"], entry.get("NSPrivacyAccessedAPITypeReasons"))

    return {
        "NSPrivacyTracking": tracking,
        "NSPrivacyTrackingDomains": domains,
        "NSPrivacyCollectedDataTypes": [collected[k] for k in sorted(collected)],
        "NSPrivacyAccessedAPITypes": [accessed[k] for k in sorted(accessed)],
    }


def write_into(framework, manifest):
    # macOS-style (Mac Catalyst) frameworks are versioned; resources live under Versions/A/Resources
    resources = os.path.join(framework, "Versions", "A", "Resources")
    target_dir = resources if os.path.isdir(resources) else framework
    target = os.path.join(target_dir, "PrivacyInfo.xcprivacy")
    with open(target, "wb") as f:
        plistlib.dump(manifest, f, fmt=plistlib.FMT_XML)

    # adding a resource breaks an existing (ad-hoc) seal - reapply it; unsigned slices stay unsigned
    signed = subprocess.run(["codesign", "-d", framework], capture_output=True).returncode == 0
    if signed:
        subprocess.run(["codesign", "--force", "--sign", "-", "--preserve-metadata=identifier,entitlements", framework], check=True)
    print(f"merge-privacy-manifests: wrote {target}")


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)

    paths = sorted(set(find_manifests(sys.argv[1])))
    if not paths:
        sys.exit(f"merge-privacy-manifests: no PrivacyInfo.xcprivacy found under {sys.argv[1]}")

    bundles = sorted({os.path.basename(p.split(".bundle")[0]) for p in paths})
    print(f"merge-privacy-manifests: merging {len(paths)} manifests from: {', '.join(bundles)}")

    manifest = merge(paths)
    for framework in sys.argv[2:]:
        write_into(framework, manifest)


if __name__ == "__main__":
    main()
