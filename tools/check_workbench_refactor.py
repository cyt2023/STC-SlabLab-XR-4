#!/usr/bin/env python3
"""Compare a mechanical workbench refactor with an explicit source baseline.

Checks complete member bodies (including comments), not just line counts, and
keeps each file's field/type/property declarations in their original order.
This is an audit for source relocation; it does not replace Unity runtime tests.

Usage:
  python tools/check_workbench_refactor.py --baseline PATH_TO_SNAPSHOT_API_FOLDER
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import re
from pathlib import Path

CLASS = "VolumeSTCubeQuestSpatialWorkbench"
ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "RenderingModule/Assets/VolumeSTCubeAPI"


def balanced(chunk):
    return len(re.findall(r"#if\b", chunk)) == len(re.findall(r"#endif\b", chunk))


def strip_code(src):
    out = list(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            j = src.find('\n', i); j = n if j < 0 else j
            for k in range(i, j): out[k] = ' '
            i = j
        elif c == '/' and i + 1 < n and src[i + 1] == '*':
            j = src.find('*/', i + 2); j = n if j < 0 else j + 2
            for k in range(i, j):
                if out[k] != '\n': out[k] = ' '
            i = j
        elif c == '@' and i + 1 < n and src[i + 1] == '"':
            j = i + 2
            while j < n:
                if src[j] == '"':
                    if j + 1 < n and src[j + 1] == '"': j += 2; continue
                    j += 1; break
                j += 1
            for k in range(i, j):
                if out[k] != '\n': out[k] = ' '
            i = j
        elif c == '"':
            j = i + 1
            while j < n:
                if src[j] == '\\': j += 2; continue
                if src[j] == '"': j += 1; break
                j += 1
            for k in range(i, j):
                if out[k] != '\n': out[k] = ' '
            i = j
        elif c == "'":
            j = i + 1
            while j < n:
                if src[j] == '\\': j += 2; continue
                if src[j] == "'": j += 1; break
                j += 1
            for k in range(i, j):
                if out[k] != '\n': out[k] = ' '
            i = j
        else:
            i += 1
    return ''.join(out)


def parse_members(text):
    """(start, end) spans of the class-body members, verbatim."""
    code = strip_code(text)
    idx = code.index('class ' + CLASS)
    i = code.index('{', idx)
    n = len(code)
    depth = 0
    members, start = [], None
    body_start = i + 1
    while i < n:
        ch = code[i]
        closed = False
        if ch == '{':
            depth += 1
        elif ch == '}':
            depth -= 1
            if depth == 0:
                return members, body_start, i
            if depth == 1:
                j = i + 1
                while j < n and code[j] in ' \t\r\n':
                    j += 1
                # a '}' that closes an initializer continues with ';' (or ',')
                if not (j < n and code[j] in ';,'):
                    members.append((start, i + 1))
                    start = None
                    closed = True
        elif ch == ';' and depth == 1:
            members.append((start, i + 1))
            start = None
            closed = True
        if not closed and depth == 1 and start is None and ch not in ' \t\r\n':
            start = i
        i += 1
    return members, body_start, n


def member_texts(text):
    """Whole class body carved into members; text between members (comments,
    attributes, blank lines) travels with the member that follows it."""
    spans, body_start, class_close = parse_members(text)
    if not spans:
        return []
    out = []
    prev_end = body_start
    for a, b in spans:
        chunk = text[prev_end:b]
        if chunk.strip():
            out.append(chunk.strip('\n'))
        prev_end = b
    tail = text[prev_end:class_close]
    if tail.strip() and out:
        out[-1] = out[-1] + tail.rstrip('\n')
    # Closing member-level directives belong to the preceding declaration.
    # Otherwise #endif is attached to the next method and merging a conditional
    # block accidentally swallows that unrelated method as well.
    separated = []
    for chunk in out:
        closing = re.match(r"^(\s*#endif[^\n]*\n)", chunk)
        while closing and separated:
            separated[-1] += "\n" + closing.group(1).rstrip("\n")
            chunk = chunk[closing.end():]
            closing = re.match(r"^(\s*#endif[^\n]*\n)", chunk)
        if chunk.strip():
            separated.append(chunk)
    out = separated
    # A member-level #if (written flush left, e.g. the Quest speech fields)
    # wraps several declarations: keep merging until the unit is balanced so the
    # conditional never straddles two output files.
    units, pending = [], ""
    for chunk in out:
        pending = chunk if not pending else pending + "\n" + chunk
        if balanced(pending):
            units.append(pending)
            pending = ""
    if pending:
        units.append(pending)
    return units



METHOD = re.compile(
    r"(?:#.*\n\s*)*(?:\[[^\]]*\]\s*)*"
    r"(?:public|private|protected|internal)\s+"
    r"(?:(?:static|override|virtual|async)\s+)*"
    r"[\w.<>\[\]]+\s+(\w+)\s*\("
)


def inventory(folder):
    files = sorted(folder.glob(CLASS + "*.cs"))
    if not files:
        raise ValueError("No workbench source files in " + str(folder))
    return {p.name: member_texts(p.read_text(encoding="utf-8")) for p in files}


def compare(baseline, current):
    before = inventory(baseline)
    after = inventory(current)
    old = collections.Counter(m.strip() for ms in before.values() for m in ms)
    new = collections.Counter(m.strip() for ms in after.values() for m in ms)
    problems = []
    if old != new:
        problems.append("Complete member bodies differ: %d removed/changed, %d added/changed"
                        % (sum((old - new).values()), sum((new - old).values())))
    for filename, members in before.items():
        declarations = lambda ms: [m.strip() for m in ms
                                  if not METHOD.match(strip_code(m).strip())
                                  and not re.match(r"(?:#.*\n\s*)*(?:private|public)\s+"
                                                   r"(?:(?:sealed|static)\s+)?(?:class|enum|struct)\b",
                                                   strip_code(m).strip())]
        if declarations(members) != declarations(after.get(filename, [])):
            problems.append("Field/type/property order or initialization changed: " + filename)
    return {
        "result": "fail" if problems else "pass",
        "baselineParts": len(before),
        "currentParts": len(after),
        "membersChecked": sum(old.values()),
        "memberDigest": hashlib.sha256("\n".join(sorted(old.elements())).encode()).hexdigest(),
        "failures": problems,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--current", type=Path, default=API)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    report = compare(args.baseline, args.current)
    encoded = json.dumps(report, indent=2) + "\n"
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(encoded, encoding="utf-8")
    print(encoded, end="")
    return 0 if report["result"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
