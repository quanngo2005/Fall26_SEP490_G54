# ADR-001: AI call triage — storage and integration choices

Date: 23/11/2026 · Status: Accepted · Context: `Project/Documents/AI_COMPLIANCE_AUDITOR_PLAN_v2.md`, `ai-plan/replan-3w.md`, contract `docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md`.

## Decisions

1. **Separate Python `ai-service`** (FastAPI, faster-whisper, ffmpeg) called by the .NET backend over the docker network with a shared `X-Service-Key`. No external AI API; models run locally (RTX 3050 4 GB / CPU fallback).
2. **One pipeline right after upload** (O1): normalize → ASR → number normalisation → PII mask → PII mute → detectors → guardrail, in one `/v1/analyze` call driven by `AudioPipelineBackgroundService` (Postgres row queue with `FOR UPDATE SKIP LOCKED`). No scan-job tables in the Must scope.
3. **Knowledge base in SQLite + `sqlite-vec` inside `ai-service`** (O2), built from the Markdown vault (`python -m app.kb.build`), versioned by `kb_version`. `bpms_db` (postgres:16-alpine) stays untouched by AI; a pgvector `ai-kb-db` is only a Should.
4. **AI flags in a new table `ai_review_flag`** (O6), not in `quality_flag` (which holds QA-created flags with a mandatory severity). AI never writes severity, penalty or contribution; the Risk Event links back through `ai_review_flag_id`.
5. **Privacy:** only the PII-muted audio and masked transcript are stored; `retain_until` = +1 year (BR-19); logs carry ids and timings only.
6. **Permissions** go to role `VERIFIER` (the QA role after the 19/11 role consolidation) and `SYSTEM_ADMIN`.

## Consequences

- Backend owns state and authorization; ai-service is stateless except the read-only KB file.
- Re-running detection after a KB change (Should) can reuse the stored masked transcript.
- ASR quality is bounded by the local GPU; the model choice is recorded per recording (`asr_model`, `pipeline_version`).
