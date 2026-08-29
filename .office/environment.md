# The office — environment

protocol-version: 1.0.0

**This file is the project-specific binding layer.** The four hats describe a protocol that is not
about this project at all. This file is everything about *this workspace and this machine* that the
protocol needs in order to run. If the office is lifted out and reused on another project, **the
hats travel and this file stays behind** — the new project writes its own.

**Every hat runs `/office-doctor` as its first action, and stops on FAILED.** Doctor measures this
file against the machine; reading it and believing it is exactly how a binding file that described a
filesystem which did not exist survived unnoticed upstream. Once doctor passes, this file is the
reference for content. Its contents are deliberately not repeated in the hats: one source of truth.
Where a hat and this file disagree about a path, a tool, a container or a port, this file is right.

The handoff to a hat — from a human conversation today, from the foreman later — is exactly three
things: **the label**, which selects the hat; **a pointer to this file**; and **any ticket-specific
fact that cannot be derived from the repo**. Everything else you need is here, in the repo's
`README.md`, or in the issue.

## Definitions

| variable | what it is | value |
|---|---|---|
| `${OFFICE_REPO}` | the `owner/name` slug every `gh` call is scoped to with `--repo`. The working directory is not a reliable scope. | `erkantaylan/chess-learn` |
| `${OFFICE_BASE_BRANCH}` | the branch work merges into, and this repo's GitHub default branch — which is why a pushed `Closes #N` closes instantly. | `master` |
| `${OFFICE_AGENT_NAME}` | the commit author/committer **name**, per issue, so a run's commits are attributable. | `agent/issue-<N>` |
| `${OFFICE_AGENT_EMAIL}` | the commit author/committer **email**. A bot address the project owns, not a person's. | `office-bot@chess-learn.invalid` |
| `${OFFICE_GATES}` | the build and test commands, spelled out. § Gates below says which of them a given diff calls for. | see § Gates |
| `${OFFICE_DIARY_DIR}` | **derived, never written here**: `${OFFICE_RUNTIME_ROOT}/diary/${OFFICE_REPO}`. Outside this repo, because diaries are instrumentation about *the office* and must not enter this codebase’s history. See § Diaries. | derived — see § The machine |

`${OFFICE_AGENT_IDENTITY}` is the `${OFFICE_AGENT_NAME}` / `${OFFICE_AGENT_EMAIL}` pair together.
It is always passed **inline on the commit**, never via `git config user.name` — a config write
outlives the run that made it and reattributes whatever is committed next.

**This is a Windows machine, and every path you write must be in the Git Bash form** — a
forward-slash path, not a backslash one. Both spellings reach the same directory, but only the
first is what `bash` — and therefore every office script — will accept. An agent that writes a
backslash path into a shell command here gets an escape sequence, not a directory.

## The machine

**Nothing in this file names a path on a computer**, because this file is committed and travels to
every clone of this repo, while a path is true on exactly one machine. The office supplies the one
machine fact and everything else derives from it.

| | |
|---|---|
| `${OFFICE_RUNTIME_ROOT}` | from `$OFFICE_RUNTIME_ROOT`, else the `config.yml` above your working directory |
| `${OFFICE_DIARY_DIR}` | derived: `${OFFICE_RUNTIME_ROOT}/diary/${OFFICE_REPO}` |
| your own working tree | derived: `${OFFICE_RUNTIME_ROOT}/runs/${OFFICE_REPO}/issue-<N>-<role>/` |
| the office's own source | `$OFFICE_SOURCE`, and your dispatch message names it |

This was not always so. Until protocol 1.0.0 this file named `/c/Projects/...` in eleven places, and
that is exactly why it sat uncommitted for weeks: correct on the machine that wrote it and a lie on
every other, while the hats are told this file wins over what they can see.

<!-- office-doctor: ignore-path docs/current-limitations.md -->
<!-- office-doctor: ignore-path scripts/ -->
<!-- office-doctor: ignore-path runs/ -->
The three paths above are named in this section, and none of them is in THIS repository:
`scripts/` and `docs/current-limitations.md` belong to the office's own source, and `runs/` is a
directory inside the runtime root. Doctor checks every repo-relative path a binding file names,
which is the right check — it is what catches a file describing a tree that does not exist — so the
three that are deliberately somebody else's are marked here rather than left to fail it every run.

## Workspace layout

Every run gets a **working tree of its own**, so no two agents ever share one. The table below is
everything *else* on the machine: the directories you will find next to your clone, and the rule for
each.

