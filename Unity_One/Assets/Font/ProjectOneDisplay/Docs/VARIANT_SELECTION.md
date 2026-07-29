# Project One Display Variant Selection

## 판정

`B_REFERENCE`를 최종 `Project One Display Bold v0.100`으로 선택한다.

## 비교

| Variant | Stroke | Radius | Width | Irregularity | 24px holes | Ink ratio | 판정 |
|---|---:|---:|---:|---:|---:|---:|---|
| A_SAFE | 155 | 80 | 96% | ±4 | 9 | 0.3543 | 작은 크기 우수, title weight 약함 |
| B_REFERENCE | 170 | 96 | 100% | ±6 | 9 | 0.3723 | 균형 최상, 선택 |
| C_CUTE | 182 | 110 | 105% | ±8 | 9 | 0.4088 | 강한 title, button 과밀 위험 |

세 variant 모두 TTF/OTF open, required cmap, bounds, metric clipping,
OTS sanitize를 통과했다. 실제 1920×1080 / 2560×1440 Preview를 함께
검토했으며, 자동 score만으로 선택하지 않았다.

## Reference 해석

- 굵고 거의 일정한 stroke
- 크게 rounded된 terminal/corner
- 넉넉한 counter
- 약간 넓은 toy-like proportion
- deterministic한 소량의 handmade offset
- 3D 느낌은 outline이 아니라 Unity TMP material에서 처리

Logo mark는 font glyph로 만들지 않았고, reference의 outline을 trace하지
않았다. 첨부 logo image file은 현재 task attachment에서 접근할 수 없어
prompt의 상세 style specification을 source of truth로 사용했다.

`REFERENCE_IMAGE_UNAVAILABLE_STYLE_SPEC_USED`
