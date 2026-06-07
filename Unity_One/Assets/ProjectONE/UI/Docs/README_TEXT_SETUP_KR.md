# Project ONE TMP 텍스트 스타일 시스템

## 1. PNG에 글자를 박지 않고 TMP로 올리는 이유
Project ONE UI 이미지는 배경, 버튼, 카드 같은 시각 프레임 역할만 맡고, 실제 문구는 `TextMeshProUGUI`로 얹습니다. 이렇게 해야 해상도 변경, 언어 변경, 방 코드/점수/코인 같은 동적 값 변경, 접근성 조정이 가능하며 같은 PNG를 여러 문구에 재사용할 수 있습니다.

## 2. ProjectOneTextStyleSet 사용법
`Assets/ProjectONE/UI/ScriptableObjects/ProjectOneTextStyleSet.asset`은 Project ONE UI Kit의 기본 색상과 글자 크기를 담는 ScriptableObject입니다. `defaultFont`에는 프로젝트 안에 있는 한국어 지원 TMP Font Asset을 연결합니다. 현재 프로젝트에서는 `Assets/NotoSansKR/NotoSansKR-VariableFont_wght SDF.asset` 같은 기존 TMP Font Asset을 우선 사용할 수 있습니다.

## 3. Text Prefab 사용법
`Assets/ProjectONE/UI/Prefabs/Text/` 아래의 `PF_Text_*.prefab`을 Canvas 하위에 배치하고 `Text` 오브젝트의 내용을 원하는 문구로 바꾸면 됩니다. 프리팹 구조는 기본적으로 `PrefabRoot/Text`만 사용하며, 모든 `TextMeshProUGUI`의 `Raycast Target`은 `false`입니다.

## 4. ProjectOneTextStyleApplier 사용법
`ProjectOneTextStyleApplier`는 `TMP_Text target`, `ProjectOneTextStyleSet styleSet`, `ProjectOneTextStyleType styleType`을 기준으로 크기, 색, 정렬, 굵기, fontWeight, character spacing, line spacing, 줄바꿈, overflow를 적용합니다. `useSoftShadow`를 켜면 `UnityEngine.UI.Shadow` 컴포넌트를 사용하고, `useWhiteOutline` 또는 `useNavyOutline`을 켜면 `UnityEngine.UI.Outline` 컴포넌트를 사용합니다. shared material은 직접 수정하지 않습니다.

## 5. 한국어 TMP Font Asset 연결 방법
외부 폰트 다운로드 없이 프로젝트에 이미 있는 TMP Font Asset을 사용합니다. 새 폰트가 필요한 경우에도 이 시스템은 폰트를 만들거나 다운로드하지 않습니다. `ProjectOneTextStyleSet.asset`의 `defaultFont`에 한국어 글리프가 들어 있는 TMP Font Asset을 직접 연결하면 모든 프리팹과 적용 컴포넌트가 같은 폰트를 씁니다. 프로젝트 안에서 TMP Font Asset을 찾지 못하면 TMP 기본 폰트를 임시로 사용하고 Console Warning을 남깁니다.

## 6. 버튼 텍스트 Raycast Target false 이유
버튼의 실제 클릭 판정은 Button 또는 Image가 가져야 합니다. 텍스트가 Raycast Target을 켜면 클릭 이벤트가 텍스트에서 가로막혀 버튼 입력이 불안정해질 수 있으므로, 이 시스템의 모든 TMP 텍스트는 `Raycast Target = false`를 기본으로 둡니다.

## 7. 동적 텍스트 변경 방법
HUD 값은 `ProjectOneHUDTextView`에서 `SetCoin`, `SetTime`, `SetStamina`, `SetMission`으로 갱신합니다. 로비 값은 `ProjectOneLobbyTextView`에서 `SetRoomCode`, `SetReadyCount`, `SetTopMessage`로 갱신합니다. 결과 화면 값은 `ProjectOneResultTextView`에서 `SetResult`, `SetRanking`으로 갱신합니다.