| what | what it is | the rule |
|---|---|---|
| **the runtime root** | the office's own working data, a git repo of its own. Everything under it is keyed by **owner and repo**, because issue numbers are per-repo and two owners can have repos of the same name. | Its `.gitignore` is a whitelist and **is** the disposability boundary: the diary tree and `config.yml` are tracked and durable, every clone and mirror under it is ignored and thrown away. Never keep anything you would miss outside the tracked half. |
| **`${OFFICE_DIARY_DIR}`** | this repo's diaries. | Write your own as a plain file. **Never delete or overwrite another run's; never commit any of it anywhere.** |
| **another run's working tree** | every other run lives beside yours under the runtime root's `runs/`. | Yours is the one matching your issue. **Never read or write another run's** — it may be mid-edit, and nothing you learn from it is reportable. |
| **the human's own checkout** | a working copy of this repo somewhere on the machine. It may be running a live Aspire stack. | **Read-only, and preferably not read at all.** Never edit it, never commit in it, never dispatch into it. Leave every container you found running still running, with the same container ID. |
| **the office's own source** | the hats, the commands and `scripts/`. Your dispatch message names where it is. | Read-only from here. A protocol change is a ticket in *that* repo, never an edit made in passing. Its `docs/current-limitations.md` is the one page that says which office commands are not installed yet. |
| **anything else on the machine** | the human's other projects, unrelated to this one. | **Off limits.** Do not read them for answers about this repo, and do not edit them. |

## Tooling reality

What is actually installed on this machine, what is missing, and the workaround for each gap.

- **This is Windows 10 with Git Bash.** `bash`, GNU `coreutils`, `sed`, `awk`, `grep` and `find` are
  present and behave as on Linux. PowerShell is also available but the office's scripts are `bash`.
- **`gh` is authenticated** as `erkantaylan`. Pass `--repo ${OFFICE_REPO}` on every call.
- **`gh issue view --comments` fails on some repos** with a Projects-classic GraphQL deprecation
  error, and the failure looks like the issue is unreadable rather than like a `gh` bug. The REST
  path works: `gh api repos/erkantaylan/chess-learn/issues/<N>/comments --jq '.[] | "--- @\(.user.login) \(.created_at)\n\(.body)"'`.
  Reading every comment on the issue is not optional for any hat, so know the fallback before you
  need it.
- **`make -f gh.mk <target>`** is the issue-dependency helper in the office's own `scripts/` directory —
  `help`, `list`, `ready`, `show`, `blocked-by`, `blocks` and their inverses. `make` is installed.
  `show` is how you verify an edge landed the way you meant instead of trusting the write.
- **.NET SDK 10.0.203** is installed. `global.json` pins `10.0.100` with `rollForward: latestFeature`,
  so the installed SDK satisfies it.
- **the `aspire` CLI is installed and on `PATH`.** Where it lives is a machine fact and belongs in the
  runtime root's `config.yml`, not here. No dotnet workloads are installed and none
  is needed — Aspire ships as a CLI plus NuGet packages now.
- **Docker Desktop is running**, server 29.2.0. Aspire starts the Postgres container through it.
- **`node` and `npm`** are on PATH via `fnm`. The frontend has **no build step** and no
  `package.json`, so they are only useful for `node --check` on a `views/*.js` file.
- **`psql` and `pg_restore` are NOT installed on this machine.** Use the Postgres image, which
  carries the client tools, and mount anything you read **read-only**:
  ```bash
  docker run --rm --network host postgres:17 psql "<connection string from the Aspire dashboard>" -c '\dt'
  ```
  The connection string is not in the repo; take it from the running dashboard.
- **`jq` is installed (1.8.2)** but the office's own scripts deliberately do not need it — every
  filter they run goes through `gh --jq`. Do not add a dependency on standalone `jq` in anything
  you write for the office.
- Non-interactive file flags only (`cp -f`, `mv -f`, `rm -rf`). Nothing may block on a prompt.
- **Inspecting is reading.** A read-only hat may run `docker ps`, `docker inspect`, `git log` and the
  like — observing a running system is not starting one. What it may not do is create, stop, restart
  or attach to anything.
- **Facts about the office's own maturity are not here.** Which office commands are installed, what
  the plugin supports — those are identical in every repo and live in
  `docs/current-limitations.md` in the office's own source. Read it once; it is one short page.

## Running the app

The app is two things that ship together: a static frontend (`index.html`, `views/`, `engine/`) with
no build step, and an ASP.NET Core + PostgreSQL backend under Aspire that also serves the frontend.

