# Repertoire Table API

Base URL: `http://localhost:8000`. Everything under `/api`. All requests and
responses are JSON (`Content-Type: application/json`). The same process serves
`index.html` and `engine/`, so the frontend can use **relative** URLs
(`fetch('/api/studies')`) — no CORS needed in normal use. (Permissive CORS is
enabled for `localhost` / `127.0.0.1` origins anyway, for a separate dev server.)

Timestamps are ISO-8601 UTC with a `Z` suffix, e.g. `2026-08-19T16:59:53Z`.

## The study record

```json
{
  "id": 1,
  "name": "Italian Game",
  "start_fen": "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
  "tree": { "san": "", "fen": "...", "children": [] },
  "pgn": "1. e4 e5 *",
  "created_at": "2026-08-19T16:59:53Z",
  "updated_at": "2026-08-19T16:59:59Z"
}
```

- `id` — integer, server-assigned, never sent by the client.
- `name` — non-empty string, max 200 chars.
- `start_fen` — string, may be `""`.
- `tree` — **arbitrary JSON**, stored verbatim. Send the same object the app
  keeps in `localStorage` under `repertoire-table`, or just its `tree` member —
  the server does not validate or rewrite it, it round-trips exactly.
  The only thing the server reads out of it is `children` (recursively) to
  compute `move_count` for the list endpoint.
- `pgn` — string, may be `""`.

---

## `GET /api/health`

```
curl -s http://localhost:8000/api/health
```
```json
{"ok": true, "studies": 1}
```

## `GET /api/studies`

Listing — **no `tree` and no `pgn`** in the payload, so it stays cheap. Sorted
by `updated_at` descending (most recently saved first).

```
curl -s http://localhost:8000/api/studies
```
```json
[
  {"id": 2, "name": "French Defence", "created_at": "2026-08-19T16:59:53Z",
   "updated_at": "2026-08-19T16:59:53Z", "move_count": 0},
  {"id": 1, "name": "Italian Game", "created_at": "2026-08-19T16:59:53Z",
   "updated_at": "2026-08-19T16:59:59Z", "move_count": 2}
]
```

`move_count` = number of nodes in `tree` excluding the root position node.

## `POST /api/studies`

Creates a study. Request body:

```json
{"name": "Italian Game", "start_fen": "<FEN>", "tree": { ... }, "pgn": "1. e4 e5 *"}
```

`name` is required. `start_fen` (default `""`), `tree` (default `null`) and
`pgn` (default `""`) are optional. Responds **201** with the full record
including the new `id`.

```
curl -s -X POST http://localhost:8000/api/studies \
  -H 'Content-Type: application/json' \
  -d '{"name":"Italian Game","start_fen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1","tree":{"san":"","children":[{"san":"e4","children":[]}]},"pgn":"1. e4 *"}'
```

Missing or empty `name` -> **422** with FastAPI's validation detail body.

## `GET /api/studies/{id}`

Full record. **404** `{"detail": "study 999 not found"}` if it does not exist.

```
curl -s http://localhost:8000/api/studies/1
```

## `PUT /api/studies/{id}`

**Partial** update — send only the fields you want changed; omitted fields are
left alone. `updated_at` is always bumped. Returns the full updated record.

```
curl -s -X PUT http://localhost:8000/api/studies/1 \
  -H 'Content-Type: application/json' \
  -d '{"tree":{"san":"","children":[{"san":"d4","children":[]}]},"pgn":"1. d4 *"}'
```

Rename only:

```
curl -s -X PUT http://localhost:8000/api/studies/1 \
  -H 'Content-Type: application/json' -d '{"name":"Giuoco Piano"}'
```

Note: because omission is what "leave alone" means, you cannot set `tree` to
JSON `null` by sending `{"tree": null}` — that is indistinguishable from
"unset" and is treated as no change. Send `{"tree": {}}` if you need to clear it.

**404** for an unknown id, **422** for e.g. `{"name": ""}`.

## `DELETE /api/studies/{id}`

**204** with an empty body on success, **404** if the id is unknown.

```
curl -s -o /dev/null -w '%{http_code}\n' -X DELETE http://localhost:8000/api/studies/2
```

---

## How the frontend should call this

The intended flow, replacing (or backing up) the single `localStorage`
`repertoire-table` blob:

1. **On load — populate a study picker.**
   `GET /api/studies` -> render `name` + `move_count` + `updated_at`.
   Keep the chosen `id` in memory (and in `localStorage`, so a refresh reopens
   the same study).

2. **Open a study.**
   `GET /api/studies/{id}` -> feed `start_fen`, `tree` (and `path`, if you keep
   it inside `tree`) straight into the existing loader. The `tree` you get back
   is byte-for-byte the JSON you sent.

3. **First save of a new study.**
   `POST /api/studies` with `{name, start_fen, tree, pgn}`. Store the returned
   `id`; every later save is a `PUT` to that id.

4. **Subsequent saves.**
   `PUT /api/studies/{id}` with `{start_fen, tree, pgn}` (add `name` when the
   user renames). Debounce it — a few hundred ms after the last edit — rather
   than firing on every move; the whole tree goes over the wire each time.
   There is no optimistic-concurrency check: last write wins.

5. **Delete.** `DELETE /api/studies/{id}`, then refresh the list.

6. **Offline / static-only fallback.** If `GET /api/health` fails, the app is
   being served without the backend (`serve.sh` fallback mode, or `file://`).
   Keep using `localStorage` and hide the server-study UI.
