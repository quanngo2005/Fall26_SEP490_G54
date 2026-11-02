"""Tests for /v1/detect endpoint."""

from uuid import uuid4

from fastapi.testclient import TestClient


def test_detect_success(client: TestClient, auth_headers: dict[str, str]) -> None:
    payload = {
        "recording_id": str(uuid4()),
        "product_code": "PROD_A",
        "work_date": "2026-11-01",
        "segments": [
            {
                "id": 0,
                "start_ms": 0,
                "end_ms": 2000,
                "speaker": "UNKNOWN",
                "text_masked": "",
                "service_code": None,
            }
        ],
    }

    resp = client.post("/v1/detect", headers=auth_headers, json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data == {
        "pipeline_version": "0.1.0",
        "stage": "W1_STUB",
        "kb_version": None,
        "service_spans": [],
        "flags": [],
    }


def test_detect_empty_segments_invalid(client: TestClient, auth_headers: dict[str, str]) -> None:
    payload = {
        "recording_id": str(uuid4()),
        "segments": [],
    }
    resp = client.post("/v1/detect", headers=auth_headers, json=payload)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_REQUEST"
    assert set(resp.json().keys()) == {"error_code", "message"}


def test_detect_end_ms_less_than_start_ms(client: TestClient, auth_headers: dict[str, str]) -> None:
    payload = {
        "recording_id": str(uuid4()),
        "segments": [
            {
                "id": 0,
                "start_ms": 5000,
                "end_ms": 3000,
                "speaker": "UNKNOWN",
                "text_masked": "",
                "service_code": None,
            }
        ],
    }
    resp = client.post("/v1/detect", headers=auth_headers, json=payload)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_REQUEST"
    assert set(resp.json().keys()) == {"error_code", "message"}


def test_detect_end_ms_equal_start_ms(client: TestClient, auth_headers: dict[str, str]) -> None:
    payload = {
        "recording_id": str(uuid4()),
        "segments": [
            {
                "id": 0,
                "start_ms": 1000,
                "end_ms": 1000,
                "speaker": "UNKNOWN",
                "text_masked": "",
                "service_code": None,
            }
        ],
    }
    resp = client.post("/v1/detect", headers=auth_headers, json=payload)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_REQUEST"
    assert set(resp.json().keys()) == {"error_code", "message"}


def test_detect_extra_fields_forbidden(client: TestClient, auth_headers: dict[str, str]) -> None:
    payload = {
        "recording_id": str(uuid4()),
        "unexpected_field": "disallowed",
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
    resp = client.post("/v1/detect", headers=auth_headers, json=payload)
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_REQUEST"
    assert set(resp.json().keys()) == {"error_code", "message"}
