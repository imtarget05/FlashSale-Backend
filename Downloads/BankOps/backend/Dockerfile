FROM python:3.11-slim

WORKDIR /app

# Install dependencies
RUN pip install --no-cache-dir poetry

# Copy poetry files and install dependencies first (for caching)
COPY pyproject.toml poetry.lock* ./
RUN pip install --no-cache-dir poetry poetry-plugin-export && \
    poetry export -o requirements.txt --without-hashes && \
    pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY app ./app
COPY alembic ./alembic
COPY alembic.ini ./alembic.ini

EXPOSE 8000

CMD ["sh", "-c", "alembic upgrade head && uvicorn app.main:app --host 0.0.0.0 --port 8000"]