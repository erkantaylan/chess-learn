"""Dependency-free fallback server: static files only, no API, no database.

Used by ./serve.sh when FastAPI/uvicorn are not installed. It still sets the
COOP/COEP/CORP headers and the application/wasm type, so Stockfish works.
"""
import os
import sys
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


class Handler(SimpleHTTPRequestHandler):
    extensions_map = {
        **SimpleHTTPRequestHandler.extensions_map,
        ".wasm": "application/wasm",
        ".js": "application/javascript",
    }

    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cross-Origin-Resource-Policy", "same-origin")
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        super().end_headers()

    def log_message(self, fmt, *args):
        sys.stderr.write("[static] %s\n" % (fmt % args))


def main() -> None:
    port = int(os.environ.get("PORT", "8000"))
    handler = partial(Handler, directory=str(REPO_ROOT))
    with ThreadingHTTPServer(("0.0.0.0", port), handler) as httpd:
        print(f"[static] serving {REPO_ROOT} on http://localhost:{port}", flush=True)
        httpd.serve_forever()


if __name__ == "__main__":
    main()
