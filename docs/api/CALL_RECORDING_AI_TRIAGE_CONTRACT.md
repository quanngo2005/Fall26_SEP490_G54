# Call Recording & AI Triage Contract

Status: **contract agreed, not implemented** (AI-01, 23/11/2026). Plan: `Project/Documents/ai-plan/replan-3w.md` §3–§4. Decisions: R1–R4 in `ai-plan/DECISIONS.md`, ADR in `docs/adr/ADR-001-ai-call-triage.md`.

Every response uses the `ApiResponse` envelope (`API_CONVENTIONS.md`). Tables: `call_recording`, `call_transcript`, `ai_review_flag`, plus 4 columns on `risk_event`.

## 0. Principles (do not break)

- AI only **suggests segments to listen to**. It never sets severity, penalty points or a contribution, and never creates a Risk Event. Only a QA user creates a Risk Event (UC113), choosing the severity.
- UI never shows the word "vi phạm" for an AI flag and never shows `rankScore` as a score; it is a sort key only.
- Stored audio is the **PII-muted** copy (the original file is replaced after processing, Q4b). Transcripts are stored **masked** only. Logs never contain transcript text or client file names.
- The pipeline runs once, right after upload (D1 = A). No batch scan job in the Must scope.

## 1. Flow

```
Teller submits a result (UC86) ─▶ POST /submissions/{id}/call-recordings   call_recording PENDING
AudioPipelineBackgroundService (FOR UPDATE SKIP LOCKED, ≤ 3 attempts, timeout)
   ─▶ ai-service POST /v1/analyze  (normalize → ASR → number normalisation → PII mask → PII mute → detectors + guardrail)
   ◀─ muted audio + masked segments + flags
   1 transaction: call_recording READY (file replaced by the muted copy) · call_transcript · ai_review_flag (PENDING)
QA: SCR-REV-06 tab "Đoạn cần nghe lại" ─▶ SCR-AI-03 ─▶ confirm / dismiss (reason)
   confirm ─▶ (QA action) SCR-RSK-01-M1 prefilled with recording + media_start_ms/end_ms + ai_review_flag_id
```

`processing_status`: `PENDING → PROCESSING → READY | FAILED`. `FAILED` is final for that row; the Teller uploads again (a new row). A worker crash leaves `PROCESSING` rows older than the timeout to be picked again while `attempts < 3`.

## 2. Permissions

| Endpoint | Rule |
|---|---|
| `POST /submissions/{id}/call-recordings` | Caller is the submitter of the submission (same rule as `POST /works/{id}/submissions`); submission not withdrawn |
| `GET /submissions/{id}/call-recordings` | Submitter, or `call_recording:play` (`All`) |
| `GET /call-recordings/{id}` | Submitter, or `call_recording:play` (`All`) |
| `GET /call-recordings/{id}/stream` | Submitter, or `call_recording:play` (`All`); only when `READY` |
| `GET /call-recordings/{id}/transcript`, `/trace` | `ai_review_flag:read` (`All`) |
| `GET /ai-review-flags`, `GET /ai-review-flags/{id}` | `ai_review_flag:read` (`All`) |
| `POST /ai-review-flags/{id}/confirm`, `/dismiss` | `ai_review_flag:review` (`All`); not on a flag about the caller (403) |

Seed: `ai_review_flag:read`, `ai_review_flag:review`, `call_recording:play` → `VERIFIER` (the QA role; role `QA` was merged into `VERIFIER` on 19/11) and `SYSTEM_ADMIN`.

## 3. Upload — `POST /submissions/{submissionId}/call-recordings`

`multipart/form-data`: `file` (required), `productCode` (optional, ≤ 32; default = `work_type.product_code` of the work).

