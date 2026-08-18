#!/usr/bin/env python3
"""Re-add the graph edges graphify's extraction does not produce on its own.

Run this after every `graphify update .` (and after a full `/graphify` rebuild).
Both passes are deterministic — they read the repository and the existing graph,
never an LLM — so this costs nothing and always produces the same result.

Two gaps are repaired:

1. Documentation is disconnected from code. The semantic (doc) extraction and the
   AST (code) extraction produce node sets that never reference each other, so the
   graph comes out as two disjoint halves: a query about a code symbol can never
   reach the Markdown that explains it. This pass adds a `documents` edge from each
   doc-extracted node to the code node carrying the same name.

2. Extension-method calls are missing. graphify types a call receiver and looks the
   method up on that type or its base classes. A C# extension method is declared on
   a static helper class and never on the receiver's type, so `res.ToApiResult()`
   and `builder.AddSerginCore(modules)` resolve to nothing and no `calls` edge is
   emitted. That erases the whole host-composition spine
   (Program.cs -> AddSerginWebUi -> AddSerginCore -> module.AddServices). This pass
   scans for `static ... Name(this T ...)` declarations, finds the call sites, and
   attributes each one to the enclosing method.

Both passes refuse to guess: a name that does not resolve to exactly one target is
skipped and reported rather than bound to a maybe-match.

Community assignments are deliberately left untouched. Re-clustering would shift
every community id and silently invalidate the curated community names in
graph.json, which is a worse outcome than communities that predate these edges.
Run `graphify cluster-only .` yourself if you want them recomputed — and expect to
relabel afterwards.

Run from the repository root (paths are resolved against the current directory):
    python .claude/skills/graphify/scripts/graphify_repair.py [--graph graphify-out/graph.json] [--dry-run]
"""
from __future__ import annotations

import argparse
import collections
import json
import re
import sys
from pathlib import Path

# `[^()<>]*` inside the generic group, not `[^(]*`: the looser class lets
# `<WebApplication> UseSerginWebUiAsync<TRootComponent>` match as a single generic
# argument list, which captures "Task" as the method name instead of the real one.
EXTENSION_DECL = re.compile(
    r"\bstatic\s+[\w<>,\[\]\?\. ]+?\s+(\w+)(?:<[^()<>]*>)?\s*\(\s*this\s+", re.M
)
SOURCE_DIRS = ("src", "tests")


def normalize(text: object) -> str:
    return re.sub(r"[^a-z0-9]+", "", str(text).lower())


def posix(path: object) -> str:
    return str(path or "").replace("\\", "/")


def is_ast(node: dict) -> bool:
    """AST-extracted nodes carry `_origin: "ast"`; semantic ones carry no origin."""
    return node.get("_origin") == "ast"


def csharp_files(root: Path) -> list[Path]:
    files: list[Path] = []
    for directory in SOURCE_DIRS:
        base = root / directory
        if base.is_dir():
            files.extend(base.rglob("*.cs"))
    return files


def bridge_docs_to_code(nodes: list[dict]) -> tuple[list[dict], list[str]]:
    """One `documents` edge per doc node naming exactly one code node."""
    by_id = {n["id"]: n for n in nodes}
    code_by_name: dict[str, list[str]] = collections.defaultdict(list)
    for node in nodes:
        if is_ast(node):
            code_by_name[normalize(node.get("label"))].append(node["id"])

    edges: list[dict] = []
    skipped: list[str] = []
    for node in nodes:
        if is_ast(node):
            continue
        candidates = code_by_name.get(normalize(node.get("label")), [])
        if not candidates:
            continue
        target = candidates[0] if len(candidates) == 1 else None
        if target is None:
            # Several code nodes share the name — usually one definition plus some
            # usages in .razor markup. Prefer the file named after the symbol.
            wanted = normalize(node.get("label"))
            defined_in = [
                c for c in candidates
                if normalize(Path(posix(by_id[c].get("source_file"))).name.split(".")[0]) == wanted
            ]
            target = defined_in[0] if len(defined_in) == 1 else None
        if target is None:
            skipped.append(f"{node.get('label')} ({len(candidates)} code nodes, no clear definition)")
            continue
        edges.append({
            "source": node["id"],
            "target": target,
            "relation": "documents",
            "confidence": "INFERRED",
            "confidence_score": 0.95,
            "source_file": node.get("source_file"),
            "source_location": node.get("source_location"),
            "weight": 1.0,
        })
    return edges, skipped