- **Start it with** `cd src/Aspire/Repertoire.AppHost/ && aspire run`. Stop it by stopping *that*
  process — see the kill rule below.
- **Ports are allocated dynamically.** `launchSettings.json` asks for `http://localhost:0` for the
  AppHost, the OTLP endpoint and the resource service, and `AspireProgram.cs` gives the API a plain
  `WithHttpEndpoint()` with no fixed port. So two stacks can run side by side without colliding, and
  **the entry point is whatever the dashboard prints** — never a port you assumed.
- **The retired Python server used port 8000.** If something is answering there, it is not the app
  you started.
- **Never `pkill -f dotnet`, and never `pkill -f aspire`.** Every clone's host process shares a name,
  so a pattern kill reaches across other agents' stacks and the human's. Take the pid of what you
  started and kill only that pid.
- **The Postgres container is shared, deliberately, and it is the one real collision on this repo.**
  `AspireProgram.cs` gives it `ContainerLifetime.Persistent` and a **named data volume**,
  `repertoire-pgdata`, so it survives AppHost restarts and every stack on this machine mounts the
  same data. Consequences, all of them load-bearing:
  - **Never `docker volume rm repertoire-pgdata`, and never `docker rm` the `db-repertoire`
    container.** The volume is where the human's studies live. The source comment records that
    losing one is not hypothetical — the first Urusov study was lost exactly this way, to an
    orphaned volume after a resource-config change recreated the container.
  - Changing anything about the Postgres resource in `AspireProgram.cs` — a version bump, a sibling
    resource like pgAdmin — changes its config hash and makes Aspire **recreate the container**. A
    ticket that touches that file is a ticket that can strand the data volume. Say so on the issue.
  - Two runs with the app up at once are **reading and writing one database**. A ticket that
    exercises the API needs `lane: repertoire-pgdata` so it cannot run beside another that does.

## Gates

**The fenced block below is read by a script**, so its shape is a contract rather than a convention:
exactly one fence in this section, `#` comment lines ignored, **the first remaining line is the build
command and the second is the test command, in that order**. `/office-doctor` runs the first and
never the second.

```bash
# build: the only compiler in this repo. ~1.5s incremental; ~31s in a fresh
# working tree, which pays for the NuGet restore once. It compiles the backend
# ONLY -- the frontend is vanilla JS with no build step, so a green build says
# nothing whatsoever about index.html, views/ or engine/. See the table below.
dotnet build Repertoire.slnx
# test: there is no test project in this solution, so there is no second line
# here and /office-doctor will report that none is declared. That is the honest
# state, not an omission: a command that runs nothing and exits 0 would read as
# a passing suite to every agent downstream. Verification on this repo is
# exercising the app -- see the "and" column below and § Verification traps.
```

**Which of them you run is decided by what the branch changed, not by which hat you wear.** Start
from the diff, not from the ticket's description of itself — `git diff --stat master..<branch>` is
the discriminator.

| what the branch actually touches | build | test | and |
|---|---|---|---|
| `src/` — any C# | yes | none exists | start the stack and exercise the endpoint through the API, signed in. `docs/API.md` is the contract |
| `index.html`, `views/`, `engine/` | yes — it is fast and worth knowing the backend is green, but it does **not** compile any of this | none exists | `node --check` any `views/*.js` you touched, then **load the app over HTTP and drive it in a browser**. This is the only gate this part of the repo has |
| a migration under `src/Api/Repertoire.Api/Migrations/` | yes | none exists | apply it against the shared volume knowingly — see § Running the app. A migration is the one change that can cost the human data |
| only `*.md`, `docs/`, `.office/` | yes — instant, and worth knowing the base branch is green | none exists | verify the deliverable on its own terms: re-run the commands the document gives, re-derive any number it states |

**Any gate you skip must be stated on the issue, with its reason and the evidence that skipping was
safe.** An empty `git diff --stat` over the source paths is evidence; "it's only docs" is an
assertion. A silently skipped gate and a gate that passed look identical to the next agent, and that
is the failure this rule exists to prevent.

## Verification traps

**The most valuable section in this file.** These are the ways this specific machine and this
specific stack will let you conclude "verified" when you have not. Add to it whenever a hat's diary
reports a new one.

The entries below were read out of `README.md` and `AspireProgram.cs` rather than learned by getting
them wrong, which makes them a starting set and not a complete one. **Add what you learn.**

- **Opening `index.html` from disk silently disables the engine.** Stockfish needs a Web Worker, and
  browsers refuse to load one from a `file://` origin. Everything else in the app works that way, so
  the failure is partial and quiet. *The wrong conclusion it produces:* "the engine is broken on this
  branch", or worse, "the engine works" after testing everything except it. Serve the app over HTTP
  — that is what `aspire run` is for — before you judge anything involving evaluation, arrows or
  suggestions.
