# BankOps Backend API

## Setup

```bash
cd backend
poetry install
poetry run python -m uvicorn app.main:app --reload --port 8000
```

## Environment

Copy `.env.example` to `.env` and configure.

## API Docs

- Swagger UI: http://localhost:8000/docs
- ReDoc: http://localhost:8000/redoc
- OpenAPI JSON: http://localhost:8000/openapi.json

## Health Check

- Liveness: `GET /health/live`
- Readiness: `GET /health/ready`