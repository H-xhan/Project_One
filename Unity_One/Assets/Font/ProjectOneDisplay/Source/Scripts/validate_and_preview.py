#!/usr/bin/env python3
"""Validate Project One Display builds and render deterministic comparison previews."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import random
import subprocess
from collections import Counter, deque
from pathlib import Path

from fontTools.pens.areaPen import AreaPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont


COLORS = {
    "cream": "#F6EEDB",
    "dark": "#18213E",
    "navy": "#1D2B5D",
    "blue": "#58B7F2",
    "yellow": "#FFD94D",
    "mint": "#68D9BD",
    "white": "#FFFDF5",
    "ink": "#29314F",
    "shadow": "#142044",
}

MANDATORY_LINES = [
    "PROJECT ONE",
    "READY",
    "COUNTDOWN",
    "PLAY",
    "RESULT",
    "SETTINGS",
    "EXIT",
    "프로젝트 원",
    "작은 발, 큰 모험",
    "게임 시작",
    "방 만들기",
    "방 참가",
    "준비",
    "준비 완료",
    "남은 시간",
    "포스트잇",
    "결과 확인",
    "승리",
    "무승부",
    "관전",
    "자유 이동",
    "플레이어 전환",
    "설정",
    "다시하기",
    "나가기",
    "홈으로",
    "연결 중",
    "접속 실패",
    "공격",
    "점프",
    "달리기",
    "잡기",
    "던지기",
    "벽타기",
]

KOREAN_UI_LINES = [
    "프로젝트 원",
    "작은 발, 큰 모험",
    "게임 시작",
    "준비 완료",
    "포스트잇",
    "결과 확인",
    "관전",
    "설정",
    "나가기",
]

PRIORITY_HANGUL = (
    "가 각 간 값 강 겹 공 나 난 날 다 달 마 막 모 몸 바 발 빵 사 상 "
    "아 안 앙 자 잡 차 카 타 파 하 준 비 게 임 포 스 트 잇 결 과 관 전 설 정 던 지 기 벽 타 기"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def get_name(font: TTFont, name_id: int) -> str:
    for record in font["name"].names:
        if record.nameID != name_id:
            continue
        try:
            return record.toUnicode()
        except UnicodeDecodeError:
            continue
    return ""


def inspect_font(path: Path, required_characters: str) -> dict[str, object]:
    report: dict[str, object] = {
        "path": str(path),
        "sha256": sha256(path),
        "size_bytes": path.stat().st_size,
        "open": False,
        "errors": [],
        "warnings": [],
    }
    try:
        font = TTFont(path, recalcBBoxes=False, recalcTimestamp=False)
    except Exception as error:  # noqa: BLE001 - validation report requires exact parser failure
        report["errors"].append(f"open_failed:{type(error).__name__}:{error}")
        return report

    report["open"] = True
    tables = set(font.keys())
    required_tables = {"head", "hhea", "maxp", "OS/2", "name", "cmap", "hmtx", "post"}
    missing_tables = sorted(required_tables - tables)
    if missing_tables:
        report["errors"].append(f"missing_tables:{','.join(missing_tables)}")

    family = get_name(font, 1)
    style = get_name(font, 2)
    postscript_name = get_name(font, 6)
    version = get_name(font, 5)
    report["names"] = {
        "family": family,
        "style": style,
        "postscript": postscript_name,
        "version": version,
    }

    unicode_maps: dict[int, set[str]] = {}
    for table in font["cmap"].tables:
        if not table.isUnicode():
            continue
        for codepoint, glyph_name in table.cmap.items():
            unicode_maps.setdefault(codepoint, set()).add(glyph_name)
    conflicting_unicode = {
        f"U+{codepoint:04X}": sorted(names)
        for codepoint, names in unicode_maps.items()
        if len(names) > 1
    }
    if conflicting_unicode:
        report["errors"].append(f"conflicting_unicode:{len(conflicting_unicode)}")
    report["duplicate_unicode_conflicts"] = conflicting_unicode

    best_cmap = font.getBestCmap() or {}
    required_codepoints = sorted({ord(character) for character in required_characters if character not in "\r\n"})
    missing_codepoints = [codepoint for codepoint in required_codepoints if codepoint not in best_cmap]
    if missing_codepoints:
        report["errors"].append(f"missing_required:{len(missing_codepoints)}")
    report["missing_required"] = [f"U+{codepoint:04X}" for codepoint in missing_codepoints]

    glyph_set = font.getGlyphSet()
    empty_required: list[str] = []
    invalid_bounds: list[str] = []
    clipped: list[dict[str, object]] = []
    extreme_bearings: list[dict[str, object]] = []
    zero_advance: list[str] = []
    signed_area_counts = Counter()
    ascender = font["hhea"].ascent
    descender = font["hhea"].descent
    metrics = font["hmtx"].metrics

    for codepoint in required_codepoints:
        glyph_name = best_cmap.get(codepoint)
        if not glyph_name:
            continue
        glyph = glyph_set[glyph_name]
        bounds_pen = BoundsPen(glyph_set)
        try:
            glyph.draw(bounds_pen)
        except Exception as error:  # noqa: BLE001
            invalid_bounds.append(f"{glyph_name}:{type(error).__name__}:{error}")
            continue
        bounds = bounds_pen.bounds
        character = chr(codepoint)
        if bounds is None:
            if character not in {" ", "\u00A0"}:
                empty_required.append(f"U+{codepoint:04X}:{glyph_name}")
            continue
        if not all(math.isfinite(value) for value in bounds):
            invalid_bounds.append(f"{glyph_name}:non_finite:{bounds}")
            continue
        x_min, y_min, x_max, y_max = bounds
        if y_max > ascender + 1 or y_min < descender - 1:
            clipped.append(
                {
                    "glyph": glyph_name,
                    "codepoint": f"U+{codepoint:04X}",
                    "bounds": [x_min, y_min, x_max, y_max],
                }
            )
        advance, left_bearing = metrics.get(glyph_name, (0, 0))
        right_bearing = advance - x_max
        if advance == 0 and character != "\u200b":
            zero_advance.append(f"U+{codepoint:04X}:{glyph_name}")
        if left_bearing < -100 or right_bearing < -100 or advance > 1400:
            extreme_bearings.append(
                {
                    "glyph": glyph_name,
                    "codepoint": f"U+{codepoint:04X}",
                    "advance": advance,
                    "left": left_bearing,
                    "right": right_bearing,
                }
            )
        area_pen = AreaPen(glyph_set)
        try:
            glyph.draw(area_pen)
            signed_area_counts["positive" if area_pen.value > 0 else "negative" if area_pen.value < 0 else "zero"] += 1
        except Exception:  # noqa: BLE001
            signed_area_counts["failed"] += 1

    if empty_required:
        report["errors"].append(f"empty_required:{len(empty_required)}")
    if invalid_bounds:
        report["errors"].append(f"invalid_bounds:{len(invalid_bounds)}")
    if clipped:
        report["errors"].append(f"metric_clipping:{len(clipped)}")
    if zero_advance:
        report["errors"].append(f"zero_advance:{len(zero_advance)}")
    if extreme_bearings:
        report["warnings"].append(f"extreme_bearings:{len(extreme_bearings)}")

    report["glyph_count"] = len(font.getGlyphOrder())
    report["unicode_count"] = len(best_cmap)
    report["empty_required"] = empty_required
    report["invalid_bounds"] = invalid_bounds
    report["metric_clipping"] = clipped
    report["zero_advance"] = zero_advance
    report["extreme_bearings"] = extreme_bearings
    report["contour_signed_area"] = dict(signed_area_counts)
    report["metrics"] = {
        "units_per_em": font["head"].unitsPerEm,
        "hhea_ascent": ascender,
        "hhea_descent": descender,
        "line_gap": font["hhea"].lineGap,
        "os2_typo_ascender": font["OS/2"].sTypoAscender,
        "os2_typo_descender": font["OS/2"].sTypoDescender,
        "weight_class": font["OS/2"].usWeightClass,
        "width_class": font["OS/2"].usWidthClass,
    }
    font.close()
    return report


def run_external_validator(command: list[str], log_path: Path) -> dict[str, object]:
    try:
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=600,
        )
        output = completed.stdout + "\n" + completed.stderr
        log_path.write_text(
            "COMMAND\n" + subprocess.list2cmdline(command) + "\n\n" + output,
            encoding="utf-8",
            newline="\n",
        )
        return {
            "available": True,
            "return_code": completed.returncode,
            "log": str(log_path),
            "tail": output[-3000:],
        }
    except (OSError, subprocess.TimeoutExpired) as error:
        log_path.write_text(str(error), encoding="utf-8", newline="\n")
        return {
            "available": False,
            "return_code": None,
            "log": str(log_path),
            "error": f"{type(error).__name__}:{error}",
        }


def checker_background(size: tuple[int, int]) -> Image.Image:
    image = Image.new("RGB", size, "#E9E6E0")
    draw = ImageDraw.Draw(image)
    tile = max(24, min(size) // 28)
    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle((x, y, x + tile, y + tile), fill="#D5D1CA")
    return image


def make_background(size: tuple[int, int], mode: str) -> Image.Image:
    if mode == "checker":
        return checker_background(size)
    return Image.new("RGB", size, COLORS[mode])


def font_for(path: Path, size: int):
    return ImageFont.truetype(str(path), size=size)


def text_size(draw: ImageDraw.ImageDraw, text: str, font) -> tuple[int, int, tuple[int, int, int, int]]:
    bounds = draw.textbbox((0, 0), text, font=font)
    return bounds[2] - bounds[0], bounds[3] - bounds[1], bounds


def draw_centered(
    draw: ImageDraw.ImageDraw,
    canvas_width: int,
    y: float,
    text: str,
    font,
    fill: str,
    shadow: bool = True,
    shadow_scale: float = 0.07,
):
    width, _, bounds = text_size(draw, text, font)
    x = (canvas_width - width) / 2.0 - bounds[0]
    if shadow:
        offset = max(2, int(getattr(font, "size", 30) * shadow_scale))
        draw.text((x + offset, y + offset), text, font=font, fill=COLORS["shadow"])
    draw.text((x, y), text, font=font, fill=fill)


def render_reference(path: Path, font_path: Path, size: tuple[int, int], background: str, variant: str):
    image = make_background(size, background)
    draw = ImageDraw.Draw(image)
    foreground = COLORS["white"] if background == "dark" else COLORS["navy"]
    scale = size[1] / 1080.0
    small = font_for(font_path, int(34 * scale))
    project_font = font_for(font_path, int(170 * scale))
    one_font = font_for(font_path, int(220 * scale))
    draw.text((50 * scale, 35 * scale), f"{variant} · ORIGINAL DESIGN · REFERENCE MOOD, NO TRACE", font=small, fill=foreground)
    draw_centered(draw, size[0], 230 * scale, "PROJECT", project_font, COLORS["navy"] if background != "dark" else COLORS["white"])
    word = "ONE"
    colors = [COLORS["blue"], COLORS["yellow"], COLORS["mint"]]
    widths = [text_size(draw, character, one_font)[0] for character in word]
    total = sum(widths) + int(22 * scale) * 2
    x = (size[0] - total) / 2
    for character, color, width in zip(word, colors, widths):
        offset = max(3, int(14 * scale))
        draw.text((x + offset, 520 * scale + offset), character, font=one_font, fill=COLORS["shadow"])
        draw.text((x, 520 * scale), character, font=one_font, fill=color)
        x += width + int(22 * scale)
    footer = font_for(font_path, int(42 * scale))
    draw_centered(draw, size[0], 865 * scale, "PROJECT ONE DISPLAY · BOLD · v0.100", footer, foreground, shadow=False)
    image.save(path)


def render_korean_ui(path: Path, font_path: Path, size: tuple[int, int], background: str, variant: str):
    image = make_background(size, background)
    draw = ImageDraw.Draw(image)
    scale = size[1] / 1080.0
    title_font = font_for(font_path, int(76 * scale))
    body_font = font_for(font_path, int(48 * scale))
    color = COLORS["white"] if background == "dark" else COLORS["navy"]
    draw_centered(draw, size[0], 65 * scale, f"{variant} · KOREAN UI", title_font, color)
    columns = [KOREAN_UI_LINES[:5], KOREAN_UI_LINES[5:]]
    x_origins = [size[0] * 0.13, size[0] * 0.57]
    for column, x in zip(columns, x_origins):
        for index, line in enumerate(column):
            y = (245 + index * 145) * scale
            draw.rounded_rectangle(
                (x, y - 20 * scale, x + size[0] * 0.30, y + 85 * scale),
                radius=24 * scale,
                fill="#FFF8E9" if background != "dark" else "#24325C",
            )
            draw.text((x + 35 * scale, y), line, font=body_font, fill=color)
    image.save(path)


def render_sizes(path: Path, font_path: Path, size: tuple[int, int], background: str, variant: str):
    image = make_background(size, background)
    draw = ImageDraw.Draw(image)
    scale = size[1] / 1080.0
    color = COLORS["white"] if background == "dark" else COLORS["navy"]
    header = font_for(font_path, int(56 * scale))
    draw.text((55 * scale, 35 * scale), f"{variant} · 18—144 PX", font=header, fill=color)
    y = 135 * scale
    for point_size in [18, 24, 32, 48, 72, 96, 144]:
        preview_size = max(12, int(point_size * scale))
        sample_font = font_for(font_path, preview_size)
        line = f"{point_size:>3}px  PROJECT ONE · 준비 완료 · 포스트잇"
        draw.text((70 * scale, y), line, font=sample_font, fill=color)
        y += (point_size + 22) * scale
    image.save(path)


def render_contact(path: Path, font_path: Path, size: tuple[int, int], background: str, variant: str):
    image = make_background(size, background)
    draw = ImageDraw.Draw(image)
    scale = size[1] / 1080.0
    color = COLORS["white"] if background == "dark" else COLORS["ink"]
    header = font_for(font_path, int(52 * scale))
    glyph_font = font_for(font_path, int(54 * scale))
    draw.text((55 * scale, 35 * scale), f"{variant} · LATIN / DIGIT / SYMBOL", font=header, fill=color)
    lines = [
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "abcdefghijklmnopqrstuvwxyz",
        "0123456789  ! ? % + - × / : ;",
        "( ) [ ] { } < >  ← ↑ → ↓",
        "·  #  &  @  $  =  _  ^  ~",
    ]
    for index, line in enumerate(lines):
        draw.text((75 * scale, (170 + index * 150) * scale), line, font=glyph_font, fill=color)
    image.save(path)


def split_rows(values: list[str], columns: int) -> list[list[str]]:
    return [values[index : index + columns] for index in range(0, len(values), columns)]


def render_hangul_stress(
    path: Path,
    font_path: Path,
    size: tuple[int, int],
    background: str,
    variant: str,
    available_hangul: list[str],
):
    image = make_background(size, background)
    draw = ImageDraw.Draw(image)
    scale = size[1] / 1080.0
    color = COLORS["white"] if background == "dark" else COLORS["navy"]
    header = font_for(font_path, int(50 * scale))
    priority_font = font_for(font_path, int(38 * scale))
    stress_font = font_for(font_path, int(39 * scale))
    draw.text((50 * scale, 30 * scale), f"{variant} · HANGUL PROJECT SUBSET STRESS", font=header, fill=color)
    draw.multiline_text((55 * scale, 130 * scale), PRIORITY_HANGUL, font=priority_font, fill=color, spacing=18 * scale)
    randomizer = random.Random(20260729)
    sample = sorted(randomizer.sample(available_hangul, min(100, len(available_hangul))), key=ord)
    columns = 20
    rows = split_rows(sample, columns)
    start_y = 410 * scale
    cell_width = (size[0] - 110 * scale) / columns
    cell_height = 102 * scale
    for row_index, row in enumerate(rows):
        for column_index, character in enumerate(row):
            x = 55 * scale + column_index * cell_width
            y = start_y + row_index * cell_height
            draw.rounded_rectangle(
                (x, y, x + cell_width - 6 * scale, y + cell_height - 8 * scale),
                radius=8 * scale,
                outline="#B9B097" if background != "dark" else "#53638F",
                width=max(1, int(2 * scale)),
            )
            draw.text((x + cell_width * 0.22, y + 10 * scale), character, font=stress_font, fill=color)
    image.save(path)


def render_ui_mockup(path: Path, font_path: Path, size: tuple[int, int], background: str, variant: str):
    image = make_background(size, background)
    draw = ImageDraw.Draw(image)
    scale = size[1] / 1080.0
    title = font_for(font_path, int(118 * scale))
    button = font_for(font_path, int(49 * scale))
    hud = font_for(font_path, int(72 * scale))
    small = font_for(font_path, int(34 * scale))
    draw.text((55 * scale, 35 * scale), f"{variant} · REAL UI MOCKUP", font=small, fill=COLORS["navy"])
    draw_centered(draw, size[0], 125 * scale, "PROJECT ONE", title, COLORS["navy"])
    button_specs = [
        ("게임 시작", COLORS["blue"]),
        ("방 만들기", COLORS["yellow"]),
        ("설정", COLORS["mint"]),
    ]
    for index, (label, fill) in enumerate(button_specs):
        x0 = size[0] * 0.08
        y0 = (380 + index * 150) * scale
        x1 = size[0] * 0.40
        y1 = y0 + 108 * scale
        draw.rounded_rectangle((x0 + 8 * scale, y0 + 10 * scale, x1 + 8 * scale, y1 + 10 * scale), radius=28 * scale, fill=COLORS["shadow"])
        draw.rounded_rectangle((x0, y0, x1, y1), radius=28 * scale, fill=fill)
        text_width, text_height, bounds = text_size(draw, label, button)
        draw.text(((x0 + x1 - text_width) / 2 - bounds[0], (y0 + y1 - text_height) / 2 - bounds[1]), label, font=button, fill=COLORS["navy"])
    cards = [
        ("READY", COLORS["blue"]),
        ("3", COLORS["yellow"]),
        ("RESULT", COLORS["mint"]),
        ("튜토리얼", COLORS["white"]),
    ]
    for index, (label, fill) in enumerate(cards):
        x0 = size[0] * 0.53
        y0 = (340 + index * 155) * scale
        x1 = size[0] * 0.92
        y1 = y0 + 112 * scale
        draw.rounded_rectangle((x0, y0, x1, y1), radius=20 * scale, fill="#EFE2C5", outline=COLORS["navy"], width=max(2, int(4 * scale)))
        draw_centered(draw, int(x0 + x1), y0 + 8 * scale, label, hud if index != 3 else button, fill, shadow=False)
    image.save(path)


def enclosed_counter_count(font_path: Path, text: str = "PROB8O0C", size: int = 24) -> dict[str, object]:
    font = font_for(font_path, size)
    canvas = Image.new("L", (600, 100), 255)
    draw = ImageDraw.Draw(canvas)
    draw.text((4, 4), text, font=font, fill=0)
    bounds = canvas.getbbox()
    pixels = canvas.load()
    dark_points = [(x, y) for y in range(canvas.height) for x in range(canvas.width) if pixels[x, y] < 128]
    if not dark_points:
        return {"holes": 0, "ink_ratio": 0.0, "bbox": None}
    x_min = max(0, min(x for x, _ in dark_points) - 1)
    x_max = min(canvas.width - 1, max(x for x, _ in dark_points) + 1)
    y_min = max(0, min(y for _, y in dark_points) - 1)
    y_max = min(canvas.height - 1, max(y for _, y in dark_points) + 1)
    visited: set[tuple[int, int]] = set()
    holes = 0
    white_count = 0
    area = max(1, (x_max - x_min + 1) * (y_max - y_min + 1))
    for y in range(y_min, y_max + 1):
        for x in range(x_min, x_max + 1):
            if pixels[x, y] < 128:
                continue
            white_count += 1
            if (x, y) in visited:
                continue
            queue = deque([(x, y)])
            visited.add((x, y))
            component: list[tuple[int, int]] = []
            touches_edge = False
            while queue:
                current_x, current_y = queue.popleft()
                component.append((current_x, current_y))
                if current_x in (x_min, x_max) or current_y in (y_min, y_max):
                    touches_edge = True
                for offset_x, offset_y in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    next_point = (current_x + offset_x, current_y + offset_y)
                    if not (x_min <= next_point[0] <= x_max and y_min <= next_point[1] <= y_max):
                        continue
                    if next_point in visited or pixels[next_point[0], next_point[1]] < 128:
                        continue
                    visited.add(next_point)
                    queue.append(next_point)
            if not touches_edge and len(component) >= 2:
                holes += 1
    ink_ratio = 1.0 - white_count / area
    return {"holes": holes, "ink_ratio": round(ink_ratio, 4), "bbox": [x_min, y_min, x_max, y_max]}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lab", type=Path, required=True)
    parser.add_argument("--characters", type=Path, required=True)
    parser.add_argument("--ots", type=Path)
    parser.add_argument("--fontbakery", type=Path)
    parser.add_argument("--final", action="store_true")
    args = parser.parse_args()

    lab = args.lab.resolve()
    required_characters = args.characters.read_text(encoding="utf-8")
    reports_directory = lab / "reports"
    previews_directory = lab / "previews"
    reports_directory.mkdir(parents=True, exist_ok=True)
    previews_directory.mkdir(parents=True, exist_ok=True)

    validation: dict[str, object] = {}
    preview_manifest: list[dict[str, object]] = []
    variant_ttf: dict[str, Path] = {}

    variant_labels = ("FINAL",) if args.final else ("A_SAFE", "B_REFERENCE", "C_CUTE")
    for variant in variant_labels:
        build_directory = lab / "build" / variant
        ttf_candidates = sorted(build_directory.glob("*.ttf"))
        otf_candidates = sorted(build_directory.glob("*.otf"))
        if len(ttf_candidates) != 1 or len(otf_candidates) != 1:
            raise RuntimeError(f"{variant} requires exactly one TTF and OTF, found {ttf_candidates} / {otf_candidates}")
        ttf = ttf_candidates[0]
        variant_ttf[variant] = ttf
        files_report = []
        for font_path in [ttf, otf_candidates[0]]:
            font_report = inspect_font(font_path, required_characters)
            validator_label = font_path.suffix[1:].lower()
            if args.ots:
                ots_log = reports_directory / f"{variant}_{validator_label}_ots.log"
                font_report["ots"] = run_external_validator([str(args.ots), str(font_path)], ots_log)
            files_report.append(font_report)
        validation[variant] = {
            "fonts": files_report,
            "counter_24px": enclosed_counter_count(ttf),
        }

        with TTFont(ttf) as font:
            cmap = font.getBestCmap() or {}
            available_hangul = [chr(codepoint) for codepoint in cmap if 0xAC00 <= codepoint <= 0xD7A3]
        renderers = [
            ("Reference", render_reference),
            ("KoreanUI", render_korean_ui),
            ("Sizes", render_sizes),
            ("Contact", render_contact),
            ("HangulStress", render_hangul_stress),
            ("RealUIMockup", render_ui_mockup),
        ]
        for width, height in [(1920, 1080), (2560, 1440)]:
            backgrounds = ["cream", "dark", "checker", "cream", "dark", "checker"]
            for (label, renderer), background in zip(renderers, backgrounds):
                output = previews_directory / f"{variant}_{label}_{width}x{height}.png"
                if label == "HangulStress":
                    renderer(output, ttf, (width, height), background, variant, available_hangul)
                else:
                    renderer(output, ttf, (width, height), background, variant)
                preview_manifest.append(
                    {
                        "variant": variant,
                        "type": label,
                        "resolution": f"{width}x{height}",
                        "background": background,
                        "path": str(output),
                        "sha256": sha256(output),
                    }
                )

        if args.fontbakery:
            bakery_log = reports_directory / f"{variant}_fontbakery.log"
            validation[variant]["fontbakery"] = run_external_validator(
                [str(args.fontbakery), "check-universal", str(ttf), "--loglevel", "WARN"],
                bakery_log,
            )

    validation_path = reports_directory / ("final_validation.json" if args.final else "variant_validation.json")
    validation_path.write_text(json.dumps(validation, ensure_ascii=False, indent=2), encoding="utf-8", newline="\n")
    preview_manifest_path = reports_directory / ("final_preview_manifest.json" if args.final else "preview_manifest.json")
    preview_manifest_path.write_text(json.dumps(preview_manifest, ensure_ascii=False, indent=2), encoding="utf-8", newline="\n")

    rows = []
    for variant in variant_labels:
        counter = validation[variant]["counter_24px"]
        critical_errors = sum(len(font["errors"]) for font in validation[variant]["fonts"])
        rows.append(
            f"<tr><td>{variant}</td><td>{critical_errors}</td><td>{counter['holes']}</td>"
            f"<td>{counter['ink_ratio']}</td><td>{html.escape(str(variant_ttf[variant]))}</td></tr>"
        )
    images = "\n".join(
        f"<figure><img src='../previews/{Path(item['path']).name}'><figcaption>{item['variant']} · {item['type']} · {item['resolution']}</figcaption></figure>"
        for item in preview_manifest
        if item["resolution"] == "1920x1080"
    )
    html_report = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><title>Project One Display Variant Review</title>
<style>body{{font-family:Arial,sans-serif;background:#f4eedf;color:#1d2b5d;margin:24px}}
table{{border-collapse:collapse;background:white}}td,th{{border:1px solid #aaa;padding:8px}}
.grid{{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}}
figure{{margin:0;background:white;padding:10px}}img{{width:100%;height:auto}}figcaption{{padding:8px}}</style></head>
<body><h1>Project One Display Variant Review</h1>
<p>Original reference-inspired design. No logo outline or commercial font outline was traced.</p>
<table><thead><tr><th>Variant</th><th>Critical validation errors</th><th>24px enclosed counters</th><th>24px ink ratio</th><th>TTF</th></tr></thead>
<tbody>{''.join(rows)}</tbody></table><div class="grid">{images}</div></body></html>"""
    html_path = reports_directory / ("final_validation_report.html" if args.final else "variant_selection_report.html")
    html_path.write_text(html_report, encoding="utf-8", newline="\n")

    print(
        json.dumps(
            {
                "validation": str(validation_path),
                "preview_manifest": str(preview_manifest_path),
                "html": str(html_path),
                "variants": {
                    variant: {
                        "critical_errors": sum(len(font["errors"]) for font in validation[variant]["fonts"]),
                        "counter_24px": validation[variant]["counter_24px"],
                    }
                    for variant in validation
                },
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
