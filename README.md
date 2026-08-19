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
