#!/usr/bin/env python3
"""Build original Project One Display UFO sources and compiled fonts."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from fontTools.agl import UV2AGL
from shapely import affinity
from shapely.geometry import GeometryCollection, LineString, MultiPolygon, Point, Polygon, box
from shapely.geometry.polygon import orient
from shapely.ops import unary_union
from ufoLib2 import Font


@dataclass(frozen=True)
class DesignTokens:
    key: str
    stroke: float
    corner_radius: float
    width_factor: float
    irregularity: float


VARIANTS = {
    "A_SAFE": DesignTokens("A_SAFE", 155.0, 80.0, 0.96, 4.0),
    "B_REFERENCE": DesignTokens("B_REFERENCE", 170.0, 96.0, 1.00, 6.0),
    "C_CUTE": DesignTokens("C_CUTE", 182.0, 110.0, 1.05, 8.0),
}

UPM = 1000
ASCENDER = 850
DESCENDER = -270
LINE_GAP = 100
CAP_HEIGHT = 720
X_HEIGHT = 520

L_COMPAT = list("ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ")
V_COMPAT = list("ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ")
T_COMPAT = [
    None,
    "ㄱ",
    "ㄲ",
    "ㄳ",
    "ㄴ",
    "ㄵ",
    "ㄶ",
    "ㄷ",
    "ㄹ",
    "ㄺ",
    "ㄻ",
    "ㄼ",
    "ㄽ",
    "ㄾ",
    "ㄿ",
    "ㅀ",
    "ㅁ",
    "ㅂ",
    "ㅄ",
    "ㅅ",
    "ㅆ",
    "ㅇ",
    "ㅈ",
    "ㅊ",
    "ㅋ",
    "ㅌ",
    "ㅍ",
    "ㅎ",
]

COMPATIBILITY_JAMO = (
    "ㄱㄲㄳㄴㄵㄶㄷㄸㄹㄺㄻㄼㄽㄾㄿㅀ"
    "ㅁㅂㅃㅄㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"
    "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ"
)

COMPOUND_CONSONANTS = {
    "ㄲ": ("ㄱ", "ㄱ"),
    "ㄳ": ("ㄱ", "ㅅ"),
    "ㄵ": ("ㄴ", "ㅈ"),
    "ㄶ": ("ㄴ", "ㅎ"),
    "ㄸ": ("ㄷ", "ㄷ"),
    "ㄺ": ("ㄹ", "ㄱ"),
    "ㄻ": ("ㄹ", "ㅁ"),
    "ㄼ": ("ㄹ", "ㅂ"),
    "ㄽ": ("ㄹ", "ㅅ"),
    "ㄾ": ("ㄹ", "ㅌ"),
    "ㄿ": ("ㄹ", "ㅍ"),
    "ㅀ": ("ㄹ", "ㅎ"),
    "ㅃ": ("ㅂ", "ㅂ"),
    "ㅄ": ("ㅂ", "ㅅ"),
    "ㅆ": ("ㅅ", "ㅅ"),
    "ㅉ": ("ㅈ", "ㅈ"),
}

COMPOUND_VOWELS = {
    "ㅘ": ("ㅗ", "ㅏ"),
    "ㅙ": ("ㅗ", "ㅐ"),
    "ㅚ": ("ㅗ", "ㅣ"),
    "ㅝ": ("ㅜ", "ㅓ"),
    "ㅞ": ("ㅜ", "ㅔ"),
    "ㅟ": ("ㅜ", "ㅣ"),
    "ㅢ": ("ㅡ", "ㅣ"),
}

HORIZONTAL_VOWELS = {"ㅗ", "ㅛ", "ㅜ", "ㅠ", "ㅡ"}


def union_geometries(*items):
    flattened = [item for item in items if item is not None and not item.is_empty]
    if not flattened:
        return GeometryCollection()
    return unary_union(flattened).buffer(0)


def stroke_path(points: Iterable[tuple[float, float]], width: float, closed: bool = False):
    points = list(points)
    if closed and points and points[0] != points[-1]:
        points.append(points[0])
    if len(points) < 2:
        return GeometryCollection()
    return LineString(points).buffer(
        width / 2.0,
        cap_style=1,
        join_style=1,
        quad_segs=8,
    )


def ellipse(cx: float, cy: float, rx: float, ry: float):
    return affinity.scale(Point(cx, cy).buffer(1.0, quad_segs=12), rx, ry)


def ellipse_ring(cx: float, cy: float, rx: float, ry: float, thickness: float):
    outer = ellipse(cx, cy, rx, ry)
    inner_rx = max(1.0, rx - thickness)
    inner_ry = max(1.0, ry - thickness)
    return outer.difference(ellipse(cx, cy, inner_rx, inner_ry)).buffer(0)


def rounded_rectangle(x: float, y: float, width: float, height: float, radius: float):
    radius = max(0.0, min(radius, width / 2.0, height / 2.0))
    if radius <= 0.0:
        return box(x, y, x + width, y + height)
    return box(x + radius, y + radius, x + width - radius, y + height - radius).buffer(
        radius,
        join_style=1,
        quad_segs=8,
    )


def arc_points(
    cx: float,
    cy: float,
    rx: float,
    ry: float,
    start_degrees: float,
    end_degrees: float,
    steps: int = 24,
):
    return [
        (
            cx + math.cos(math.radians(start_degrees + (end_degrees - start_degrees) * index / steps)) * rx,
            cy + math.sin(math.radians(start_degrees + (end_degrees - start_degrees) * index / steps)) * ry,
        )
        for index in range(steps + 1)
    ]


def deterministic_adjustment(name: str, amount: float) -> tuple[float, float]:
    digest = hashlib.sha256(name.encode("utf-8")).digest()
    x = ((digest[0] / 255.0) * 2.0 - 1.0) * amount
    scale_x = 1.0 + ((digest[1] / 255.0) * 2.0 - 1.0) * amount / 1200.0
    return x, scale_x


def uppercase_geometry(character: str, tokens: DesignTokens):
    stroke = tokens.stroke
    width = {
        "I": 360,
        "J": 500,
        "M": 820,
        "W": 900,
        "T": 650,
    }.get(character, 690)
    left = 95.0
    right = width - 95.0
    middle = width / 2.0
    top = float(CAP_HEIGHT)
    bottom = 0.0
    center_y = top / 2.0

    if character == "A":
        geometry = union_geometries(
            stroke_path([(left, bottom), (middle, top), (right, bottom)], stroke),
            stroke_path([(180, 275), (width - 180, 275)], stroke * 0.88),
        )
    elif character == "B":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path(
                [
                    (left, top - 10),
                    (width - 230, top - 10),
                    (right, top - 105),
                    (right, 470),
                    (width - 230, 365),
                    (left, 365),
                ],
                stroke,
            ),
            stroke_path(
                [
                    (left, 355),
                    (width - 215, 355),
                    (right + 8, 250),
                    (right + 8, 110),
                    (width - 215, 10),
                    (left, 10),
                ],
                stroke,
            ),
        )
    elif character == "C":
        geometry = stroke_path(arc_points(middle, center_y, width * 0.39, 310, 42, 318, 30), stroke)
    elif character == "D":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path(
                [(left, top), (width - 245, top), (right, 590), (right, 130), (width - 245, 0), (left, 0)],
                stroke,
            ),
        )
    elif character == "E":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path([(left, top), (right, top)], stroke),
            stroke_path([(left, center_y), (right - 85, center_y)], stroke),
            stroke_path([(left, bottom), (right, bottom)], stroke),
        )
    elif character == "F":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path([(left, top), (right, top)], stroke),
            stroke_path([(left, center_y), (right - 85, center_y)], stroke),
        )
    elif character == "G":
        geometry = union_geometries(
            stroke_path(arc_points(middle, center_y, width * 0.39, 310, 42, 318, 30), stroke),
            stroke_path([(middle + 45, 295), (right + 5, 295), (right + 5, 90)], stroke),
        )
    elif character == "H":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path([(right, bottom), (right, top)], stroke),
            stroke_path([(left, center_y), (right, center_y)], stroke),
        )
    elif character == "I":
        geometry = union_geometries(
            stroke_path([(middle, bottom), (middle, top)], stroke),
            stroke_path([(95, top), (width - 95, top)], stroke * 0.9),
            stroke_path([(95, bottom), (width - 95, bottom)], stroke * 0.9),
        )
    elif character == "J":
        geometry = union_geometries(
            stroke_path([(105, top), (right, top)], stroke),
            stroke_path([(right - 45, top), (right - 45, 150)] + arc_points(250, 150, 155, 145, 0, -180, 15), stroke),
        )
    elif character == "K":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path([(right, top), (left + 25, center_y), (right, bottom)], stroke),
        )
    elif character == "L":
        geometry = union_geometries(
            stroke_path([(left, top), (left, bottom)], stroke),
            stroke_path([(left, bottom), (right, bottom)], stroke),
        )
    elif character == "M":
        geometry = stroke_path(
            [(left, bottom), (left, top), (middle, 245), (right, top), (right, bottom)],
            stroke,
        )
    elif character == "N":
        geometry = stroke_path([(left, bottom), (left, top), (right, bottom), (right, top)], stroke)
    elif character == "O":
        geometry = ellipse_ring(middle, center_y, width * 0.40, 318, stroke)
    elif character == "P":
        geometry = union_geometries(
            stroke_path([(left, bottom), (left, top)], stroke),
            stroke_path(
                [(left, top), (width - 220, top), (right, 610), (right, 460), (width - 220, 355), (left, 355)],
                stroke,
            ),
        )
    elif character == "Q":
        geometry = union_geometries(
            ellipse_ring(middle, center_y, width * 0.40, 318, stroke),
            stroke_path([(middle + 70, 160), (right + 35, -25)], stroke * 0.78),
        )
    elif character == "R":
        geometry = union_geometries(
            uppercase_geometry("P", tokens)[0],
            stroke_path([(middle + 5, 355), (right + 30, bottom)], stroke),
        )
    elif character == "S":
        geometry = stroke_path(
            [
                (right - 15, 635),
                (width - 210, 705),
                (185, 675),
                (105, 555),
                (170, 425),
                (width - 170, 330),
                (right, 205),
                (width - 175, 55),
                (170, 25),
                (90, 105),
            ],
            stroke,
        )
    elif character == "T":
        geometry = union_geometries(
            stroke_path([(left - 25, top), (right + 25, top)], stroke),
            stroke_path([(middle, top), (middle, bottom)], stroke),
        )
    elif character == "U":
        geometry = stroke_path(
            [(left, top), (left, 170)] + arc_points(middle, 170, middle - left, 170, 180, 360, 18) + [(right, top)],
            stroke,
        )
    elif character == "V":
        geometry = stroke_path([(left, top), (middle, bottom), (right, top)], stroke)
    elif character == "W":
        geometry = stroke_path(
            [(left, top), (215, bottom), (middle, 330), (width - 215, bottom), (right, top)],
            stroke,
        )
    elif character == "X":
        geometry = union_geometries(
            stroke_path([(left, top), (right, bottom)], stroke),
            stroke_path([(right, top), (left, bottom)], stroke),
        )
    elif character == "Y":
        geometry = union_geometries(
            stroke_path([(left, top), (middle, center_y), (right, top)], stroke),
            stroke_path([(middle, center_y), (middle, bottom)], stroke),
        )
    elif character == "Z":
        geometry = stroke_path([(left, top), (right, top), (left, bottom), (right, bottom)], stroke)
    else:
        geometry = rounded_rectangle(left, bottom, right - left, top, tokens.corner_radius).difference(
            rounded_rectangle(
                left + stroke,
                bottom + stroke,
                right - left - stroke * 2,
                top - stroke * 2,
                max(18.0, tokens.corner_radius - stroke / 2),
            )
        )

    width = int(round(width * tokens.width_factor))
    geometry = affinity.scale(geometry, xfact=tokens.width_factor, yfact=1.0, origin=(0, 0))
    return geometry.buffer(0), width


def lowercase_geometry(character: str, tokens: DesignTokens):
    stroke = tokens.stroke * 0.88
    width = {"i": 300, "j": 355, "l": 310, "m": 860, "w": 820}.get(character, 610)
    left = 82.0
    right = width - 82.0
    middle = width / 2.0
    top = float(X_HEIGHT)
    center = top / 2.0

    if character == "a":
        geometry = union_geometries(
            ellipse_ring(middle - 25, center, width * 0.32, 220, stroke),
            stroke_path([(right - 15, 455), (right - 15, 0)], stroke),
        )
    elif character == "b":
        geometry = union_geometries(
            stroke_path([(left, 0), (left, 720)], stroke),
            ellipse_ring(middle + 5, center, width * 0.32, 220, stroke),
        )
    elif character == "c":
        geometry = stroke_path(arc_points(middle, center, width * 0.34, 220, 42, 318, 24), stroke)
    elif character == "d":
        geometry = union_geometries(
            ellipse_ring(middle - 10, center, width * 0.32, 220, stroke),
            stroke_path([(right, 0), (right, 720)], stroke),
        )
    elif character == "e":
        geometry = union_geometries(
            stroke_path(arc_points(middle, center, width * 0.34, 220, 35, 325, 24), stroke),
            stroke_path([(left + 35, center), (right, center)], stroke * 0.78),
        )
    elif character == "f":
        geometry = union_geometries(
            stroke_path([(middle - 35, 0), (middle - 35, 590)] + arc_points(middle + 75, 590, 110, 130, 180, 80, 10), stroke),
            stroke_path([(left - 5, 390), (right - 10, 390)], stroke * 0.85),
        )
    elif character == "g":
        geometry = union_geometries(
            ellipse_ring(middle - 20, center + 15, width * 0.32, 215, stroke),
            stroke_path([(right - 5, 450), (right - 5, -65)] + arc_points(middle, -65, middle - left, 105, 0, -145, 12), stroke),
        )
    elif character == "h":
        geometry = union_geometries(
            stroke_path([(left, 0), (left, 720)], stroke),
            stroke_path([(left, 365)] + arc_points(middle, 300, middle - left, 190, 170, 15, 15) + [(right, 0)], stroke),
        )
    elif character == "i":
        geometry = union_geometries(
            stroke_path([(middle, 0), (middle, 430)], stroke),
            ellipse(middle, 635, stroke * 0.52, stroke * 0.52),
        )
    elif character == "j":
        geometry = union_geometries(
            stroke_path([(middle + 30, 430), (middle + 30, -55)] + arc_points(middle - 55, -55, 85, 95, 0, -145, 10), stroke),
            ellipse(middle + 30, 635, stroke * 0.52, stroke * 0.52),
        )
    elif character == "k":
        geometry = union_geometries(
            stroke_path([(left, 0), (left, 720)], stroke),
            stroke_path([(right, top), (left + 20, center), (right, 0)], stroke),
        )
    elif character == "l":
        geometry = stroke_path([(middle, 720), (middle, 70)] + arc_points(middle + 70, 70, 70, 70, 180, 300, 8), stroke)
    elif character == "m":
        span = right - left
        first_center = left + span * 0.27
        second_start = left + span * 0.50
        second_center = left + span * 0.76
        geometry = union_geometries(
            stroke_path([(left, 0), (left, top)], stroke),
            stroke_path(
                [(left, 350)]
                + arc_points(first_center, 300, span * 0.25, 175, 175, 5, 12)
                + [(second_start, 0)],
                stroke,
            ),
            stroke_path(
                [(second_start, 350)]
                + arc_points(second_center, 300, span * 0.24, 175, 175, 5, 12)
                + [(right, 0)],
                stroke,
            ),
        )
    elif character == "n":
        geometry = union_geometries(
            stroke_path([(left, 0), (left, top)], stroke),
            stroke_path([(left, 350)] + arc_points(middle, 300, middle - left, 185, 175, 5, 14) + [(right, 0)], stroke),
        )
    elif character == "o":
        geometry = ellipse_ring(middle, center, width * 0.34, 225, stroke)
    elif character == "p":
        geometry = union_geometries(
            stroke_path([(left, -180), (left, top)], stroke),
            ellipse_ring(middle + 10, center, width * 0.32, 220, stroke),
        )
    elif character == "q":
        geometry = union_geometries(
            ellipse_ring(middle - 10, center, width * 0.32, 220, stroke),
            stroke_path([(right, top), (right, -180)], stroke),
        )
    elif character == "r":
        geometry = union_geometries(
            stroke_path([(left, 0), (left, top)], stroke),
            stroke_path([(left, 340)] + arc_points(middle - 25, 315, middle - left, 145, 175, 55, 10), stroke),
        )
    elif character == "s":
        geometry = stroke_path(
            [(right - 5, 450), (middle + 80, 510), (left + 20, 455), (left, 355), (middle, 260), (right, 170), (right - 25, 65), (middle - 90, 5), (left, 70)],
            stroke,
        )
    elif character == "t":
        geometry = union_geometries(
            stroke_path([(middle, 690), (middle, 85)] + arc_points(middle + 70, 85, 70, 80, 180, 305, 8), stroke),
            stroke_path([(left, 410), (right, 410)], stroke * 0.84),
        )
    elif character == "u":
        geometry = union_geometries(
            stroke_path([(left, top), (left, 155)] + arc_points(middle, 155, middle - left, 155, 180, 360, 16) + [(right, top)], stroke),
        )
    elif character == "v":
        geometry = stroke_path([(left, top), (middle, 0), (right, top)], stroke)
    elif character == "w":
        geometry = stroke_path([(left, top), (220, 0), (middle, 245), (width - 220, 0), (right, top)], stroke)
    elif character == "x":
        geometry = union_geometries(
            stroke_path([(left, top), (right, 0)], stroke),
            stroke_path([(right, top), (left, 0)], stroke),
        )
    elif character == "y":
        geometry = union_geometries(
            stroke_path([(left, top), (middle, 30), (right, top)], stroke),
            stroke_path([(middle, 30), (middle - 70, -170)], stroke),
        )
    elif character == "z":
        geometry = stroke_path([(left, top), (right, top), (left, 0), (right, 0)], stroke)
    else:
        uppercase, upper_width = uppercase_geometry(character.upper(), tokens)
        geometry = affinity.scale(uppercase, xfact=0.84, yfact=X_HEIGHT / CAP_HEIGHT, origin=(0, 0))
        width = int(upper_width * 0.84)

    geometry = affinity.scale(geometry, xfact=tokens.width_factor, yfact=1.0, origin=(0, 0))
    width = int(round(width * tokens.width_factor))
    return geometry.buffer(0), width


def digit_geometry(character: str, tokens: DesignTokens):
    stroke = tokens.stroke * 0.92
    width = int(round(620 * tokens.width_factor))
    left, right, middle = 90.0, 530.0, 310.0
    top, bottom, center = 700.0, 0.0, 350.0
    if character == "0":
        geometry = union_geometries(
            ellipse_ring(middle, center, 225, 315, stroke),
            stroke_path([(215, 165), (405, 535)], stroke * 0.45),
        )
    elif character == "1":
        geometry = union_geometries(
            stroke_path([(middle, bottom), (middle, top)], stroke),
            stroke_path([(190, 560), (middle, top)], stroke * 0.8),
            stroke_path([(160, bottom), (455, bottom)], stroke * 0.8),
        )
    elif character == "2":
        geometry = stroke_path(
            [(left + 20, 565), (185, 685), (430, 680), (right, 545), (475, 420), (left, 0), (right, 0)],
            stroke,
        )
    elif character == "3":
        geometry = union_geometries(
            stroke_path([(left + 10, 620), (220, 690), (450, 665), (right, 540), (455, 370), (280, 340)], stroke),
            stroke_path([(280, 340), (470, 315), (right, 155), (430, 20), (205, 15), (left, 100)], stroke),
        )
    elif character == "4":
        geometry = union_geometries(
            stroke_path([(430, bottom), (430, top)], stroke),
            stroke_path([(430, top), (left, 240), (right, 240)], stroke),
        )
    elif character == "5":
        geometry = stroke_path([(right, top), (left + 35, top), (left, 375), (390, 375), (right, 260), (485, 65), (240, 5), (left, 90)], stroke)
    elif character == "6":
        geometry = union_geometries(
            stroke_path([(465, 650), (300, 705), (145, 560), (105, 275)], stroke),
            ellipse_ring(middle, 235, 220, 220, stroke),
        )
    elif character == "7":
        geometry = stroke_path([(left, top), (right, top), (245, bottom)], stroke)
    elif character == "8":
        geometry = union_geometries(
            ellipse_ring(middle, 535, 190, 165, stroke),
            ellipse_ring(middle, 190, 225, 190, stroke),
        )
    else:
        geometry = union_geometries(
            ellipse_ring(middle, 470, 220, 220, stroke),
            stroke_path([(515, 435), (475, 150), (325, 0), (150, 60)], stroke),
        )
    return geometry.buffer(0), width


def punctuation_geometry(character: str, tokens: DesignTokens):
    stroke = tokens.stroke * 0.75
    standard_width = 460
    center = standard_width / 2.0
    dot = ellipse(center, 65, stroke * 0.46, stroke * 0.46)
    mappings = {
        "-": (stroke_path([(85, 270), (375, 270)], stroke), standard_width),
        "_": (stroke_path([(75, -25), (385, -25)], stroke), standard_width),
        "+": (union_geometries(stroke_path([(75, 310), (385, 310)], stroke), stroke_path([(center, 145), (center, 475)], stroke)), standard_width),
        "=": (union_geometries(stroke_path([(75, 220), (385, 220)], stroke), stroke_path([(75, 390), (385, 390)], stroke)), standard_width),
        ".": (dot, 300),
        ",": (union_geometries(dot, stroke_path([(center + 20, 45), (center - 20, -95)], stroke * 0.48)), 300),
        ":": (union_geometries(ellipse(center, 155, stroke * 0.43, stroke * 0.43), ellipse(center, 465, stroke * 0.43, stroke * 0.43)), 300),
        ";": (union_geometries(ellipse(center, 465, stroke * 0.43, stroke * 0.43), ellipse(center, 155, stroke * 0.43, stroke * 0.43), stroke_path([(center + 20, 135), (center - 25, -15)], stroke * 0.46)), 300),
        "!": (union_geometries(stroke_path([(center, 700), (center, 230)], stroke), dot), 300),
        "?": (union_geometries(stroke_path([(85, 590), (155, 690), (325, 685), (390, 565), (335, 450), (center, 360), (center, 245)], stroke), dot), standard_width),
        "/": (stroke_path([(85, -25), (375, 720)], stroke), standard_width),
        "\\": (stroke_path([(85, 720), (375, -25)], stroke), standard_width),
        "|": (stroke_path([(center, -60), (center, 720)], stroke), 300),
        "*": (union_geometries(stroke_path([(center, 175), (center, 555)], stroke * 0.7), stroke_path([(75, 270), (385, 460)], stroke * 0.7), stroke_path([(75, 460), (385, 270)], stroke * 0.7)), standard_width),
        "×": (union_geometries(stroke_path([(85, 170), (375, 520)], stroke), stroke_path([(375, 170), (85, 520)], stroke)), standard_width),
        "%": (union_geometries(ellipse_ring(135, 535, 75, 75, stroke * 0.45), ellipse_ring(325, 165, 75, 75, stroke * 0.45), stroke_path([(90, 60), (370, 640)], stroke * 0.55)), standard_width),
        "#": (union_geometries(stroke_path([(145, 50), (205, 670)], stroke * 0.55), stroke_path([(280, 50), (340, 670)], stroke * 0.55), stroke_path([(70, 250), (390, 250)], stroke * 0.55), stroke_path([(70, 460), (390, 460)], stroke * 0.55)), standard_width),
        "'": (stroke_path([(center, 710), (center - 25, 535)], stroke * 0.62), 260),
        '"': (union_geometries(stroke_path([(105, 710), (85, 535)], stroke * 0.58), stroke_path([(255, 710), (235, 535)], stroke * 0.58)), 360),
        "`": (stroke_path([(center - 55, 700), (center + 20, 560)], stroke * 0.62), 300),
        "~": (stroke_path([(65, 290), (155, 345), (260, 260), (395, 330)], stroke * 0.55), standard_width),
        "^": (stroke_path([(80, 360), (center, 590), (380, 360)], stroke * 0.65), standard_width),
        "<": (stroke_path([(360, 555), (95, 350), (360, 145)], stroke), standard_width),
        ">": (stroke_path([(100, 555), (365, 350), (100, 145)], stroke), standard_width),
    }
    if character in mappings:
        return mappings[character][0].buffer(0), int(mappings[character][1] * tokens.width_factor)
    if character in "()[]{}":
        open_side = character in "([{"
        is_round = character in "()"
        is_brace = character in "{}"
        if is_round:
            points = arc_points(center + (85 if open_side else -85), 320, 175, 390, 100 if open_side else 80, 260 if open_side else -80, 22)
        elif is_brace:
            x = 315 if open_side else 145
            points = [(x, 720), (center, 650), (center, 430), (130 if open_side else 330, 350), (center, 270), (center, 60), (x, -20)]
        else:
            x_outer = 325 if open_side else 135
            x_inner = 170 if open_side else 290
            points = [(x_outer, 720), (x_inner, 720), (x_inner, -20), (x_outer, -20)]
        return stroke_path(points, stroke).buffer(0), int(450 * tokens.width_factor)
    if character in "←↑→↓↔↕":
        horizontal = character in "←→↔"
        if horizontal:
            shaft = stroke_path([(70, 350), (500, 350)], stroke * 0.68)
            heads = []
            if character in "←↔":
                heads.append(stroke_path([(190, 500), (65, 350), (190, 200)], stroke * 0.68))
            if character in "→↔":
                heads.append(stroke_path([(380, 500), (505, 350), (380, 200)], stroke * 0.68))
        else:
            shaft = stroke_path([(285, 90), (285, 650)], stroke * 0.68)
            heads = []
            if character in "↑↕":
                heads.append(stroke_path([(135, 535), (285, 665), (435, 535)], stroke * 0.68))
            if character in "↓↕":
                heads.append(stroke_path([(135, 205), (285, 75), (435, 205)], stroke * 0.68))
        return union_geometries(shaft, *heads), int(570 * tokens.width_factor)
    if character in "•·":
        return ellipse(center, 310, stroke * 0.52, stroke * 0.52), 320
    if character == "…":
        return union_geometries(*(ellipse(95 + index * 140, 65, stroke * 0.42, stroke * 0.42) for index in range(3))), 470
    if character in "✓":
        return stroke_path([(65, 305), (205, 145), (465, 560)], stroke * 0.78), 530
    if character in "★☆":
        points = []
        for index in range(10):
            angle = math.radians(90 + index * 36)
            radius = 285 if index % 2 == 0 else 125
            points.append((310 + math.cos(angle) * radius, 340 + math.sin(angle) * radius))
        star = Polygon(points)
        if character == "☆":
            star = star.difference(affinity.scale(star, 0.55, 0.55, origin=(310, 340)))
        return star.buffer(0), 620
    if character in "♥♡":
        left_circle = ellipse(210, 445, 150, 145)
        right_circle = ellipse(410, 445, 150, 145)
        triangle = Polygon([(80, 450), (540, 450), (310, 60)])
        heart = union_geometries(left_circle, right_circle, triangle)
        if character == "♡":
            heart = heart.difference(affinity.scale(heart, 0.62, 0.62, origin=(310, 350)))
        return heart.buffer(0), 620
    if character == "※":
        return union_geometries(
            stroke_path([(80, 130), (390, 570)], stroke * 0.62),
            stroke_path([(390, 130), (80, 570)], stroke * 0.62),
            ellipse(center, 350, stroke * 0.45, stroke * 0.45),
        ), standard_width
    if character == "℃":
        return union_geometries(
            ellipse_ring(120, 590, 70, 70, stroke * 0.38),
            stroke_path(arc_points(340, 340, 160, 260, 45, 315, 24), stroke * 0.65),
        ), 560
    if character == "₩":
        return union_geometries(
            stroke_path([(70, 650), (180, 40), (300, 420), (420, 40), (540, 650)], stroke * 0.62),
            stroke_path([(65, 275), (545, 275)], stroke * 0.48),
            stroke_path([(65, 420), (545, 420)], stroke * 0.48),
        ), 610
    return rounded_rectangle(80, 30, 340, 650, 90).difference(rounded_rectangle(160, 110, 180, 490, 40)), 500


def map_points(points, bounds):
    x, y, width, height = bounds
    return [(x + px * width, y + py * height) for px, py in points]


def consonant_geometry(jamo: str, bounds, stroke: float):
    x, y, width, height = bounds
    if jamo in COMPOUND_CONSONANTS:
        left_jamo, right_jamo = COMPOUND_CONSONANTS[jamo]
        gap = width * 0.08
        half = (width - gap) / 2.0
        return union_geometries(
            consonant_geometry(left_jamo, (x, y, half, height), stroke * 0.88),
            consonant_geometry(right_jamo, (x + half + gap, y, half, height), stroke * 0.88),
        )
    skeletons = {
        "ㄱ": [[(0.15, 0.85), (0.85, 0.85), (0.85, 0.15)]],
        "ㄴ": [[(0.15, 0.85), (0.15, 0.15), (0.85, 0.15)]],
        "ㄷ": [[(0.85, 0.85), (0.15, 0.85), (0.15, 0.15), (0.85, 0.15)]],
        "ㄹ": [[(0.15, 0.85), (0.85, 0.85), (0.85, 0.64), (0.22, 0.64), (0.22, 0.40), (0.85, 0.40), (0.85, 0.15), (0.15, 0.15)]],
        "ㅅ": [[(0.15, 0.15), (0.50, 0.85), (0.85, 0.15)]],
        "ㅈ": [[(0.13, 0.86), (0.87, 0.86)], [(0.16, 0.15), (0.50, 0.70), (0.84, 0.15)]],
        "ㅊ": [[(0.25, 0.92), (0.75, 0.92)], [(0.13, 0.76), (0.87, 0.76)], [(0.16, 0.15), (0.50, 0.61), (0.84, 0.15)]],
        "ㅋ": [[(0.15, 0.85), (0.85, 0.85), (0.85, 0.15)], [(0.43, 0.53), (0.85, 0.53)]],
        "ㅌ": [[(0.14, 0.86), (0.86, 0.86)], [(0.14, 0.54), (0.86, 0.54)], [(0.14, 0.18), (0.86, 0.18)], [(0.20, 0.86), (0.20, 0.18)], [(0.80, 0.86), (0.80, 0.18)]],
        "ㅍ": [[(0.15, 0.78), (0.85, 0.78)], [(0.15, 0.25), (0.85, 0.25)], [(0.28, 0.88), (0.28, 0.15)], [(0.72, 0.88), (0.72, 0.15)]],
    }
    if jamo == "ㅇ":
        return ellipse_ring(x + width / 2, y + height / 2, width * 0.36, height * 0.36, stroke)
    if jamo == "ㅁ":
        return stroke_path(map_points([(0.16, 0.18), (0.16, 0.82), (0.84, 0.82), (0.84, 0.18)], bounds), stroke, closed=True)
    if jamo == "ㅂ":
        return union_geometries(
            stroke_path(map_points([(0.20, 0.88), (0.20, 0.12)], bounds), stroke),
            stroke_path(map_points([(0.80, 0.88), (0.80, 0.12)], bounds), stroke),
            stroke_path(map_points([(0.20, 0.82), (0.80, 0.82)], bounds), stroke),
            stroke_path(map_points([(0.20, 0.50), (0.80, 0.50)], bounds), stroke),
            stroke_path(map_points([(0.20, 0.18), (0.80, 0.18)], bounds), stroke),
        )
    if jamo == "ㅎ":
        return union_geometries(
            stroke_path(map_points([(0.28, 0.93), (0.72, 0.93)], bounds), stroke * 0.82),
            stroke_path(map_points([(0.16, 0.76), (0.84, 0.76)], bounds), stroke * 0.82),
            ellipse_ring(x + width / 2, y + height * 0.37, width * 0.29, height * 0.24, stroke * 0.92),
        )
    paths = skeletons.get(jamo, skeletons["ㄱ"])
    return union_geometries(*(stroke_path(map_points(path, bounds), stroke) for path in paths))


def vowel_geometry(jamo: str, bounds, stroke: float):
    x, y, width, height = bounds
    if jamo in COMPOUND_VOWELS:
        first, second = COMPOUND_VOWELS[jamo]
        return union_geometries(
            vowel_geometry(first, (x, y, width * 0.62, height), stroke * 0.90),
            vowel_geometry(second, (x + width * 0.45, y, width * 0.55, height), stroke * 0.90),
        )
    skeletons = {
        "ㅏ": [[(0.38, 0.10), (0.38, 0.90)], [(0.38, 0.50), (0.86, 0.50)]],
        "ㅑ": [[(0.38, 0.10), (0.38, 0.90)], [(0.38, 0.36), (0.86, 0.36)], [(0.38, 0.65), (0.86, 0.65)]],
        "ㅓ": [[(0.62, 0.10), (0.62, 0.90)], [(0.14, 0.50), (0.62, 0.50)]],
        "ㅕ": [[(0.62, 0.10), (0.62, 0.90)], [(0.14, 0.36), (0.62, 0.36)], [(0.14, 0.65), (0.62, 0.65)]],
        "ㅗ": [[(0.10, 0.34), (0.90, 0.34)], [(0.50, 0.34), (0.50, 0.88)]],
        "ㅛ": [[(0.10, 0.30), (0.90, 0.30)], [(0.36, 0.30), (0.36, 0.85)], [(0.66, 0.30), (0.66, 0.85)]],
        "ㅜ": [[(0.10, 0.66), (0.90, 0.66)], [(0.50, 0.12), (0.50, 0.66)]],
        "ㅠ": [[(0.10, 0.70), (0.90, 0.70)], [(0.36, 0.16), (0.36, 0.70)], [(0.66, 0.16), (0.66, 0.70)]],
        "ㅡ": [[(0.10, 0.50), (0.90, 0.50)]],
        "ㅣ": [[(0.50, 0.10), (0.50, 0.90)]],
        "ㅐ": [[(0.25, 0.10), (0.25, 0.90)], [(0.25, 0.50), (0.58, 0.50)], [(0.75, 0.10), (0.75, 0.90)]],
        "ㅒ": [[(0.22, 0.10), (0.22, 0.90)], [(0.22, 0.36), (0.52, 0.36)], [(0.22, 0.65), (0.52, 0.65)], [(0.76, 0.10), (0.76, 0.90)]],
        "ㅔ": [[(0.38, 0.10), (0.38, 0.90)], [(0.08, 0.50), (0.38, 0.50)], [(0.75, 0.10), (0.75, 0.90)]],
        "ㅖ": [[(0.40, 0.10), (0.40, 0.90)], [(0.08, 0.36), (0.40, 0.36)], [(0.08, 0.65), (0.40, 0.65)], [(0.76, 0.10), (0.76, 0.90)]],
    }
    paths = skeletons.get(jamo, skeletons["ㅣ"])
    return union_geometries(*(stroke_path(map_points(path, bounds), stroke) for path in paths))


def hangul_syllable_geometry(character: str, tokens: DesignTokens):
    syllable_index = ord(character) - 0xAC00
    initial_index = syllable_index // 588
    vowel_index = (syllable_index % 588) // 28
    final_index = syllable_index % 28
    initial = L_COMPAT[initial_index]
    vowel = V_COMPAT[vowel_index]
    final = T_COMPAT[final_index]
    stroke = tokens.stroke * 0.72

    if final:
        final_bounds = (90, 20, 820, 215)
        if vowel in HORIZONTAL_VOWELS:
            initial_bounds = (110, 520, 780, 285)
            vowel_bounds = (130, 270, 740, 225)
        else:
            initial_bounds = (50, 270, 475, 530)
            vowel_bounds = (545, 270, 405, 530)
    else:
        final_bounds = None
        if vowel in HORIZONTAL_VOWELS:
            initial_bounds = (105, 405, 790, 395)
            vowel_bounds = (125, 80, 750, 290)
        else:
            initial_bounds = (45, 80, 480, 720)
            vowel_bounds = (545, 80, 410, 720)

    geometry = union_geometries(
        consonant_geometry(initial, initial_bounds, stroke),
        vowel_geometry(vowel, vowel_bounds, stroke),
        consonant_geometry(final, final_bounds, stroke * 0.90) if final and final_bounds else None,
    )
    return geometry.buffer(0), 1000


def standalone_jamo_geometry(character: str, tokens: DesignTokens):
    codepoint = ord(character)
    if character in COMPATIBILITY_JAMO:
        jamo = character
    elif 0x1100 <= codepoint <= 0x1112:
        jamo = L_COMPAT[codepoint - 0x1100]
    elif 0x1161 <= codepoint <= 0x1175:
        jamo = V_COMPAT[codepoint - 0x1161]
    elif 0x11A8 <= codepoint <= 0x11C2:
        jamo = T_COMPAT[codepoint - 0x11A7]
    else:
        jamo = "ㅇ"
    stroke = tokens.stroke * 0.75
    if jamo in V_COMPAT:
        geometry = vowel_geometry(jamo, (120, 70, 760, 740), stroke)
    else:
        geometry = consonant_geometry(jamo, (120, 70, 760, 740), stroke)
    return geometry.buffer(0), 1000


def character_geometry(character: str, tokens: DesignTokens):
    codepoint = ord(character)
    if "A" <= character <= "Z":
        return uppercase_geometry(character, tokens)
    if "a" <= character <= "z":
        return lowercase_geometry(character, tokens)
    if "0" <= character <= "9":
        return digit_geometry(character, tokens)
    if 0xAC00 <= codepoint <= 0xD7A3:
        return hangul_syllable_geometry(character, tokens)
    if 0x1100 <= codepoint <= 0x11FF or 0x3130 <= codepoint <= 0x318F:
        return standalone_jamo_geometry(character, tokens)
    return punctuation_geometry(character, tokens)


def draw_geometry(pen, geometry) -> None:
    if geometry.is_empty:
        return
    if isinstance(geometry, Polygon):
        polygons = [geometry]
    elif isinstance(geometry, MultiPolygon):
        polygons = list(geometry.geoms)
    else:
        polygons = [item for item in getattr(geometry, "geoms", ()) if isinstance(item, Polygon)]
    for polygon in polygons:
        polygon = orient(polygon, sign=1.0)
        rings = [polygon.exterior, *polygon.interiors]
        for ring in rings:
            coordinates = list(ring.coords)
            if len(coordinates) < 4:
                continue
            pen.moveTo(tuple(coordinates[0]))
            for point in coordinates[1:-1]:
                pen.lineTo(tuple(point))
            pen.closePath()


def glyph_name(character: str) -> str:
    return UV2AGL.get(ord(character), f"uni{ord(character):04X}")


def configure_font(font: Font, family_name: str) -> None:
    info = font.info
    info.familyName = family_name
    info.styleName = "Bold"
    info.styleMapFamilyName = family_name
    info.styleMapStyleName = "bold"
    info.postscriptFontName = family_name.replace(" ", "") + "-Bold"
    info.postscriptFullName = family_name + " Bold"
    info.postscriptWeightName = "Bold"
    info.unitsPerEm = UPM
    info.ascender = ASCENDER
    info.descender = DESCENDER
    info.capHeight = CAP_HEIGHT
    info.xHeight = X_HEIGHT
    info.openTypeHheaAscender = ASCENDER
    info.openTypeHheaDescender = DESCENDER
    info.openTypeHheaLineGap = LINE_GAP
    info.openTypeOS2TypoAscender = ASCENDER
    info.openTypeOS2TypoDescender = DESCENDER
    info.openTypeOS2TypoLineGap = LINE_GAP
    info.openTypeOS2WinAscent = ASCENDER
    info.openTypeOS2WinDescent = abs(DESCENDER)
    info.openTypeOS2WeightClass = 700
    info.openTypeOS2WidthClass = 5
    info.openTypeNameVersion = "Version 0.100"
    info.openTypeNameUniqueID = f"0.100;TRIADCANVAS;{info.postscriptFontName}"
    info.versionMajor = 0
    info.versionMinor = 100
    info.copyright = "Copyright 2026 Triad Canvas"
    info.trademark = "Project One Display is an original internal prototype for Triad Canvas."
    info.note = "Original reference-inspired geometric rounded display design; no source outline was traced."


def add_notdef(font: Font, tokens: DesignTokens) -> None:
    glyph = font.newGlyph(".notdef")
    glyph.width = 700
    outer = rounded_rectangle(70, -20, 560, 760, tokens.corner_radius)
    inner = rounded_rectangle(175, 90, 350, 540, 45)
    diagonal = stroke_path([(190, 115), (510, 610)], tokens.stroke * 0.45)
    draw_geometry(glyph.getPen(), union_geometries(outer.difference(inner), diagonal))


def build_ufo(character_set: str, tokens: DesignTokens, ufo_path: Path, exact_name: bool) -> dict[str, object]:
    family_name = "Project One Display" if exact_name else f"Project One Display {tokens.key.replace('_', ' ').title()}"
    font = Font()
    configure_font(font, family_name)
    add_notdef(font, tokens)
    glyph_order = [".notdef"]
    name_for_character: dict[str, str] = {}
    diagnostics: list[dict[str, object]] = []

    for character in sorted(set(character_set), key=ord):
        name = glyph_name(character)
        if name in font:
            name = f"uni{ord(character):04X}"
        glyph = font.newGlyph(name)
        glyph.unicodes = [ord(character)]
        if character in {" ", "\u00A0"}:
            glyph.width = 360
        else:
            geometry, advance = character_geometry(character, tokens)
            offset, scale_x = deterministic_adjustment(name, tokens.irregularity)
            geometry = affinity.scale(geometry, xfact=scale_x, yfact=1.0, origin=(advance / 2.0, 0.0))
            geometry = affinity.translate(geometry, xoff=offset)
            geometry = geometry.buffer(0)
            draw_geometry(glyph.getPen(), geometry)
            glyph.width = int(max(250, advance + 110))
            min_x, min_y, max_x, max_y = geometry.bounds if not geometry.is_empty else (0, 0, 0, 0)
            diagnostics.append(
                {
                    "character": character,
                    "codepoint": f"U+{ord(character):04X}",
                    "glyph": name,
                    "advance": glyph.width,
                    "bounds": [round(min_x, 2), round(min_y, 2), round(max_x, 2), round(max_y, 2)],
                    "valid_geometry": geometry.is_valid,
                    "empty": geometry.is_empty,
                }
            )
        glyph_order.append(name)
        name_for_character[character] = name

    font.glyphOrder = glyph_order
    kern_pairs = {
        ("A", "V"): -55,
        ("A", "W"): -45,
        ("A", "T"): -40,
        ("F", "A"): -35,
        ("L", "T"): -45,
        ("P", "A"): -35,
        ("T", "A"): -45,
        ("T", "O"): -30,
        ("V", "A"): -55,
        ("W", "A"): -45,
        ("Y", "A"): -55,
        ("P", "R"): -15,
        ("O", "N"): -15,
    }
    for (left, right), value in kern_pairs.items():
        if left in name_for_character and right in name_for_character:
            font.kerning[(name_for_character[left], name_for_character[right])] = value

    ufo_path.parent.mkdir(parents=True, exist_ok=True)
    if ufo_path.exists():
        shutil.rmtree(ufo_path)
    font.save(ufo_path, overwrite=True)
    diagnostics_path = ufo_path.parent / f"{ufo_path.stem}_geometry_report.json"
    diagnostics_path.write_text(
        json.dumps(
            {
                "family": family_name,
                "variant": tokens.key,
                "tokens": tokens.__dict__,
                "metrics": {
                    "upm": UPM,
                    "ascender": ASCENDER,
                    "descender": DESCENDER,
                    "line_gap": LINE_GAP,
                    "cap_height": CAP_HEIGHT,
                    "x_height": X_HEIGHT,
                },
                "glyph_count": len(glyph_order),
                "glyphs": diagnostics,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
        newline="\n",
    )
    return {"family": family_name, "glyph_count": len(glyph_order), "diagnostics": str(diagnostics_path)}


def compile_ufo(python: Path, ufo_path: Path, output_directory: Path) -> dict[str, object]:
    output_directory.mkdir(parents=True, exist_ok=True)
    command = [
        str(python),
        "-m",
        "fontmake",
        "-u",
        str(ufo_path),
        "-o",
        "ttf",
        "otf",
        "--output-dir",
        str(output_directory),
        "--verbose",
        "WARNING",
    ]
    completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    log_path = output_directory / "fontmake.log"
    log_path.write_text(
        "COMMAND\n" + subprocess.list2cmdline(command) + "\n\nSTDOUT\n" + completed.stdout + "\n\nSTDERR\n" + completed.stderr,
        encoding="utf-8",
        newline="\n",
    )
    if completed.returncode != 0:
        raise RuntimeError(f"fontmake failed for {ufo_path}: {completed.returncode}\n{completed.stderr}")
    fonts = sorted(str(path) for path in output_directory.glob("*") if path.suffix.lower() in {".ttf", ".otf"})
    return {"command": command, "return_code": completed.returncode, "fonts": fonts, "log": str(log_path)}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lab", type=Path, required=True)
    parser.add_argument("--characters", type=Path, required=True)
    parser.add_argument("--variants", action="store_true")
    parser.add_argument("--final", choices=sorted(VARIANTS))
    args = parser.parse_args()

    lab = args.lab.resolve()
    characters = args.characters.read_text(encoding="utf-8")
    selected = [VARIANTS[key] for key in sorted(VARIANTS)] if args.variants else []
    if args.final:
        selected.append(VARIANTS[args.final])
    if not selected:
        parser.error("Specify --variants or --final")

    manifest: list[dict[str, object]] = []
    for tokens in selected:
        exact_name = bool(args.final and tokens.key == args.final and not args.variants)
        label = "FINAL" if exact_name else tokens.key
        ufo_path = lab / "source" / label / ("ProjectOneDisplay.ufo" if exact_name else f"ProjectOneDisplay_{label}.ufo")
        output_directory = lab / "build" / label
        source_result = build_ufo(characters, tokens, ufo_path, exact_name)
        compile_result = compile_ufo(Path(sys.executable), ufo_path, output_directory)
        manifest.append(
            {
                "label": label,
                "variant": tokens.key,
                "source": str(ufo_path),
                "source_result": source_result,
                "compile_result": compile_result,
            }
        )

    manifest_path = lab / "reports" / ("final_build_manifest.json" if args.final and not args.variants else "variant_build_manifest.json")
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8", newline="\n")
    print(json.dumps(manifest, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
