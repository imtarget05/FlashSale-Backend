FROM python:3.11-slim

WORKDIR /app/backend

COPY backend/pyproject.toml backend/poetry.lock* ./
RUN pip install --no-cache-dir poetry poetry-plugin-export && \
    poetry export -o requirements.txt --without-hashes && \
    pip install --no-cache-dir -r requirements.txt

COPY backend/app ./app
COPY backend/alembic ./alembic
COPY backend/alembic.ini ./alembic.ini

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]