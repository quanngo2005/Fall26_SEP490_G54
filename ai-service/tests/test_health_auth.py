"""Tests for /health endpoint and authentication."""

from uuid import uuid4

from fastapi.testclient import TestClient


def test_health_endpoint_no_auth(client: TestClient) -> None:
    resp = client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data == {"status": "ok", "pipeline_version": "0.1.0"}


def test_analyze_auth_enforcement(client: TestClient) -> None:
    # 1. Missing key
    resp_missing = client.post("/v1/analyze")
    assert resp_missing.status_code == 401
    assert resp_missing.json() == {
        "error_code": "UNAUTHORIZED",
        "message": "Missing service key",
    }

    # 2. Wrong key
    resp_wrong = client.post(
        "/v1/analyze",
        headers={"X-Service-Key": "wrong-key-000000000000"},
    )
    assert resp_wrong.status_code == 401
    assert resp_wrong.json() == {
        "error_code": "UNAUTHORIZED",
        "message": "Invalid service key",
    }


def test_detect_auth_enforcement(client: TestClient) -> None:
    payload = {
        "recording_id": str(uuid4()),
        "segments": [
            {
                "id": 0,
                "start_ms": 0,
                "end_ms": 1000,
                "speaker": "UNKNOWN",
                "text_masked": "",
                "service_code": None,
            }
        ],
    }

    # 1. Missing key
    resp_missing = client.post("/v1/detect", json=payload)
    assert resp_missing.status_code == 401
    assert resp_missing.json() == {
        "error_code": "UNAUTHORIZED",
        "message": "Missing service key",
    }

    # 2. Wrong key
    resp_wrong = client.post(
        "/v1/detect",
        json=payload,
        headers={"X-Service-Key": "wrong-key-000000000000"},
    )
    assert resp_wrong.status_code == 401
    assert resp_wrong.json() == {
        "error_code": "UNAUTHORIZED",
        "message": "Invalid service key",
    }