- Accepted: `audio/mpeg` (.mp3), `audio/mp4`/`audio/x-m4a` (.m4a), `video/mp4` (.mp4), `audio/wav` (.wav); checked by extension **and** magic bytes. Max 50 MB. Duration is checked by the pipeline (3 s – 20 min).
- Stores the file under the evidence storage root (`call-recordings/{yyyy}/{MM}/{id}{ext}`), `sha256`, `transaction_ref` = the submission's matched transaction code at upload time (snapshot, may be null), `retain_until` = upload date + 1 year (BR-19).
- Same `sha256` already uploaded for **another** submission: the upload is accepted and the pipeline adds a `RECORDING_INTEGRITY` / `DUPLICATE_AUDIO` flag.

201 → `CallRecordingDto`:

```json
{
  "id": "uuid", "resultSubmissionId": "uuid", "workId": "uuid",
  "fileName": "goi-khach-1.m4a", "mimeType": "audio/mp4", "sizeBytes": 812345, "durationMs": null,
  "productCode": "TERM_DEPOSIT", "transactionRef": "GD2610001",
  "processingStatus": "PENDING", "errorCode": null,
  "uploadedAt": "2026-11-24T03:00:00+00:00", "processedAt": null,
  "flagCount": 0
}
```

Errors: 400 `CALL_RECORDING_INVALID_FILE` (type / empty / > 50 MB), 403 not the submitter, 404 submission, 409 `CALL_RECORDING_SUBMISSION_CLOSED` (withdrawn submission).

`GET /submissions/{id}/call-recordings` → `CallRecordingDto[]`, newest first. `GET /call-recordings/{id}` → `CallRecordingDto`.

## 4. Stream — `GET /call-recordings/{id}/stream`

Returns the muted audio with `Accept-Ranges: bytes`; supports `Range` (206) for seeking. 409 `CALL_RECORDING_NOT_READY` unless `READY`. The frontend fetches it as a blob with the bearer token (same pattern as evidence preview) and revokes the object URL on leave.

## 5. Transcript and trace

`GET /call-recordings/{id}/transcript` →

```json
{ "callRecordingId": "uuid", "asrModel": "large-v3-turbo-int8", "language": "vi", "piiMasked": true,
  "segments": [ { "id": 0, "startMs": 0, "endMs": 4200, "speaker": "UNKNOWN", "textMasked": "Dạ em chào anh, số tài khoản [STK] ...", "serviceCode": "SAVINGS" } ] }
```

`speaker` is always `UNKNOWN` (mono audio); the UI labels it "Chưa xác định người nói". 404 when no transcript yet.

`GET /call-recordings/{id}/trace` → work (`code`, `title`), submission (`submissionNo`, `submittedAt`), employee (`code`, `fullName`, department), `transactionRef`, `productCode`, `pipelineVersion`, `kbVersion`, `asrModel`.

## 6. AI review flags

`AiReviewFlagDto`:

```json
{
  "id": "uuid", "code": "AI-000123", "callRecordingId": "uuid", "employeeId": "uuid", "employeeCode": "NV001", "employeeName": "…",
  "departmentName": "…", "workCode": "W-…", "flagType": "FACT_MISMATCH", "ruleCode": "SAV-RATE-12M", "detector": "fact_rate_v1",
  "rankScore": 0.82, "startMs": 61200, "endMs": 66900, "evidenceText": "lãi suất 12 tháng là [SỐ] phần trăm",
  "kbRefs": [ { "chunkId": "fact/SAVINGS/rate-12m#1", "title": "Lãi suất tiết kiệm 12 tháng", "excerpt": "…", "kbVersion": "2026.11.20" } ],
  "details": { "said": "7,5%", "expected": "5,2%" },
  "status": "PENDING", "reviewedByName": null, "reviewedAt": null, "dismissReason": null, "riskEventId": null,
  "pipelineVersion": "0.3.0", "kbVersion": "2026.11.20", "createdAt": "…"
}
```

`flagType` ∈ `RECORDING_INTEGRITY | FACT_MISMATCH | MISSING_DISCLOSURE | ABSOLUTE_CLAIM` (`TYING_COERCION` is a `ruleCode` under `ABSOLUTE_CLAIM`, O4). `status` ∈ `PENDING | CONFIRMED | DISMISSED`. `evidenceText` is masked text and must be a substring of the masked segments it points to (guardrail).

