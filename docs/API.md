# Repertoire Table API

Everything under `/api`. All requests and responses are JSON
(`Content-Type: application/json`). The same process serves `index.html`,
`views/` and `engine/`, so the frontend uses **relative** URLs
(`fetch('/api/studies')`) — no CORS involved, and the session cookie below only
works same-origin anyway.

Two backends implement this contract:

| | base URL | store | accounts |
|---|---|---|---|
| .NET (`src/`) | Aspire assigns the port; `http://localhost:5080` under compose | PostgreSQL, `tree` in a `jsonb` column | yes — the whole API needs a session, and each account has its own library |
| Python (`legacy/python/`, retired) | `http://localhost:8000` | SQLite, `tree` in a TEXT column | none — wide open |

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
  "updated_at": "2026-08-19T16:59:59Z",
  "owned": true
}
```

- `id` — integer, server-assigned, never sent by the client.
- `name` — non-empty string, max 200 chars.
- `start_fen` — string, may be `""`.
- `tree` — **arbitrary JSON**, stored opaquely. Send the same object the app
  keeps in `localStorage` under `repertoire-table-v3`, or just its `tree` member —
  the server does not validate or rewrite it.
  The only thing the server reads out of it is `children` (recursively) to
  compute `move_count` for the list endpoint.
  On the .NET backend it lands in a Postgres `jsonb` column, so what comes back
  is **semantically** identical but not byte-identical: key order is not
  preserved, whitespace is dropped and duplicate keys collapse. Nothing in the
  app reads the tree positionally, so this does not matter — but it is queryable
  now, which the SQLite TEXT column never was:

  ```sql
  SELECT name, tree->'children'->0->>'san' AS first_move FROM studies;
  ```

  Move trees nest two JSON levels per ply, so the .NET backend raises the
  serializer depth limit to 512 (~255 plies). The stock limit of 64 rejects a
  study around move 32.
- `pgn` — string, may be `""`.
- `owned` — boolean, response only: whether the caller owns this study and so
  may `PUT`/`DELETE` it. `false` for a study opened by id from someone else's
  link. The owner's identity itself is never sent. (.NET backend only; the
  Python backend has no accounts and does not send it.)

---

## Authentication

Two sign-in modes, picked by configuration; there is no registration and no
users table in either.

- **Password login** (default): exactly one account, `Auth__Username` /
  `Auth__Password` as env vars (or user-secrets locally). The app refuses to
  boot outside Development if they are unset.
- **Central login** (`Auth__Oidc__Authority` set): sign-in goes to the OpenID
  Connect provider, and `POST /api/login` does not exist (404).

Every study has an owner, and each account sees its own library:

| mode | owner id |
|---|---|
| central login | the provider's `sub` claim (not the username, which can change) |
| password login | the configured `Auth__Username` |

You list, update and delete only your own studies. Any session allowed to use
`/api/studies` at all can **read** any study by id — a study link is shareable,
read-only. Saving a copy of someone else's study is an ordinary `POST` of the
tree under a new name; there is no clone endpoint.

Everything under `/api/studies` requires a session. `/api/health`, `/api/me`,
`/api/login` and `/api/logout` are anonymous, as is the frontend itself — the
app is fully usable signed out, it just has nowhere to save.

### `POST /api/login`

```
curl -s -c jar -X POST http://localhost:5080/api/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"erkan","password":"..."}'
```
```json
{"authed": true, "user": "erkan", "roles": [], "can_save": true}
```

Sets an HttpOnly session cookie (`repertoire.auth`), 30 days, sliding, `Secure`
whenever the request arrived over HTTPS. Wrong credentials give **401** with
`{"detail": "Wrong username or password"}`; both the username and password legs
are compared in fixed time.

### `POST /api/logout`

Clears the cookie. Responds `{"authed": false, "user": null, "roles": [], "can_save": false}`.

Signs you out of **this app only**, under the central login too — the
provider's own session is left alone.

### `GET /login?next=…` and `GET /logout` (page navigation, not `/api`)

`/login` is the sign-in page under the password login, and a redirect to the
provider under the central login. Either way you land on `next` afterwards
(already signed in: straight away). `next` must be a path on this origin —
anything else (`//host`, `/\host`, an absolute URL) is replaced by `/`. The
board's Sign in button links to `/login?next=/`.

`/logout` clears the cookie and redirects to `/`. Like `POST /api/logout` it
signs out of this app only, in both modes: it does not visit the provider's
end-session endpoint.

### `GET /api/me`

```json
{"authed": true, "user": "erkan", "roles": [], "can_save": true}
```

`roles` are the central identity provider's (empty under the password login).
`can_save` is whether `/api/studies` will accept this session: a guest of the
central login is `authed` but not `can_save`, and gets **403** there.

Anonymous, so the frontend can ask "am I signed in?" on load. **404 on the
Python backend**, which has no accounts — the frontend reads that as "not signed
in", which is the right answer for a server that cannot authenticate anyone.

### Unauthenticated requests

Anything under `/api/studies` without a valid cookie returns **401** with an
empty body — not a redirect to a login page. A session that lapses mid-use
surfaces the same way, and the frontend folds it back into the signed-out state.

Under the central login, a session without the `user` role (a guest) gets
**403** there instead — signed in, but no access to the library. That is the
only 403 `/api/studies` gives: a write to a study that is not yours is a
**404** (below), so a 403 always means "this account has no access".

---

## `GET /api/health`

```
curl -s http://localhost:8000/api/health
```
```json
{"ok": true, "studies": 1}
```

Anonymous. `studies` counts every study on the server, not the caller's.

## `GET /api/studies`

Listing of **your own** studies only — **no `tree`, no `pgn` and no `owned`**
(every row is yours) in the payload, so it stays cheap. Sorted by `updated_at`
descending (most recently saved first).

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
`pgn` (default `""`) are optional. The study is owned by the caller. Responds
**201** with the full record including the new `id` (and `"owned": true`).

```
curl -s -X POST http://localhost:8000/api/studies \
  -H 'Content-Type: application/json' \
  -d '{"name":"Italian Game","start_fen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1","tree":{"san":"","children":[{"san":"e4","children":[]}]},"pgn":"1. e4 *"}'
```

Missing or empty `name` -> **422** with FastAPI's validation detail body.

## `GET /api/studies/{id}`

Full record of **any** study, yours or not — this is what a shared link opens.
`owned` says which. **404** `{"detail": "study 999 not found"}` if it does not
exist.

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

**404** `{"detail": "study 7 is not one of your studies"}` when the study
exists but is someone else's — deliberately not 403 (see
[Unauthenticated requests](#unauthenticated-requests)). Nothing is changed.

## `DELETE /api/studies/{id}`

**204** with an empty body on success, **404** if the id is unknown, and
**404** `{"detail": "study 7 is not one of your studies"}` if it is someone
else's (the study is kept).

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
   is semantically the JSON you sent (jsonb, see above).
   If `owned` is `false` it is someone else's: show it read-only, and save
   changes as a copy (step 3, under a new name) rather than a `PUT`, which
   would 404.

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
   being served without a backend (the retired static-only fallback, or `file://`).
   Keep using `localStorage` and hide the server-study UI.
