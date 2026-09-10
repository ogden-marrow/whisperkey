"""Parse every workflow file so a malformed one fails loudly.

A workflow GitHub cannot parse fails in 0 seconds with no job, no step and no
log, which is a miserable thing to debug. That happened once here, to
release.yml, and this exists so it cannot happen quietly again.
"""
import glob
import sys

import yaml

bad = False
for path in sorted(glob.glob(".github/workflows/*.y*ml")):
    try:
        doc = yaml.safe_load(open(path, encoding="utf-8"))
    except Exception as e:  # noqa: BLE001 - any parse failure is a failure
        print(f"FAIL  {path}\n      {e}")
        bad = True
        continue

    # yaml turns a bare `on:` key into True, which is its own small trap.
    triggers = doc.get("on", doc.get(True))
    if not triggers:
        print(f"FAIL  {path}\n      no triggers; this workflow would never run")
        bad = True
        continue

    if not doc.get("jobs"):
        print(f"FAIL  {path}\n      no jobs")
        bad = True
        continue

    print(f"ok    {path}  ({len(doc['jobs'])} job(s))")

sys.exit(1 if bad else 0)
