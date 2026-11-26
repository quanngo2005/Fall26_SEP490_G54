"""Vietnamese spoken-number normalisation that keeps source character offsets."""

from dataclasses import dataclass
from decimal import Decimal
import re

DIGIT_WORDS: dict[str, int] = {
    "không": 0,
    "một": 1,
    "hai": 2,
    "ba": 3,
    "bốn": 4,
    "năm": 5,
    "sáu": 6,
    "bảy": 7,
    "bẩy": 7,
    "tám": 8,
    "chín": 9,
}
# Unit-digit variants that only appear after "mười"/"mươi" ("hai mươi mốt", "mười lăm").
_UNIT_VARIANTS: dict[str, int] = {"mốt": 1, "tư": 4, "lăm": 5, "nhăm": 5}
_SCALES: dict[str, int] = {"nghìn": 1_000, "ngàn": 1_000, "triệu": 1_000_000, "tỷ": 1_000_000_000, "tỉ": 1_000_000_000}
_DECIMAL_POINTS = frozenset({"phẩy", "chấm"})
_ZERO_TENS = frozenset({"linh", "lẻ"})
# A lone digit word is a number only before one of these ("sáu tháng", "năm năm"), so "năm nay" stays text.
_UNIT_NOUNS = frozenset({"tháng", "năm", "ngày", "tuần", "tuổi", "lần", "phút", "giờ"})
_MIN_DIGIT_SEQUENCE = 4
_TRAILING_PUNCT = ".,;:!?…\"')"
# Digits Whisper already wrote: "50", "7,5", "7.5" (thousands-dotted amounts such as "50.000.000" stay as they are).
_NUMERIC_RE = re.compile(r"\d+(?:,\d+|\.\d{1,2})?")


@dataclass(frozen=True)
class Piece:
    """One output token and the source character range it came from."""

    text: str
    start: int
    source_start: int
    source_end: int

    @property
    def end(self) -> int:
        return self.start + len(self.text)


@dataclass(frozen=True)
class NormalizedText:
    text: str
    pieces: tuple[Piece, ...]

    def source_range(self, start: int, end: int) -> tuple[int, int]:
        """Map a character range of the normalised text back to the source text."""
        hits = [p for p in self.pieces if p.start < end and start < p.end]
        if not hits:
            raise ValueError("range does not overlap the normalised text")
        return hits[0].source_start, hits[-1].source_end


@dataclass(frozen=True)
class _Token:
    core: str
    punct: str
    start: int
    end: int


def normalize(text: str) -> NormalizedText:
    """Rewrite spoken numbers as digits: "năm mươi triệu" -> "50.000.000", "bảy phẩy năm phần trăm" -> "7,5%".

    Tokens are re-joined with single spaces; every output piece keeps the source range it replaced.
    """
    tokens = [_split_token(m.group(), m.start()) for m in re.finditer(r"\S+", text)]
    pieces: list[Piece] = []
    pos = 0
    i = 0
    while i < len(tokens):
        match = _match_digit_sequence(tokens, i) or _match_number(tokens, i)
        if match is None:
            out, end_index = text[tokens[i].start : tokens[i].end], i + 1
        else:
            out, end_index = match
        first, last = tokens[i], tokens[end_index - 1]
        if pieces:
            pos += 1
        pieces.append(Piece(text=out, start=pos, source_start=first.start, source_end=last.end))
        pos += len(out)
        i = end_index
    return NormalizedText(text=" ".join(p.text for p in pieces), pieces=tuple(pieces))


def _split_token(raw: str, start: int) -> _Token:
    core = raw.rstrip(_TRAILING_PUNCT)
    return _Token(core=core.lower(), punct=raw[len(core) :], start=start, end=start + len(raw))


def _match_digit_sequence(tokens: list[_Token], i: int) -> tuple[str, int] | None:
    """Digit-by-digit speech ("không chín một hai, ba bốn năm") -> "0912345"; commas between groups are allowed."""
    digits: list[str] = []
    j = i
    while j < len(tokens) and tokens[j].core in DIGIT_WORDS:
        digits.append(str(DIGIT_WORDS[tokens[j].core]))
        j += 1
        if tokens[j - 1].punct not in ("", ","):
            break
    if len(digits) < _MIN_DIGIT_SEQUENCE:
        return None
    return "".join(digits) + tokens[j - 1].punct, j


