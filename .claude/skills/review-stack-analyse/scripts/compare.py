#!/usr/bin/env python
"""Normalize the outputs of Ivy.StackAnalyzer + reference tools into one compact,
token-cheap comparison. The agent reads THIS instead of the raw JSON/YAML blobs.

Usage:
  python compare.py --ivy ivy.json [--specfy specfy.json] [--cd cd-dir] [--linguist linguist.txt]

Any artifact may be missing/unreadable; it is reported as "unavailable" and skipped.
Technology names are normalized (lowercased, alphanumerics only) so cosmetic
naming differences ("TanStack Router" vs "tanstackrouter") don't read as gaps.
"""
import argparse, glob, json, os, re
from collections import Counter


def norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s.lower())


def load_json(path):
    if not path or not os.path.exists(path):
        return None
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f)
    except Exception:
        return None


def ivy_summary(path):
    d = load_json(path)
    if not d:
        return None
    return {
        "langs": [(l["name"], l.get("percent", 0)) for l in d.get("languages", [])],
        "comps": [(c["relativePath"], c.get("isWorkspaceRoot", False), c.get("isAuxiliary", False))
                  for c in d.get("components", [])],
        "techs": [(t["name"], t.get("category", "")) for t in d.get("technologies", [])],
        "infra": [i.get("kind", "?") for i in d.get("infrastructure", [])],
    }


def specfy_summary(path):
    d = load_json(path)
    if d is None:
        return None
    techs = set()

    def walk(o):
        if isinstance(o, dict):
            for t in (o.get("techs") or []):
                if isinstance(t, str):
                    techs.add(t)
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    walk(d)
    return techs


def cd_summary(folder):
    if not folder or not os.path.isdir(folder):
        return None
    files = sorted(glob.glob(os.path.join(folder, "ScanManifest_*.json")))
    if not files:
        return None
    data = load_json(files[-1])
    if not data:
        return None
    eco, names = Counter(), set()
    for c in data.get("componentsFound", []):
        comp = c.get("component", {}) or {}
        eco[comp.get("type", "?")] += 1
        n = comp.get("name") or comp.get("packageName")
        if n:
            names.add(n)
    return {"eco": dict(eco), "count": len(data.get("componentsFound", [])), "names": names}


def linguist_summary(path):
    if not path or not os.path.exists(path):
        return None
    try:
        txt = open(path, encoding="utf-8").read()
    except Exception:
        return None
    out = []
    for line in txt.splitlines():
        m = re.match(r"\s*([0-9.]+)%\s+\d+\s+(.+)", line)
        if m:
            out.append((m.group(2).strip(), float(m.group(1))))
    return out or None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ivy")
    ap.add_argument("--specfy")
    ap.add_argument("--cd")
    ap.add_argument("--linguist")
    a = ap.parse_args()

    ivy = ivy_summary(a.ivy)
    specfy = specfy_summary(a.specfy)
    cd = cd_summary(a.cd)
    ling = linguist_summary(a.linguist)

    print("# COMPARISON (normalized)\n")
    print("Sources: "
          f"ivy={'ok' if ivy else 'UNAVAILABLE'} | "
          f"specfy={'ok' if specfy is not None else 'UNAVAILABLE'} | "
          f"component-detection={'ok' if cd else 'UNAVAILABLE'} | "
          f"linguist={'ok' if ling else 'UNAVAILABLE'}\n")

    # ---- Languages: ivy vs linguist ----
    print("## LANGUAGES (top 8 by share)")
    if ivy:
        print("ivy:      " + ", ".join(f"{n} {p}%" for n, p in ivy["langs"][:8]))
    if ling:
        print("linguist: " + ", ".join(f"{n} {p}%" for n, p in ling[:8]))
    else:
        print("linguist: unavailable")
    print()

    # ---- Technologies union (ivy vs specfy) ----
    print("## TECHNOLOGIES (I=ivy, S=specfy)")
    union = {}
    if ivy:
        for name, cat in ivy["techs"]:
            union.setdefault(norm(name), {"name": name, "cat": cat, "i": False, "s": False})["i"] = True
    if specfy:
        for t in specfy:
            e = union.setdefault(norm(t), {"name": t, "cat": "", "i": False, "s": False})
            e["s"] = True
    def sortkey(item):
        k, v = item
        # specfy-only first (likely gaps), then ivy-only, then both; alpha within
        rank = 0 if (v["s"] and not v["i"]) else (1 if (v["i"] and not v["s"]) else 2)
        return (rank, k)
    for _, v in sorted(union.items(), key=sortkey):
        flag = ("I" if v["i"] else "-") + ("S" if v["s"] else "-")
        cat = f" [{v['cat']}]" if v["cat"] else ""
        print(f"  {flag}  {v['name']}{cat}")
    print()

    # ---- Highlighted gaps ----
    gaps = [v["name"] for _, v in sorted(union.items()) if v["s"] and not v["i"]]
    print("## POTENTIAL GAPS (in specfy, not ivy): " + (", ".join(gaps) if gaps else "none"))
    print()

    # ---- Components (ivy) ----
    print("## COMPONENTS (ivy)")
    if ivy:
        for path, ws, aux in ivy["comps"]:
            tags = " ".join(t for t, on in (("workspace-root", ws), ("auxiliary", aux)) if on)
            print(f"  {path}{('  ['+tags+']') if tags else ''}")
        print("  infra: " + (", ".join(ivy["infra"]) or "none"))
    print()

    # ---- component-detection ----
    print("## COMPONENT-DETECTION (dependency cross-check)")
    if cd:
        print(f"  components: {cd['count']}  ecosystems: {cd['eco']}")
        if cd["count"] <= 3 and cd["names"]:
            print("  names: " + ", ".join(sorted(cd["names"])))
    else:
        print("  unavailable")


if __name__ == "__main__":
    main()
