#!/usr/bin/env python3
"""Verify a PostgreSQL credential works. Reads env vars only; prints no secrets.

Usage: DB_URL=... DB_USER=... DB_PASSWORD=... python3 db_credential_check.py <label>
Exit 0 = connection OK, exit 1 = failed.
"""
import os
import sys
import urllib.parse

import psycopg2

label = sys.argv[1] if len(sys.argv) > 1 else "db"
url = os.environ.get("DB_URL", "")
if url.startswith("jdbc:"):
    url = url[len("jdbc:"):]
user = os.environ.get("DB_USER", "")
password = os.environ.get("DB_PASSWORD", "")

parsed = urllib.parse.urlparse(url)
try:
    conn = psycopg2.connect(
        host=parsed.hostname,
        port=parsed.port or 5432,
        dbname=(parsed.path or "/neondb").lstrip("/") or "neondb",
        user=user or parsed.username,
        password=password or parsed.password,
        sslmode="require",
        connect_timeout=10,
    )
    with conn.cursor() as cur:
        cur.execute("SELECT current_user, count(*) FROM pg_catalog.pg_roles WHERE rolname = current_user")
        role, _ = cur.fetchone()
    conn.close()
    print(f"RESULT[{label}]: connection successful (role authenticated)")
    sys.exit(0)
except Exception as exc:  # noqa: BLE001 - report class only, never the DSN
    print(f"RESULT[{label}]: REJECTED -> {type(exc).__name__}: {str(exc)[:400]}")
    sys.exit(1)
