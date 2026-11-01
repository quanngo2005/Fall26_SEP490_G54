"""Pydantic schemas and models."""

from datetime import date
from enum import Enum
from typing import Any
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator
from typing_extensions import Self


class Speaker(str, Enum):
    TELLER = "TELLER"
    CUSTOMER = "CUSTOMER"
    UNKNOWN = "UNKNOWN"


class Segment(BaseModel):
    id: int
    start_ms: int = Field(..., ge=0)
    end_ms: int
    speaker: Speaker = Speaker.UNKNOWN
    text_masked: str = ""
    service_code: str | None = None

    @model_validator(mode="after")
    def validate_duration(self) -> Self:
        if self.end_ms <= self.start_ms:
            raise ValueError("end_ms must be greater than start_ms")
        return self


class AnalyzeMeta(BaseModel):
    model_config = ConfigDict(extra="forbid")

    recording_id: UUID
    product_code: str | None = Field(default=None, max_length=32)
    work_date: date | None = None


class AsrInfo(BaseModel):
    model: str
    language: str = "vi"
    duration_ms: int
    channels: int = 1


class AnalyzeStats(BaseModel):
    pii_masked: int = 0
    guardrail_dropped: int = 0
    processing_ms: int
    source_sample_rate: int
    source_channels: int
    source_sha256: str


class AnalyzeResponse(BaseModel):
    pipeline_version: str
    stage: str = "W1_RAW"
    asr: AsrInfo
    segments: list[Segment]
    stats: AnalyzeStats


class FlagType(str, Enum):
    ABSOLUTE_CLAIM = "ABSOLUTE_CLAIM"
    TYING_COERCION = "TYING_COERCION"
    MISSING_DISCLOSURE = "MISSING_DISCLOSURE"
    FACT_MISMATCH = "FACT_MISMATCH"
    RECORDING_INTEGRITY = "RECORDING_INTEGRITY"


class ServiceSpan(BaseModel):
    service_code: str
    start_ms: int
    end_ms: int
    score: float


class Flag(BaseModel):
    flag_type: FlagType
    rule_code: str
    detector: str
    start_ms: int
    end_ms: int
    evidence_text: str
    kb_chunk_ids: list[str] = Field(default_factory=list)
    rank_score: float
    details: dict[str, Any] = Field(default_factory=dict)


class DetectRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    recording_id: UUID
    product_code: str | None = Field(default=None, max_length=32)
    work_date: date | None = None
    segments: list[Segment] = Field(..., min_length=1)


class DetectResponse(BaseModel):
    pipeline_version: str
    stage: str = "W1_STUB"
    kb_version: str | None = None
    service_spans: list[ServiceSpan] = Field(default_factory=list)
    flags: list[Flag] = Field(default_factory=list)


class HealthResponse(BaseModel):
    status: str = "ok"
    pipeline_version: str


class ErrorResponse(BaseModel):
    error_code: str
    message: str
