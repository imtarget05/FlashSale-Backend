"""Liệt kê external deps cần để import được maia.api, theo transitive closure.

Bài học từ lần deploy MAIA thất bại: quét AST của đúng một file (api.py) là
KHÔNG ĐỦ. api.py import `from maia.auth import ...`, và passlib nằm bên trong
auth.py — nên container build thành công rồi mới crash lúc uvicorn import,
với lỗi ModuleNotFoundError chỉ chỉ ra package, không chỉ ra ai cần nó.

Script này duyệt cả cây module maia.* để tìm đúng tập dependency thực sự cần.
"""
import ast
import sys
from pathlib import Path

SRC = Path(__file__).resolve().parent.parent / "src"


def module_path(mod: str) -> Path | None:
    rel = mod.replace(".", "/")
    for candidate in (SRC / f"{rel}.py", SRC / rel / "__init__.py"):
        if candidate.exists():
            return candidate
    return None


def top_level_imports(path: Path):
    try:
        tree = ast.parse(path.read_text(encoding="utf-8"))
    except (OSError, SyntaxError):
        return
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom) and node.module:
            yield node.module
        elif isinstance(node, ast.Import):
            for alias in node.names:
                yield alias.name


def main() -> int:
    stdlib = set(sys.stdlib_module_names)
    seen: set[str] = set()
    external: set[str] = set()
    unresolved: set[str] = set()
    queue = ["maia.api"]

    while queue:
        mod = queue.pop()
        if mod in seen:
            continue
        seen.add(mod)
        path = module_path(mod)
        if path is None:
            unresolved.add(mod)
            continue
        for imported in top_level_imports(path):
            head = imported.split(".")[0]
            if head == "maia":
                queue.append(imported)
            elif head not in stdlib and head != "__future__":
                external.add(head)

    print(f"maia modules walked : {len(seen)}")
    print(f"unresolved modules  : {sorted(unresolved)}")
    print()
    print("EXTERNAL DEPENDENCIES:")
    for name in sorted(external):
        print(f"  {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