- **Signed out, nothing is written down.** The app is fully usable without an account: you can open a
  study, branch it, name lines, run the engine. But every change lives in memory and is gone on
  refresh. *The wrong conclusion:* "saving is broken" — or a passing verdict on a persistence ticket
  that never persisted anything. Sign in first. Credentials come from configuration
  (`Auth__Username` / `Auth__Password`); `src/Api/Repertoire.Api/appsettings.Development.json`
  carries a throwaway pair so the app runs out of the box.
- **`localStorage` holds a local working copy, so a reload can show you state the server never
  saw.** *The wrong conclusion:* "the study saved" when the round trip never happened. To test server
  persistence, use a fresh browser profile or clear site data — a reload alone proves nothing.
- **`legacy/python/` is a second, retired backend implementing the same JSON API**, with no accounts
  and a wide-open `/api`. *The wrong conclusion:* a green verification that measured the retired
  server. `src/` is the one being built; if what answered you needed no authentication, you were
  talking to the wrong backend.
- **The database survives everything.** Persistent container plus a named volume means a test you ran
  an hour ago is still in there, and so is the human's real data. *The wrong conclusion:* "the fixture
  I see was created by my run." Check what you are looking at before you assert it.
- <!-- TODO: add the first trap a hat finds that is about this machine rather than this codebase —
     an SDK that needs a flag here, a browser that will not start headless, a path length limit.
     Every entry must name the wrong conclusion it prevents, not just the fact. -->

A review that walked into one of these and concluded "cannot verify here" is a failed review, not a
constrained one.

## Scratchpad

Your system prompt names a session scratchpad directory outside the repo. **Use it.** Draft comment
bodies there, keep command output there, think there. That is legitimate and expected, and it is not
the same thing as writing to the repo — the hats' restrictions on writing are about the repository,
its branches and its history, never about the scratchpad.

Nothing in the scratchpad survives you, so anything that matters still has to reach the issue or the
diary before you exit.

## Diaries

Diaries live **outside this repository**, at:

```
${OFFICE_DIARY_DIR}/<YYYY-MM-DD>-issue-<N>-<role>.md
```

The scheme keys on date + issue + role rather than a running number, because numbered schemes
collide the moment two runs finish at once. If the file already exists, **append a dated section;
never overwrite** — a repeated role means the previous attempt failed, and why is the most valuable
thing in the file.

**Writing a diary is a plain file write. There is no commit, no `git add`, no pull, no push, and no
`Refs #<N>` — ever.** A diary is instrumentation about the *office*, not about this codebase, and
nobody reading this repo's history wants six commits about a gate script's timezone bug. That is not
a style preference: the office's first four-hat ticket left nine commits on the base branch, of which
one was the feature and six were diaries.

Because the diary is no longer a commit, a hat that authored nothing else authors **no commits at
all**, and `done-check.sh --no-commits` is the check for that case (it asserts the range is empty and
fails if it is not).

### What a good one looks like

Two halves, both required, answering different questions.

- **Technical — what this ticket taught us about the codebase.** What you measured, what surprised
  you, what the issue or the approach got wrong about the repo, what you deliberately did not do,
  and what you could not verify. Depth: enough that someone who never saw this ticket can act on it
  — numbers, paths, commands and measured values, not adjectives. If you ruled something out, say
  what ruled it out.
- **Process — what this ticket taught us about the office.** What you were handed, whether it was
  enough to work from, how many round-trips it cost, where you waited, and **any rule you had to
  invent because no file covered it**. Depth: name the specific gap and the file that should have
  carried it. "The handoff was enough, zero round-trips" is a legitimate finding and takes one line;
  a gap takes a paragraph.

The halves are not the same length by rule. A smooth ticket has a short process half; a ticket that
fought you has a long one.

**An agent that needed something no file contained is reporting a bug, not a preference.** A fact
that only ever travels in a dispatch message works exactly once.

### Diaries are instrumentation, not documentation

They exist to tell us whether the office is working, which is a question with an end date.
**The scheduled read for this repo is at 20 office tickets** — count the diary files in
`${OFFICE_DIARY_DIR}`; when there are 20, read all of them, judge what was actually useful, then
decide whether to retire the practice, keep it, or narrow it to the process half. That is a
**scheduled read, not scheduled deletion** — nothing here expires on its own, and no diary is
deleted before that read happens.
