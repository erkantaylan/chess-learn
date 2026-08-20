"""SQLite persistence for chess study repertoires (stdlib sqlite3, no ORM)."""
import json
import os
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DB_PATH = REPO_ROOT / "data" / "repertoire.sqlite3"

SCHEMA = """
CREATE TABLE IF NOT EXISTS studies (
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    start_fen  TEXT NOT NULL DEFAULT '',
    tree       TEXT NOT NULL DEFAULT 'null',   -- arbitrary JSON, stored as text
    pgn        TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
"""


def db_path() -> Path:
    return Path(os.environ.get("DB_PATH") or DEFAULT_DB_PATH)


def now_iso() -> str:
    """ISO-8601 UTC, second precision, explicit Z."""
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def connect() -> sqlite3.Connection:
    p = db_path()
    p.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(p)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


def init_db() -> None:
    with connect() as conn:
        conn.executescript(SCHEMA)


def count_moves(tree: Any) -> int:
    """Number of move nodes in the tree, not counting the root position node."""
    if not isinstance(tree, dict):
        return 0
    n = -1  # root does not count as a move

    stack = [tree]
    while stack:
        node = stack.pop()
        if not isinstance(node, dict):
            continue
        n += 1
        kids = node.get("children")
        if isinstance(kids, list):
            stack.extend(kids)
    return max(n, 0)


def _row_to_record(row: sqlite3.Row) -> dict:
    try:
        tree = json.loads(row["tree"])
    except (TypeError, ValueError):
        tree = None
    return {
        "id": row["id"],
        "name": row["name"],
        "start_fen": row["start_fen"],
        "tree": tree,
        "pgn": row["pgn"],
        "created_at": row["created_at"],
        "updated_at": row["updated_at"],
    }


def list_studies() -> list[dict]:
    with connect() as conn:
        rows = conn.execute(
            "SELECT id, name, tree, created_at, updated_at FROM studies ORDER BY updated_at DESC, id DESC"
        ).fetchall()
    out = []
    for r in rows:
        try:
            tree = json.loads(r["tree"])
        except (TypeError, ValueError):
            tree = None
        out.append({
            "id": r["id"],
            "name": r["name"],
            "created_at": r["created_at"],
            "updated_at": r["updated_at"],
            "move_count": count_moves(tree),
        })
    return out


def create_study(name: str, start_fen: str, tree: Any, pgn: str) -> dict:
    ts = now_iso()
    with connect() as conn:
        cur = conn.execute(
            "INSERT INTO studies (name, start_fen, tree, pgn, created_at, updated_at)"
            " VALUES (?,?,?,?,?,?)",
            (name, start_fen, json.dumps(tree), pgn, ts, ts),
        )
        new_id = cur.lastrowid
    return get_study(new_id)


def get_study(study_id: int) -> Optional[dict]:
    with connect() as conn:
        row = conn.execute("SELECT * FROM studies WHERE id = ?", (study_id,)).fetchone()
    return _row_to_record(row) if row else None


def update_study(study_id: int, fields: dict) -> Optional[dict]:
    """Partial update. `fields` may contain name, start_fen, tree, pgn."""
    sets, vals = [], []
    for col in ("name", "start_fen", "pgn"):
        if col in fields:
            sets.append(f"{col} = ?")
            vals.append(fields[col])
    if "tree" in fields:
        sets.append("tree = ?")
        vals.append(json.dumps(fields["tree"]))
    sets.append("updated_at = ?")
    vals.append(now_iso())
    vals.append(study_id)
    with connect() as conn:
        cur = conn.execute(f"UPDATE studies SET {', '.join(sets)} WHERE id = ?", vals)
        if cur.rowcount == 0:
            return None
    return get_study(study_id)


def delete_study(study_id: int) -> bool:
    with connect() as conn:
        cur = conn.execute("DELETE FROM studies WHERE id = ?", (study_id,))
        return cur.rowcount > 0


def count_studies() -> int:
    with connect() as conn:
        return conn.execute("SELECT COUNT(*) AS c FROM studies").fetchone()["c"]