## 8. 대표 텍스트 스타일 추천
`Ready`는 `ReadyLarge`에 `useSoftShadow`를 켜고 필요하면 `useWhiteOutline`을 켜는 구성이 어울립니다. `승리!`는 `ResultTitle`에 약한 shadow를 권장합니다. `ROOM CODE :`는 `RoomCodeLabel`, 실제 코드 값인 `RPMKJK`는 `RoomCodeValue`를 사용합니다. 메뉴 버튼 문구는 `MenuButton`, 카드 제목은 `CardTitle`, 카드 본문은 `CardBody`, 랭킹 이름은 `RankingName`, 점수는 `RankingScore`를 권장합니다.

## 9. Localization String Table 확장
현재 뷰 스크립트는 문자열을 직접 받아 TMP에 반영하는 구조입니다. 나중에 Unity Localization을 붙일 때는 `SetMission`, `SetTopMessage`, `SetRanking` 등에 전달하는 문자열을 String Table에서 가져오도록 바꾸면 PNG나 프리팹 구조를 바꾸지 않고 다국어로 확장할 수 있습니다.

## 10. 기본 폰트처럼 얇고 밋밋하게 보일 때
`ProjectOneTextStyleSet.asset`의 `defaultFont`가 `LiberationSans SDF` 또는 TMP 기본 폰트이면 한글 UI가 얇고 기본 폰트처럼 보일 수 있습니다. `Assets/ProjectONE/UI/Docs/text_font_candidates.csv`를 열어 `looks_like_korean_font`와 `looks_like_bold_candidate`가 `true`인 TMP Font Asset을 확인한 뒤 `defaultFont`에 연결합니다.

## 11. Bold/ExtraBold 계열 TMP Font Asset 연결
프로젝트 안에 `Bold`, `Black`, `ExtraBold`, `Heavy`, `SemiBold`, `Medium`, `Pretendard`, `SUIT`, `Gmarket`, `NotoSansKR` 같은 이름의 TMP Font Asset이 있으면 우선 후보로 검토합니다. 외부 폰트를 다운로드하지 말고, 이미 프로젝트 안에 있는 TMP Font Asset만 연결합니다.

## 12. Ready / 승리! 스타일 확인
`ReadyLarge`는 큰 네이비 글자에 흰색 Outline과 약한 Shadow가 있어야 Project ONE 버튼 위에서 잘 보입니다. `ResultTitle`의 `승리!`는 Black weight와 약한 Shadow로 결과 카드 위 타이틀처럼 보이도록 설정되어 있습니다.

## 13. 한글이 네모로 보일 때
TMP Font Asset에 한글 glyph가 없으면 `Ready`, `ROOM CODE` 같은 영문은 보여도 한글이 네모로 표시될 수 있습니다. 이 경우 `ProjectOneTextStyleSet.asset`의 `defaultFont`에 한국어 glyph가 포함된 TMP Font Asset을 연결해야 합니다.

## 14. StyleSet 수정 후 다시 확인
`ProjectOneTextStyleSet.asset`에서 폰트나 크기를 바꾼 뒤 `Assets/ProjectONE/UI/Scenes/UI_TextStyle_Test.unity`를 열어 `1536 x 864` 기준으로 확인합니다. 필요한 경우 Unity 메뉴 `Project ONE > UI > Create Text Style Setup`을 실행해 테스트용 프리팹과 테스트 씬을 다시 생성합니다.

## 생성 도구
Unity 메뉴 `Project ONE > UI > Create Text Style Setup`을 실행하면 TMP Essential Resources 확인, TMP Font Asset 검색, 기본 스타일셋 생성, 텍스트 프리팹 생성, `UI_TextStyle_Test.unity` 생성, 폰트 후보 CSV 생성, README 생성을 한 번에 수행합니다.
