# Project One Display

`Project One Display Bold`는 Project One의 짧은 제목과 상태 Label을 위한
오리지널 Rounded Display Font P0입니다. Logo reference의 굵기, 둥근 비례,
counter, 장난감 같은 분위기만 해석했으며, 원본 글자 outline이나 기존 상용
font outline은 trace/copy하지 않았습니다. Logo의 발바닥 mark는 glyph에
포함하지 않았고 `O`는 정상적인 rounded oval입니다.

## Metadata

- Family: `Project One Display`
- Style: `Bold`
- PostScript Name: `ProjectOneDisplay-Bold`
- Version: `0.100`
- Copyright: `Copyright 2026 Triad Canvas`
- License: `LICENSE_REVIEW_REQUIRED`

## Selected design

최종 선택은 `B_REFERENCE`입니다.

- Stroke token: 약 `170`
- Corner radius token: 약 `96`
- Width: `100%`
- Deterministic handmade adjustment: 최대 `±6 unit`
- 24px counter test: `PROB8O0C`의 enclosed counter `9`, ink ratio `0.3723`

`A_SAFE`는 24px에서 가장 여유롭지만 title 존재감이 약했고,
`C_CUTE`는 가장 통통하지만 button size에서 과밀해질 여지가 있었습니다.
`B_REFERENCE`는 세 variant 모두 유지한 9개 counter와 Project One의
toy-like title weight 사이의 균형이 가장 좋았습니다.

## Metrics

- Units Per Em: `1000`
- Ascender: `850`
- Descender: `-270`
- Line Gap: `100`
- Cap Height: `720`
- x-Height: `520`

초기 descender 후보 `-200`은 rounded lowercase descender의 실제 bound를
포함하지 못해, 자동 clipping 검사 결과에 따라 한 번만 `-270`으로 bounded
조정했습니다. Hangul top bound는 ascender 안에 남습니다.

## Glyph coverage

- ASCII `U+0020–U+007E`: 95
- `U+00A0 NO-BREAK SPACE`: 1
- `U+00B7 MIDDLE DOT`, `U+00D7 MULTIPLICATION SIGN`: 2
- Modern Hangul Jamo: 67
- Compatibility Hangul Jamo: 51
- Arrow `U+2190–U+2193`: 4
- Project/required precomposed Hangul syllables: 294
- Total mapped code points: 514

Project UI에서 읽은 실제 P0 subset과 prompt 필수/priority phrase를
합쳤습니다. 품질이 낮은 11,172자 자동 확장을 배포하지 않았습니다.

`FULL_HANGUL_EXTENSION_PENDING`

## Source and reproducibility

- `ProjectOneDisplay.ufoz`: standard compressed UFO source
- `ProjectOneDisplay-Bold.ttf`
- `ProjectOneDisplay-Bold.otf`
- `ProjectOneDisplay_ProjectGlyphs.txt`: sorted Unicode character set
- `Scripts/`: deterministic extraction/build/validation scripts

Build architecture:

```text
Production UI string audit
→ Unicode P0 subset
→ deterministic Shapely outline rules
→ UFO source
→ fontmake/ufo2ft
→ TTF + OTF
→ fontTools + OTS + FontBakery + Pillow previews
```

## Validation summary

- TTF/OTF open: PASS
- Required cmap: 514/514
- Missing required glyph: 0
- Empty required non-whitespace glyph: 0
- Invalid/non-finite bounds: 0
- Metric clipping: 0
- Zero advance regular glyph: 0
- Extreme side bearing: 0
- OTS sanitize: PASS for TTF and OTF
- FontBakery: `ERROR 0`, `FATAL 0`, `FAIL 1`, `WARN 4`, `PASS 76`

FontBakery의 유일한 FAIL은 unhinted TrueType `prep` table의 smart dropout
instruction 부재입니다. 이 P0의 주 사용처가 Unity TMP SDF이므로 outline
손상 위험이 있는 자동 hinting을 적용하지 않았고, unhinted build를
의도적으로 유지했습니다.

## Unity usage

P0 용도:

- Main Menu button
- Ready / Countdown
- Results title
- 짧은 HUD / Tutorial / Popup title
- 약 1–8자 길이의 짧은 Korean label

긴 본문, Post-it 목록, 설정 설명, 오류 메시지는 기존 본문 font를
유지합니다. 이번 checkpoint는 Production Scene의 기존 TMP reference를
자동 교체하지 않습니다.

TMP target:

- Atlas: `4096 × 4096`
- Population: `Static`
- Render Mode: `SDF32`
- Padding: `12`
- Multi Atlas: OFF
- Fallback: existing `NotoSansKR-VariableFont_wght SDF`

Static TMP asset 생성은 `Dynamic 생성 → exact subset 추가 → Missing 0 및
atlas 1장 검증 → Static 전환` 순서를 사용합니다.
