#!/usr/bin/env bash
# Dev server for Repertoire Table.
#   ./serve.sh          -> http://localhost:8000
#
# Uses the real backend (FastAPI + SQLite persistence) when its deps are
# available -- in ./.venv, or in whatever python3 is on PATH. Otherwise falls
# back to a static-only server (app works, saving to the server does not).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
PORT="${PORT:-8000}"
export PORT

if command -v python3 >/dev/null 2>&1; then PY=python3
elif command -v python >/dev/null 2>&1; then PY=python
else
  echo "serve.sh: no python3 on PATH -- cannot start a server." >&2
  exit 1
fi

# Prefer the project venv if it exists.
if [ -x "$ROOT/.venv/bin/python" ]; then
  PY="$ROOT/.venv/bin/python"
fi

open_browser() {
  local url="$1"
  ( sleep 1
    if command -v xdg-open >/dev/null 2>&1; then xdg-open "$url" >/dev/null 2>&1
    elif command -v open    >/dev/null 2>&1; then open "$url" >/dev/null 2>&1
    else echo "serve.sh: open $url in your browser."
    fi ) &
}

if "$PY" -c "import fastapi, uvicorn" >/dev/null 2>&1; then
  echo "serve.sh: backend mode (FastAPI + SQLite at ${DB_PATH:-data/repertoire.sqlite3})"
  echo "serve.sh: app http://localhost:$PORT/   api http://localhost:$PORT/api/health"
  open_browser "http://localhost:$PORT"
  exec "$PY" -m uvicorn server.app:app --host 0.0.0.0 --port "$PORT"
else
  echo "serve.sh: WARNING -- fastapi/uvicorn not installed for $PY."
  echo "serve.sh: falling back to a STATIC-ONLY server."
  echo "serve.sh: >>> server-side persistence (the /api endpoints) is UNAVAILABLE <<<"
  echo "serve.sh: the app still works and still saves to browser localStorage."
  echo "serve.sh: to get the backend:  python3 -m venv .venv && .venv/bin/pip install -r server/requirements.txt"
  open_browser "http://localhost:$PORT"
  exec "$PY" server/static_fallback.py
fi
