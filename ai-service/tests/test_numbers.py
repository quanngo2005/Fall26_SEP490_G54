"""Tests for Vietnamese spoken-number normalisation."""

import pytest

from app.pipeline.numbers import normalize


@pytest.mark.parametrize(
    "spoken,expected",
    [
        ("năm mươi triệu", "50.000.000"),
        ("một trăm triệu, đang muốn", "100.000.000, đang muốn"),
        ("hai triệu rưỡi", "2.500.000"),
        ("hai trăm rưỡi", "250"),
        ("một triệu hai trăm nghìn", "1.200.000"),
        ("mười một nghìn đồng", "11.000 đồng"),
        ("một nghìn không trăm linh một", "1.001"),
        ("một trăm linh năm", "105"),
        ("mười lăm phút", "15 phút"),
        ("hai mươi mốt", "21"),
        ("hai mươi tư", "24"),
        ("bốn mươi lăm ngày", "45 ngày"),
        ("mười hai tháng", "12 tháng"),
        ("hai mươi năm", "20 năm"),
        ("năm năm", "5 năm"),
        ("sáu tháng", "6 tháng"),
        ("bảy phẩy năm phần trăm", "7,5%"),
        ("năm phẩy hai phần trăm một năm.", "5,2% một năm."),
        ("không phẩy năm phần trăm", "0,5%"),
        ("bốn phần trăm số tiền", "4% số tiền"),
        ("ba chấm năm", "3,5"),
        ("hai phẩy năm triệu", "2.500.000"),
        ("Một Trăm", "100"),
        ("50 triệu", "50.000.000"),
        ("2,5 triệu", "2.500.000"),
        ("7,5 phần trăm", "7,5%"),
        ("không chín một hai, ba bốn năm, sáu bảy tám.", "0912345678."),
        ("không không một hai không ba", "001203"),
        # Not numbers, or ambiguous: left as text.
        ("năm nay", "năm nay"),
        ("lãi không kỳ hạn", "lãi không kỳ hạn"),
        ("một chút", "một chút"),
        ("ngân hàng G năm tư", "ngân hàng G năm tư"),
        ("hàng trăm người", "hàng trăm người"),
        ("năm 2026", "năm 2026"),
        ("hai triệu hai", "2.000.000 hai"),
    ],
)
def test_normalize(spoken: str, expected: str) -> None:
    assert normalize(spoken).text == expected


def test_normalize_keeps_source_offsets() -> None:
    source = "Số tài khoản  một chín không ba, bốn năm sáu bảy. Lãi năm mươi triệu"
    result = normalize(source)

    assert result.text == "Số tài khoản 19034567. Lãi 50.000.000"
    digits_start = result.text.index("19034567")
    start, end = result.source_range(digits_start, digits_start + 8)
    assert source[start:end] == "một chín không ba, bốn năm sáu bảy."
    money_start = result.text.index("50.000.000")
    start, end = result.source_range(money_start, len(result.text))
    assert source[start:end] == "năm mươi triệu"
