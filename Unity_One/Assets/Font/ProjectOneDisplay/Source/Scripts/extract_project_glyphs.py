#!/usr/bin/env python3
"""Extract the Project One P0 display-font character set without mutating Unity."""

from __future__ import annotations

import argparse
import ast
import json
import re
import unicodedata
from collections import Counter
from pathlib import Path


MANDATORY_STRINGS = (
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
    "가 각 간 값 강 겹 공",
    "나 난 날",
    "다 달",
    "마 막 모 몸",
    "바 발 빵",
    "사 상",
    "아 안 앙",
    "자 잡",
    "차 카 타 파 하",
    "준 비 게 임 포 스 트 잇 결 과 관 전 설 정 던 지 기 벽 타 기",
)

UI_SYMBOLS = "·×←↑→↓"
RECOMMENDED_WHITESPACE = "\u00A0"
CANONICAL_PROJECT_HANGUL = (
    "가각간갈감강같개거건검것게격결계고곧공과관구귀그금기까끊나낙남내너널네놓눌는니닙"
    "다닥단닫달답대더던도독돌동되된두뒤드득든들떨또뛰뜯라락랑래러레려력렸령례로록료류륨"
    "르른를름리릭림립마막만말맞맵메면모목몰몸못무물미밀바밖반발방배번벽보복본볼부분불붙"
    "비빈빨사살상새생서선설성세션소속송수순스습승시식실쓸아안않알았앙야약어언얼업없었에"
    "여역연열예오올와완외요용우운움워원위유으은을음의이인일임입잇있자작잡재적전점접정제"
    "젝졌조족존종좌주준줍중증지직진집차착찰참찾채책체초최추출충측치침카칸캐컬코크큰클키"
    "타탈태택터테템토튜트틀티파패포폰표품프플픽필하한할합해했행험현혔호홈화확환활회획효"
    "후휩히"
)
YAML_TEXT_PATTERN = re.compile(r"^\s*(?:m_text|m_Text|text):\s*(.*)$")
QUOTED_STRING_PATTERN = re.compile(r'(?:"(?:\\.|[^"\\])*"|@"(?:""|[^"])*")')
HANGUL_PATTERN = re.compile(r"[\u1100-\u11ff\u3130-\u318f\uac00-\ud7a3]")

PRODUCTION_SCENES = (
    "Unity_One/Assets/Scenes/Play/Boot_Splash.unity",
    "Unity_One/Assets/Scenes/Play/MainMenu.unity",
    "Unity_One/Assets/Scenes/Play/InGame.unity",
    "Unity_One/Assets/Scenes/Play/Tutorial_Desk.unity",
)

RUNTIME_CSHARP_WHITELIST = (
    "Unity_One/Assets/Scripts/UI/CoinHUD.cs",
    "Unity_One/Assets/Scripts/UI/CharacterSelectionView.cs",
    "Unity_One/Assets/Scripts/UI/MissionHUD.cs",
    "Unity_One/Assets/Scripts/UI/MatchTimerHUD.cs",
    "Unity_One/Assets/Scripts/UI/LoadingOverlayUI.cs",
    "Unity_One/Assets/Scripts/UI/PostItInventoryHUD.cs",
    "Unity_One/Assets/Scripts/UI/PostItGuessResultsView.cs",
    "Unity_One/Assets/Scripts/UI/PostItGuessingHUD.cs",
    "Unity_One/Assets/Scripts/UI/PostItRoundPrepHUD.cs",
    "Unity_One/Assets/Scripts/UI/ResultsUI.cs",
    "Unity_One/Assets/Scripts/UI/StaminaHUD.cs",
    "Unity_One/Assets/Scripts/UI/TutorialUI.cs",
    "Unity_One/Assets/Scripts/UI/CodeOnly/ProjectOneCodeOnlyUIFactory.cs",
    "Unity_One/Assets/Scripts/System/MainMenu/LobbyManager.cs",
    "Unity_One/Assets/Scripts/System/MainMenu/UI/RelayManager.cs",
    "Unity_One/Assets/Scripts/System/MainMenu/UI/RoundUI.cs",
    "Unity_One/Assets/Scripts/System/Lobby/RoomLobbyUI.cs",
    "Unity_One/Assets/Scripts/System/GamePlay/CharacterSelectionSystem.cs",
    "Unity_One/Assets/Scripts/System/GamePlay/DeveloperIntrusionGimmick.cs",
    "Unity_One/Assets/Scripts/System/GamePlay/RoundMissionManager.cs",
    "Unity_One/Assets/Scripts/Tutorial/TutorialDirector.cs",
    "Unity_One/Assets/Scripts/Tutorial/TutorialLocalHostLauncher.cs",
)

EXCLUDED_CS_MARKERS = (
    "Debug.Log",
    "Debug.LogWarning",
    "Debug.LogError",
    "Exception(",
    "throw new",
    "[Tooltip",
    "[Header",
    "EditorGUILayout",
    "GUILayout.",
    "AssetDatabase",
    "MenuItem(",
    "nameof(",
    "Assert.",
    "Application.dataPath",
    "StackTrace",
)


def decode_csharp_literal(token: str) -> str:
    if token.startswith('@"'):
        return token[2:-1].replace('""', '"')
    try:
        value = ast.literal_eval(token)
        return value if isinstance(value, str) else ""
    except (SyntaxError, ValueError):
        return token[1:-1]


def is_supported_character(character: str) -> bool:
    codepoint = ord(character)
    return (
        0x20 <= codepoint <= 0x7E
        or 0x1100 <= codepoint <= 0x11FF
        or 0x3130 <= codepoint <= 0x318F
        or 0xAC00 <= codepoint <= 0xD7A3
        or character in UI_SYMBOLS
    )