- `GET /ai-review-flags?status&flagType&departmentId&periodId&employeeId&search&page&pageSize&sort` → `PagedResult<AiReviewFlagDto>`; default `status=PENDING`, `sort=rankScore desc` then `createdAt`; also returns `counts { pending, confirmed, dismissed }` like `/qa-queue`.
- `GET /ai-review-flags/{id}` → `AiReviewFlagDto` + `siblings` (other flags of the same recording, light DTO) for the workspace list.
- `POST /ai-review-flags/{id}/confirm` (body `{ "note": string? }`) → PENDING → CONFIRMED; sets reviewer/time. Does **not** create a Risk Event.
- `POST /ai-review-flags/{id}/dismiss` (body `{ "reason": string }`, 10–2000 chars) → PENDING → DISMISSED.
- Other transitions: 409 `AI_REVIEW_FLAG_INVALID_STATE`.

## 7. Risk Event linked to a recording (extends `RISK_EVENT_CONTRACT.md` §9.1)

`POST /risk-events` accepts optional `callRecordingId`, `mediaStartMs`, `mediaEndMs`, `aiReviewFlagId`.

- `callRecordingId` given ⇒ `mediaStartMs` and `mediaEndMs` are required, `0 ≤ start < end ≤ durationMs` — otherwise 400 `RISK_EVENT_MEDIA_TIMESTAMP_REQUIRED` (**MSG28**, BR-19).
- `aiReviewFlagId` given ⇒ the flag must be `CONFIRMED`, belong to that recording and to `employeeId`; the flag's `riskEventId` is set in the same save. 409 if the flag already has a Risk Event.
- Severity stays mandatory and chosen by QA; nothing is copied from the flag except recording, timestamps and the flag id.
- `RiskEventDetailsDto` gains `callRecordingId`, `mediaStartMs`, `mediaEndMs`, `aiReviewFlagId` (drawer button "Nghe đoạn audio").

## 8. Internal: backend → ai-service

`POST {AI_SERVICE_URL}/v1/analyze`, header `X-Service-Key` (from env, ≥ 16 chars), multipart `audio` + `meta` JSON `{ recordingId, productCode, workDate, duplicateOf? }`. Network: docker-internal only, no published port in production compose.

Response (snake_case, see `ai-service/app/schemas.py`):

```json
{ "pipeline_version": "0.3.0", "kb_version": "2026.11.20", "stage": "FULL",
  "asr": { "model": "large-v3-turbo-int8", "language": "vi", "duration_ms": 183000, "channels": 1 },
  "segments": [ … masked … ], "flags": [ … no severity … ],
  "muted_audio": { "mime_type": "audio/mpeg", "base64": "…" },
  "stats": { "pii_masked": 4, "guardrail_dropped": 1, "processing_ms": 41000, "source_sha256": "…" } }
```

ai-service errors (`UNSUPPORTED_MEDIA`, `AUDIO_TOO_SHORT`, `AUDIO_TOO_LONG`, `AUDIO_DECODE_FAILED`) → recording `FAILED` with that `errorCode`, no retry. 5xx / timeout → retry (≤ 3 attempts, backoff), then `FAILED` with `AI_SERVICE_UNAVAILABLE`.

## 9. Error codes (new)

| Code | HTTP | vi |
|---|---|---|
| `CALL_RECORDING_INVALID_FILE` | 400 | Tệp ghi âm không hợp lệ (mp3, m4a, mp4, wav; tối đa 50 MB). |
| `CALL_RECORDING_SUBMISSION_CLOSED` | 409 | Kết quả đã rút, không thể tải ghi âm. |
| `CALL_RECORDING_NOT_READY` | 409 | Ghi âm đang được xử lý. |
| `AI_REVIEW_FLAG_INVALID_STATE` | 409 | Trạng thái hiện tại của đoạn cần nghe lại không cho phép thao tác này. |
| `RISK_EVENT_MEDIA_TIMESTAMP_REQUIRED` | 400 | MSG28: Sự kiện gắn ghi âm phải có mốc thời gian bắt đầu và kết thúc. |
