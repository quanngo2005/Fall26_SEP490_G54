"""Tests for PII masking and PII time spans."""

import pytest

from app.pipeline.asr import TranscriptSegment, Word
from app.pipeline.pii import mask_transcript, merge_spans


def _segment(text: str, start_ms: int = 0, step_ms: int = 300) -> TranscriptSegment:
    words = tuple(
        Word(text=w, start_ms=start_ms + i * step_ms, end_ms=start_ms + i * step_ms + 250)
        for i, w in enumerate(text.split())
    )
    return TranscriptSegment(start_ms=start_ms, end_ms=words[-1].end_ms, words=words)


@pytest.mark.parametrize(
    "spoken,expected",
    [
        ("số tài khoản của chị là 190345678912345", "số tài khoản của chị là [STK]"),
        ("số tài khoản một chín không ba, bốn năm sáu bảy, tám chín.", "số tài khoản [STK]."),
        ("căn cước của anh là 001203004567", "căn cước của anh là [CCCD]"),
        ("số chứng minh 123456789", "số chứng minh [CCCD]"),
        ("đọc lại không không một hai, không ba không không, bốn năm sáu bảy", "đọc lại [CCCD]"),
        ("số anh là không chín một hai, ba bốn năm, sáu bảy tám. Dạ", "số anh là [SĐT]. Dạ"),
        ("gọi 0912 345 678 nhé", "gọi [SĐT] nhé"),
        ("căn cước của anh là 0012 -0300 -4567", "căn cước của anh là [CCCD]"),
        ("đọc lại, 0012 -0300 -4567, khoảng 7 ngày", "đọc lại, [CCCD], khoảng 7 ngày"),
        ("số anh 0912 .345 .678 nhé", "số anh [SĐT] nhé"),
        ("gọi +84912345678", "gọi [SĐT]"),
        ("mã hồ sơ 12345678", "mã hồ sơ [SỐ]"),
        ("email an.nguyen@example.com.vn nhé", "email [EMAIL] nhé"),
    ],
)
def test_masks_each_pii_type(spoken: str, expected: str) -> None:
    result = mask_transcript([_segment(spoken)], duration_ms=60_000)

    assert result.segments[0].text_masked == expected
    assert result.pii_count == 1


@pytest.mark.parametrize(
    "spoken,expected",
    [
        ("sổ năm mươi triệu kỳ hạn mười hai tháng", "sổ 50.000.000 kỳ hạn 12 tháng"),
        ("lãi năm phẩy hai phần trăm một năm", "lãi 5,2% một năm"),
        ("chuyển 50000000 đồng", "chuyển 50000000 đồng"),
        ("hạn mức 500.000.000 đồng", "hạn mức 500.000.000 đồng"),
        ("mã 1234567 thôi", "mã 1234567 thôi"),
    ],
)
def test_keeps_money_rates_and_short_numbers(spoken: str, expected: str) -> None:
    result = mask_transcript([_segment(spoken)], duration_ms=60_000)

    assert result.segments[0].text_masked == expected
    assert result.pii_count == 0
    assert result.pii_spans == []


def test_pii_span_comes_from_word_timestamps_with_padding() -> None:
    # Words at 0, 300, 600 ... ms; the digits are words 2..7 (600 ms .. 2350 ms).
    segment = _segment("số điện thoại không chín một hai ba bốn năm sáu bảy tám xong", start_ms=1_000)

    result = mask_transcript([segment], duration_ms=10_000)

    assert result.segments[0].text_masked == "số điện thoại [SĐT] xong"
    assert result.pii_spans == [(1_000 + 900 - 150, 1_000 + 3_600 + 250 + 150)]


def test_merge_spans_pads_clamps_and_merges() -> None:
    spans = [(5_000, 5_500), (100, 400), (5_700, 6_000), (9_900, 9_990)]

    assert merge_spans(spans, pad_ms=150, duration_ms=10_000) == [(0, 550), (4_850, 6_150), (9_750, 10_000)]


def test_segments_keep_ids_and_times() -> None:
    result = mask_transcript([_segment("dạ em chào chị"), _segment("vâng ạ", start_ms=2_000)], duration_ms=5_000)

    assert [(s.id, s.start_ms, s.end_ms) for s in result.segments] == [(0, 0, 1_150), (1, 2_000, 2_550)]
