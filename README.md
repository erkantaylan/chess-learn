# Repertoire Table

A chess opening study board: branching move tree, your own names for every variation,
arrows/circles, FEN + PGN in and out, and Stockfish analysis.

- `index.html` — the whole app (vanilla JS, no build step). Own chess engine, perft-verified.
- `engine/` — Stockfish 18 lite single-thread (WASM), GPLv3, see `engine/LICENSE-stockfish.txt`.
- Piece art: cburnett (from lichess), inlined as an SVG sprite.

## Running

Stockfish needs a Web Worker, which browsers refuse to load from `file://`.
Serve the folder over HTTP:

    ./serve.sh          # http://localhost:8000

Opening `index.html` directly still works — everything except the engine.

## Backend / Docker

There is an optional backend (`server/`, FastAPI + SQLite) that serves the app
*and* a small JSON API, so studies can live on a server instead of only in
browser `localStorage`. See `docs/API.md` for the endpoint contract.

Either way, the server sets `Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp` (so `SharedArrayBuffer` is
available for a future multi-threaded Stockfish), serves `.wasm` as
`application/wasm`, and disables caching so an edited `index.html` is never
stale.

### Locally

    python3 -m venv .venv
    .venv/bin/pip install -r server/requirements.txt
    ./serve.sh                      # http://localhost:8000, opens a browser

`serve.sh` uses `.venv` if it exists, otherwise whatever `python3` is on PATH.
If `fastapi`/`uvicorn` are missing it falls back to a static-only server — the
app works, the engine works, but the `/api` endpoints do not — and says so
loudly. `PORT` and `DB_PATH` are honoured; the DB defaults to
`data/repertoire.sqlite3` (gitignored).

### Docker

    docker compose up --build      # http://localhost:8000

The SQLite file lives in the named volume `repertoire-data` mounted at
`/app/data`, so it survives `docker compose down` (use `down -v` to wipe it).
The compose service has a healthcheck on `/api/health`.

The image is ~200-250 MB: the vendored Stockfish `.wasm` alone is 7.3 MB and
the `python:3.12-slim` base plus FastAPI/uvicorn account for the rest. That's
expected, not a packaging bug.