def _match_number(tokens: list[_Token], i: int) -> tuple[str, int] | None:
    parsed = _parse_cardinal(tokens, i)
    if parsed is None:
        return None
    value, j, scale = parsed
    single_token = j == i + 1

    if j + 1 < len(tokens) and tokens[j - 1].punct == "" and tokens[j].core in _DECIMAL_POINTS:
        fraction = _read_digits(tokens, j + 1)
        if fraction:
            value += Decimal(f"0.{fraction}")
            j += 1 + len(fraction)
            single_token = False
            if scale == 1 and j < len(tokens) and tokens[j - 1].punct == "" and tokens[j].core in _SCALES:
                value *= _SCALES[tokens[j].core]
                j += 1

    percent = (
        j + 1 < len(tokens)
        and tokens[j - 1].punct == ""
        and tokens[j].core == "phần"
        and tokens[j + 1].core == "trăm"
    )
    if percent:
        j += 2
    elif single_token and not _is_standalone_number(tokens, i):
        return None

    return _format(value) + ("%" if percent else "") + tokens[j - 1].punct, j


def _is_standalone_number(tokens: list[_Token], i: int) -> bool:
    word = tokens[i].core
    if word == "mười":
        return True
    next_word = tokens[i + 1].core if i + 1 < len(tokens) and tokens[i].punct == "" else None
    return word in DIGIT_WORDS and DIGIT_WORDS[word] >= 2 and next_word in _UNIT_NOUNS


def _read_digits(tokens: list[_Token], i: int) -> str:
    digits = ""
    j = i
    while j < len(tokens) and tokens[j].core in DIGIT_WORDS:
        digits += str(DIGIT_WORDS[tokens[j].core])
        j += 1
        if tokens[j - 1].punct:
            break
    return digits


def _parse_cardinal(tokens: list[_Token], i: int) -> tuple[Decimal, int, int] | None:
    """Parse a cardinal number starting at `i`.

    Returns (value, index after the last consumed token, largest scale used), or None when `i` does not start a number.
    A numeric token ("50", "2,5") only combines with a following scale or "phần trăm".
    """
    total = Decimal(0)
    group = Decimal(0)
    pending: Decimal | None = None
    last_scale = 1
    prev = ""
    best: tuple[Decimal, int, int] | None = None
    j = i
    while j < len(tokens):
        word = tokens[j].core
        if prev == "" and _NUMERIC_RE.fullmatch(word):
            pending, prev = Decimal(word.replace(",", ".")), "numeric"
        elif prev == "numeric" and word not in _SCALES:
            break
        elif word in DIGIT_WORDS and prev in ("", "scale", "trăm"):
            pending, prev = Decimal(DIGIT_WORDS[word]), "digit"
        elif _is_unit_digit(word, prev):
            group += DIGIT_WORDS[word] if word in DIGIT_WORDS else _UNIT_VARIANTS[word]
            prev = "unit"
        elif word == "mười" and prev in ("", "scale", "trăm"):
            group += 10
            prev = "mười"
        elif word == "mươi" and prev == "digit" and pending is not None and pending >= 2:
            group += pending * 10
            pending, prev = None, "mươi"
        elif word == "trăm" and prev == "digit" and pending is not None:
            group += pending * 100
            pending, prev = None, "trăm"
        elif word in _ZERO_TENS and prev in ("trăm", "scale"):
            prev = "linh"
        elif word in _SCALES and prev in ("digit", "numeric", "unit", "mười", "mươi", "trăm"):
            scale = _SCALES[word]
            value = group + (pending or 0)
            total = (total + value) * scale if scale > last_scale else total + value * scale
            group, pending, last_scale, prev = Decimal(0), None, max(last_scale, scale), "scale"
        elif word == "rưỡi" and prev in ("scale", "trăm"):
            total += Decimal(last_scale) / 2 if prev == "scale" else Decimal(50)
            best = (total + group, j + 1, last_scale)
            break
        else:
            break
        j += 1
        # A digit right after a scale or "trăm" ("hai triệu hai") is colloquial shorthand: not a complete number.
        complete = prev != "linh" and not (prev == "digit" and j - 1 > i)
        if complete:
            best = (total + group + (pending or 0), j, last_scale)
        if tokens[j - 1].punct:
            break
    return best


def _is_unit_digit(word: str, prev: str) -> bool:
    """Units digit after "mười"/"mươi"/"linh". After tens, five is "lăm" ("hai mươi năm" is 20 years, not 25)."""
    if prev == "linh":
        return word in DIGIT_WORDS and word != "không"
    if prev == "mười":
        return (word in DIGIT_WORDS and word not in ("không", "năm")) or word in ("lăm", "nhăm")
    if prev == "mươi":
        return (word in DIGIT_WORDS and word not in ("không", "năm")) or word in _UNIT_VARIANTS
    return False


def _format(value: Decimal) -> str:
    """Vietnamese format: thousands with ".", decimals with ","."""
    integer = int(value)
    fraction = value - integer
    text = f"{integer:,}".replace(",", ".")
    if fraction:
        text += "," + format(fraction.normalize(), "f").split(".")[1]
    return text