def extract_yaml_text(path: Path) -> list[str]:
    results: list[str] = []
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return results
    for line in lines:
        match = YAML_TEXT_PATTERN.match(line)
        if not match:
            continue
        value = match.group(1).strip()
        if value.startswith('"') and value.endswith('"'):
            try:
                decoded = json.loads(value)
                value = decoded if isinstance(decoded, str) else value
            except json.JSONDecodeError:
                value = value[1:-1].replace(r"\n", " ").replace(r"\"", '"')
        if HANGUL_PATTERN.search(value) or any(character in UI_SYMBOLS for character in value):
            results.append(value)
    return results


def extract_runtime_csharp_text(path: Path) -> list[str]:
    normalized = path.as_posix().lower()
    if "/editor/" in normalized or "/tests/" in normalized:
        return []
    results: list[str] = []
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return results
    for line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if any(marker in line for marker in EXCLUDED_CS_MARKERS):
            continue
        for token in QUOTED_STRING_PATTERN.findall(line):
            value = decode_csharp_literal(token)
            if HANGUL_PATTERN.search(value) or any(character in UI_SYMBOLS for character in value):
                results.append(value)
    return results


def compatibility_jamo() -> str:
    return (
        "ㄱㄲㄳㄴㄵㄶㄷㄸㄹㄺㄻㄼㄽㄾㄿㅀ"
        "ㅁㅂㅃㅄㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"
        "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ"
    )


def modern_jamo() -> str:
    choseong = "".join(chr(0x1100 + index) for index in range(19))
    jungseong = "".join(chr(0x1161 + index) for index in range(21))
    jongseong = "".join(chr(0x11A8 + index) for index in range(27))
    return choseong + jungseong + jongseong


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--sources", required=True, type=Path)
    parser.add_argument("--strings", required=True, type=Path)
    args = parser.parse_args()

    project = args.project.resolve()
    discovered: list[dict[str, object]] = []
    collected_text: list[str] = list(MANDATORY_STRINGS)

    source_paths = [project / relative for relative in PRODUCTION_SCENES]
    source_paths.extend(
        sorted((project / "Unity_One/Assets/ProjectONE/UI/Prefabs/Text").glob("*.prefab"))
    )
    source_paths.extend(project / relative for relative in RUNTIME_CSHARP_WHITELIST)
    for path in source_paths:
        values = (
            extract_runtime_csharp_text(path)
            if path.suffix.lower() == ".cs"
            else extract_yaml_text(path)
        )
        if not values:
            continue
        relative = path.relative_to(project).as_posix()
        discovered.append({"path": relative, "strings": values})
        collected_text.extend(values)

    discovered_characters = {
        character
        for text in collected_text
        for character in text
        if is_supported_character(character)
    }
    project_characters = {
        character
        for character in discovered_characters
        if not (0xAC00 <= ord(character) <= 0xD7A3)
    } | set(CANONICAL_PROJECT_HANGUL)
    mandatory_characters = {
        character
        for text in MANDATORY_STRINGS
        for character in text
        if is_supported_character(character)
    }
    ascii_characters = {chr(codepoint) for codepoint in range(0x20, 0x7F)}
    jamo_characters = set(compatibility_jamo() + modern_jamo())
    ui_characters = set(UI_SYMBOLS)
    whitespace_characters = set(RECOMMENDED_WHITESPACE)

    final_characters = (
        project_characters
        | mandatory_characters
        | ascii_characters
        | jamo_characters
        | ui_characters
        | whitespace_characters
    )
    ordered = "".join(sorted(final_characters, key=ord))

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.sources.parent.mkdir(parents=True, exist_ok=True)
    args.strings.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(ordered, encoding="utf-8", newline="\n")
    args.sources.write_text(
        json.dumps(discovered, ensure_ascii=False, indent=2),
        encoding="utf-8",
        newline="\n",
    )
    args.strings.write_text(
        "\n".join(dict.fromkeys(collected_text)) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    category_counts = Counter(unicodedata.category(character) for character in ordered)
    hangul_syllables = [character for character in ordered if 0xAC00 <= ord(character) <= 0xD7A3]
    report = {
        "project_root": str(project),
        "source_file_count": len(discovered),
        "source_string_count": sum(len(item["strings"]) for item in discovered),
        "mandatory_string_count": len(MANDATORY_STRINGS),
        "raw_discovered_hangul_syllable_count": len(
            {character for character in discovered_characters if 0xAC00 <= ord(character) <= 0xD7A3}
        ),
        "canonical_project_hangul_syllable_count": len(set(CANONICAL_PROJECT_HANGUL)),
        "character_count": len(ordered),
        "hangul_syllable_count": len(hangul_syllables),
        "modern_jamo_count": len(set(modern_jamo())),
        "compatibility_jamo_count": len(set(compatibility_jamo())),
        "ascii_count": len(ascii_characters),
        "ui_symbol_count": len(ui_characters),
        "recommended_whitespace_count": len(whitespace_characters),
        "unicode_category_counts": dict(sorted(category_counts.items())),
        "codepoints": [
            {
                "character": character,
                "codepoint": f"U+{ord(character):04X}",
                "name": unicodedata.name(character, "UNNAMED"),
                "source": "project_or_required",
            }
            for character in ordered
        ],
    }
    args.report.write_text(
        json.dumps(report, ensure_ascii=False, indent=2),
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps({key: report[key] for key in report if key != "codepoints"}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
