"""Fail a release when Cloudflare serves a stale or misconfigured API bundle."""

import os
import re
import time
import urllib.parse
import urllib.request


def fetch(url: str) -> str:
    request = urllib.request.Request(url, headers={"Cache-Control": "no-cache"})
    with urllib.request.urlopen(request, timeout=20) as response:
        return response.read().decode("utf-8", "replace")


def main() -> None:
    page_url = os.environ["PAGE_URL"].rstrip("/") + "/"
    expected_api_url = os.environ["EXPECTED_API_URL"].rstrip("/")
    if not expected_api_url.startswith("https://"):
        raise SystemExit("Expected API URL must use HTTPS")

    for attempt in range(10):
        try:
            html = fetch(page_url)
            scripts = re.findall(r'<script[^>]+src="([^"]+\.js)"', html)
            if not scripts:
                raise ValueError("No JavaScript bundle in deployed page")
            bundle = "\n".join(fetch(urllib.parse.urljoin(page_url, path)) for path in scripts)
            if expected_api_url in bundle and "http://localhost:8081" not in bundle:
                print(f"Deployed browser API target verified: {expected_api_url}")
                return
            raise ValueError("Deployed bundle has a stale or local API target")
        except (OSError, ValueError) as exc:
            if attempt == 9:
                raise SystemExit(str(exc)) from exc
            time.sleep(10)


if __name__ == "__main__":
    main()
