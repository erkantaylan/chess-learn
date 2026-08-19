# Repertoire Table: static app + JSON API in one container.
# NB: engine/ ships a ~7.3 MB Stockfish .wasm, so the image lands around 200 MB.
FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PORT=8000 \
    DB_PATH=/app/data/repertoire.sqlite3

WORKDIR /app

COPY server/requirements.txt /app/server/requirements.txt
RUN pip install --no-cache-dir -r /app/server/requirements.txt

COPY server/ /app/server/
COPY engine/ /app/engine/
COPY index.html /app/index.html

RUN mkdir -p /app/data
VOLUME ["/app/data"]

EXPOSE 8000
CMD ["sh", "-c", "exec uvicorn server.app:app --host 0.0.0.0 --port ${PORT}"]
