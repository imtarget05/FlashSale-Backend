#!/usr/bin/env python3
"""Crash a test-classpath JVM after DB commit and before HTTP body serialization."""

import http.client
import json
import os
import secrets
import signal
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SERVICE = ROOT / "src" / "payment-service-java"
COMPOSE = SERVICE / "docker-compose.payment-java.yml"
PROJECT = "flashsale-pre-response-drill"
BASE_URL = "http://127.0.0.1:18083"


def run(command, *, env, cwd=None):
    return subprocess.run(command, env=env, cwd=cwd, check=True, text=True,
                          capture_output=True)


def request_json(path, payload, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(BASE_URL + path, data=json.dumps(payload).encode(), headers=headers)
    with urllib.request.urlopen(request, timeout=15) as response:
        return response.status, json.load(response)


def wait_for_api(process):
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"demo JVM exited during startup: {process.returncode}")
        try:
            with urllib.request.urlopen(BASE_URL + "/healthz", timeout=2) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, TimeoutError, ConnectionResetError,
                http.client.RemoteDisconnected):
            pass
        time.sleep(1)
    raise TimeoutError("demo JVM did not become healthy")


def stop_process(process):
    if process is not None and process.poll() is None:
        os.killpg(process.pid, signal.SIGTERM)
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5)


def main():
    key = "pre-response-" + str(uuid.uuid4())
    password = secrets.token_urlsafe(24)
    env = os.environ.copy()
    env.update({
        "POSTGRES_USER": "postgres", "POSTGRES_PASSWORD": password,
        "RABBITMQ_USER": "drill", "RABBITMQ_PASSWORD": password,
        "JWT_SIGNING_KEY": secrets.token_urlsafe(48), "DEMO_AUTH_PASSWORD": password,
        "SPRING_DATASOURCE_URL": "jdbc:postgresql://127.0.0.1:55432/FlashSaleDb",
        "SPRING_DATASOURCE_USERNAME": "postgres", "SPRING_DATASOURCE_PASSWORD": password,
        "SPRING_RABBITMQ_HOST": "127.0.0.1", "SPRING_RABBITMQ_PORT": "55672",
        "SPRING_RABBITMQ_USERNAME": "drill", "SPRING_RABBITMQ_PASSWORD": password,
        "SPRING_DATA_REDIS_HOST": "127.0.0.1", "SPRING_DATA_REDIS_PORT": "56380",
        "PORT": "18083", "CRASH_DRILL_KEY": key,
    })
    override = """services:
  postgres:
    ports: ['127.0.0.1:55432:5432']
  rabbitmq:
    ports: ['127.0.0.1:55672:5672']
  redis:
    ports: ['127.0.0.1:56380:6379']
"""
    process = None
    with tempfile.TemporaryDirectory(prefix="flashsale-pre-response-") as directory:
        override_path = Path(directory) / "compose.override.yml"
        log_path = Path(directory) / "jvm.log"
        override_path.write_text(override)
        compose = ["docker", "compose", "-p", PROJECT, "-f", str(COMPOSE), "-f", str(override_path)]
        try:
            run(compose + ["up", "-d", "--wait", "postgres", "rabbitmq", "redis"], env=env)
            classpath_file = Path(directory) / "classpath.txt"
            run(["mvn", "-B", "-q", "test-compile", "dependency:build-classpath",
                 "-Dmdep.includeScope=test", f"-Dmdep.outputFile={classpath_file}"],
                env=env, cwd=SERVICE)
            classpath = os.pathsep.join((str(SERVICE / "target" / "classes"),
                                         str(SERVICE / "target" / "test-classes"),
                                         classpath_file.read_text().strip()))

            def start_jvm(profile, *, fault=False):
                child_env = {**env, "SPRING_PROFILES_ACTIVE": profile}
                output = log_path.open("a")
                try:
                    main_class = ("com.flashsale.payment.integration.CrashDrillApplication"
                                  if fault else "com.flashsale.payment.PaymentApplication")
                    command = ["java", "-cp", classpath, main_class]
                    return subprocess.Popen(
                        command,
                        cwd=SERVICE, env=child_env, stdout=output, stderr=subprocess.STDOUT,
                        start_new_session=True,
                    )
                finally:
                    output.close()

            process = start_jvm("demo,crash-drill", fault=True)
            wait_for_api(process)
            _, login = request_json("/api/auth/login",
                                    {"email": "staff@flashsale.local", "password": password})
            token = login["token"]
            payment = {"paymentId": str(uuid.uuid4()), "orderId": 1,
                       "idempotencyKey": key, "amount": "10.01", "currency": "USD"}
            response_lost = False
            try:
                request_json("/api/payments", payment, token)
            except (http.client.RemoteDisconnected, ConnectionResetError, urllib.error.URLError):
                response_lost = True
            if not response_lost:
                raise AssertionError("client unexpectedly received a response")
            process.wait(timeout=30)
            crash_exit_code = process.returncode
            log_text = log_path.read_text()
            marker = "CRASH_AFTER_COMMIT_BEFORE_RESPONSE key=" + key
            if marker not in log_text or process.returncode == 0:
                raise AssertionError("test-only pre-write hook did not terminate the JVM")

            def db_value(sql):
                return run(compose + ["exec", "-T", "postgres", "psql", "-U", "postgres",
                                      "-d", "FlashSaleDb", "-Atc", sql], env=env).stdout.strip()

            before = {table: int(db_value(
                f"SELECT count(*) FROM {table} WHERE idempotency_key = '{key}'"))
                for table in ("payment_records", "payment_outbox")}
            transaction = db_value(
                f"SELECT transaction_id FROM payment_records WHERE idempotency_key = '{key}'")
            if before != {"payment_records": 1, "payment_outbox": 1} or not transaction:
                raise AssertionError(f"commit did not survive JVM halt: {before}")

            process = start_jvm("demo")
            wait_for_api(process)
            retry_status, retry = request_json("/api/payments", payment, token)
            after = {table: int(db_value(
                f"SELECT count(*) FROM {table} WHERE idempotency_key = '{key}'"))
                for table in ("payment_records", "payment_outbox")}
            if retry_status != 200 or retry["transactionId"] != transaction or after != before:
                raise AssertionError(f"retry changed committed state: {after}")
            print(json.dumps({"result": "passed", "key": key, "pre_write_hook": True,
                              "client_response_lost": True, "jvm_exit_code": crash_exit_code,
                              "retry_status": retry_status, "before": before, "after": after}, indent=2))
        except Exception:
            if log_path.exists():
                print(log_path.read_text()[-4000:], file=os.sys.stderr)
            raise
        finally:
            stop_process(process)
            run(compose + ["down", "-v"], env=env)


if __name__ == "__main__":
    main()
