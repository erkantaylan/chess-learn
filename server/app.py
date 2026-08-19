"""Repertoire Table backend: serves the static app + a JSON API on one port.

Run:  uvicorn server.app:app --host 0.0.0.0 --port 8000   (from the repo root)
Env:  PORT (default 8000), DB_PATH (default <repo>/data/repertoire.sqlite3)
"""
import mimetypes
import os
from pathlib import Path
from typing import Any, Optional

from fastapi import APIRouter, FastAPI, HTTPException, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from . import db

# Browsers refuse to instantiate WASM served as octet-stream.
mimetypes.add_type("application/wasm", ".wasm")
mimetypes.add_type("application/javascript", ".js")

REPO_ROOT = Path(__file__).resolve().parent.parent

app = FastAPI(title="Repertoire Table API", version="1.0")


@app.on_event("startup")
def _startup() -> None:
    db.init_db()


app.add_middleware(
    CORSMiddleware,
    allow_origin_regex=r"^https?://(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$",
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.middleware("http")
async def cross_origin_isolation_and_no_cache(request, call_next):
    """SharedArrayBuffer needs COOP+COEP; COEP needs CORP on the assets themselves.

    Also kills caching so a freshly edited index.html is never served stale.
    """
    response = await call_next(request)
    response.headers["Cross-Origin-Opener-Policy"] = "same-origin"
    response.headers["Cross-Origin-Embedder-Policy"] = "require-corp"
    response.headers["Cross-Origin-Resource-Policy"] = "same-origin"
    response.headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0"
    response.headers["Pragma"] = "no-cache"
    response.headers["Expires"] = "0"
    return response


# ---------------------------------------------------------------- API models

class StudyCreate(BaseModel):
    name: str = Field(min_length=1, max_length=200)
    start_fen: str = ""
    tree: Any = None
    pgn: str = ""


class StudyUpdate(BaseModel):
    name: Optional[str] = Field(default=None, min_length=1, max_length=200)
    start_fen: Optional[str] = None
    tree: Any = None
    pgn: Optional[str] = None


api = APIRouter(prefix="/api")


@api.get("/health")
def health() -> dict:
    return {"ok": True, "studies": db.count_studies()}


@api.get("/studies")
def list_studies() -> list[dict]:
    return db.list_studies()


@api.post("/studies", status_code=201)
def create_study(body: StudyCreate) -> dict:
    return db.create_study(body.name, body.start_fen, body.tree, body.pgn)


@api.get("/studies/{study_id}")
def get_study(study_id: int) -> dict:
    rec = db.get_study(study_id)
    if rec is None:
        raise HTTPException(status_code=404, detail=f"study {study_id} not found")
    return rec


@api.put("/studies/{study_id}")
def update_study(study_id: int, body: StudyUpdate) -> dict:
    # Only fields actually present in the request body are touched.
    fields = body.model_dump(exclude_unset=True)
    if not fields:
        rec = db.get_study(study_id)
        if rec is None:
            raise HTTPException(status_code=404, detail=f"study {study_id} not found")
        return rec
    rec = db.update_study(study_id, fields)
    if rec is None:
        raise HTTPException(status_code=404, detail=f"study {study_id} not found")
    return rec


@api.delete("/studies/{study_id}", status_code=204)
def delete_study(study_id: int) -> Response:
    if not db.delete_study(study_id):
        raise HTTPException(status_code=404, detail=f"study {study_id} not found")
    return Response(status_code=204)


app.include_router(api)

# Static app last, so /api/* always wins. html=True maps / -> index.html.
app.mount("/", StaticFiles(directory=str(REPO_ROOT), html=True), name="static")


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=int(os.environ.get("PORT", "8000")))
