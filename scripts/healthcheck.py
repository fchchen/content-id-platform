#!/usr/bin/env python3
import json
import sys
import time
import urllib.error
import urllib.request


BASE_URL = "http://localhost:18080"


def request(method, path, body=None):
    data = None
    headers = {}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(f"{BASE_URL}{path}", data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=10) as response:
        payload = response.read().decode("utf-8")
        if not payload:
            return response.status, None
        try:
            return response.status, json.loads(payload)
        except json.JSONDecodeError:
            return response.status, payload


def wait_for_api():
    for _ in range(30):
        try:
            status, _ = request("GET", "/health")
            if status == 200:
                return
        except (urllib.error.URLError, TimeoutError):
            time.sleep(2)
    raise RuntimeError("API did not become healthy")


def main():
    wait_for_api()
    submission = {
        "title": "Interview Demo Track",
        "sourcePlatform": "PartnerUpload",
        "contentType": "audio",
        "durationSeconds": 191,
        "fingerprintHash": "abc123",
    }
    status, created = request("POST", "/v1/submissions", submission)
    if status != 201:
        raise RuntimeError(f"unexpected create status: {status}")

    submission_id = created["submissionId"]
    for _ in range(30):
        _, current = request("GET", f"/v1/submissions/{submission_id}")
        if current["status"] in ("matched", "no_match", "failed"):
            _, matches = request("GET", f"/v1/submissions/{submission_id}/matches")
            print(json.dumps({"submission": current, "matches": matches}, indent=2))
            return
        time.sleep(2)

    raise RuntimeError(f"submission {submission_id} did not finish processing")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"healthcheck failed: {exc}", file=sys.stderr)
        sys.exit(1)