def extension_method_calls(root: Path, nodes: list[dict]) -> tuple[list[dict], list[str]]:
    """`calls` edges from each call site's enclosing method to the extension method."""
    by_id = {n["id"]: n for n in nodes}
    skipped: list[str] = []

    declarations: dict[str, set[str]] = collections.defaultdict(set)
    for path in csharp_files(root):
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        for match in EXTENSION_DECL.finditer(text):
            declarations[match.group(1)].add(posix(path.relative_to(root)))

    methods_in_file: dict[str, list[dict]] = collections.defaultdict(list)
    for node in nodes:
        if is_ast(node) and str(node.get("label", "")).startswith("."):
            methods_in_file[posix(node.get("source_file"))].append(node)

    target_of: dict[str, str] = {}
    for name, files in declarations.items():
        if len(files) != 1:
            skipped.append(f"{name} (declared in {len(files)} files)")
            continue
        declaring_file = next(iter(files))
        # Several declarations in one file are overloads, not ambiguity.
        hits = [
            n for n in methods_in_file.get(declaring_file, [])
            if normalize(str(n.get("label", "")).strip(".()")) == normalize(name)
        ]
        if len(hits) == 1:
            target_of[name] = hits[0]["id"]
        else:
            skipped.append(f"{name} ({len(hits)} node matches in {declaring_file})")
    if not target_of:
        return [], skipped

    # Method start lines per file, so a call site can be attributed to its caller.
    starts: dict[str, list[tuple[int, str]]] = collections.defaultdict(list)
    for path, method_nodes in methods_in_file.items():
        for node in method_nodes:
            line = re.match(r"L(\d+)", str(node.get("source_location") or ""))
            if line:
                starts[path].append((int(line.group(1)), node["id"]))
    for entries in starts.values():
        entries.sort()

    file_nodes = {
        posix(n.get("source_file")): n["id"]
        for n in nodes
        if is_ast(n) and n.get("label") == Path(posix(n.get("source_file"))).name
    }

    def caller_at(path: str, line: int) -> str | None:
        enclosing = [nid for start, nid in starts.get(path, []) if start <= line]
        return enclosing[-1] if enclosing else file_nodes.get(path)

    patterns = {name: re.compile(r"\." + re.escape(name) + r"\s*[(<]") for name in target_of}
    edges: list[dict] = []
    seen: set[tuple[str, str]] = set()
    for path in csharp_files(root):
        relative = posix(path.relative_to(root))
        try:
            lines = path.read_text(encoding="utf-8", errors="ignore").splitlines()
        except OSError:
            continue
        for number, text in enumerate(lines, 1):
            if text.lstrip().startswith("//"):
                continue
            for name, target in target_of.items():
                if not patterns[name].search(text):
                    continue
                if posix(by_id[target].get("source_file")) == relative:
                    continue  # the declaration itself
                source = caller_at(relative, number)
                if not source or source == target or (source, target) in seen:
                    continue
                seen.add((source, target))
                edges.append({
                    "source": source,
                    "target": target,
                    "relation": "calls",
                    "confidence": "INFERRED",
                    "confidence_score": 0.85,
                    "source_file": relative,
                    "source_location": f"L{number}",
                    "weight": 1.0,
                })
    return edges, skipped


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--graph", default="graphify-out/graph.json",
                        help="path to graph.json (default: graphify-out/graph.json)")
    parser.add_argument("--dry-run", action="store_true",
                        help="report what would be added without writing")
    args = parser.parse_args()

    graph_path = Path(args.graph)
    if not graph_path.is_file():
        print(f"error: {graph_path} not found — run /graphify first.", file=sys.stderr)
        return 1
    root = Path.cwd()

    graph = json.loads(graph_path.read_text(encoding="utf-8"))
    nodes = graph.get("nodes", [])
    links = graph.get("links", [])
    if not nodes:
        print(f"error: {graph_path} has no nodes.", file=sys.stderr)
        return 1

    doc_edges, doc_skipped = bridge_docs_to_code(nodes)
    call_edges, call_skipped = extension_method_calls(root, nodes)

    existing = {(e.get("source"), e.get("target"), e.get("relation")) for e in links}
    fresh = [e for e in doc_edges + call_edges
             if (e["source"], e["target"], e["relation"]) not in existing]

    doc_new = sum(1 for e in fresh if e["relation"] == "documents")
    call_new = len(fresh) - doc_new
    print(f"documentation bridges: {len(doc_edges)} found, {doc_new} missing from the graph")
    print(f"extension-method calls: {len(call_edges)} found, {call_new} missing from the graph")
    for note in doc_skipped + call_skipped:
        print(f"  skipped (ambiguous): {note}")

    if not fresh:
        print("graph already complete — nothing to add.")
        return 0
    if args.dry_run:
        print(f"dry run — would add {len(fresh)} edges.")
        return 0

    graph["links"] = links + fresh
    graph_path.write_text(json.dumps(graph, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"added {len(fresh)} edges to {graph_path} ({len(links)} -> {len(graph['links'])}).")
    print("note: community assignments were not recomputed; see this file's docstring.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
