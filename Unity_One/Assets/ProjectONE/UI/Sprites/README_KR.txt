Project ONE UI Cleaned Unity Pack

정리 내용
- 업로드된 129개 이미지 중 고확신 중복/근접 중복 17개를 제거했습니다.
- 남은 112개 이미지를 Unity에서 쓰기 좋은 폴더 구조로 분류했습니다.
- 모든 결과 PNG는 RGBA 알파 채널로 저장했습니다.
- 원본 이미지에 박혀 있던 밝은 체크무늬 배경은 자동으로 투명 처리했고, 이미지 외곽 여백은 잘라냈습니다.

Unity 권장 Import 설정
- Texture Type: Sprite (2D and UI)
- Sprite Mode: Single
- Alpha Is Transparency: On
- Compression: None 또는 High Quality
- Filter Mode: Bilinear
- Mesh Type: Full Rect

폴더 설명
- Buttons: 버튼류
- Panels: 범용 패널/라벨
- HUD: 게임 HUD 구성요소
- MainMenu: 메인 메뉴용 UI
- Logo_Title: 로고/타이틀 계열
- Lobby_CharacterSelect: 로비/캐릭터 선택 UI
- Result_Ranking: 결과창/랭킹 UI
- Icons: 단독 아이콘
- Decorations: 테이프/클립/별 등 장식
- Characters: 햄스터 캐릭터/얼굴
- ColorChips: 컬러칩 5종

검수 참고
- _manifest/kept_files.csv: 유지된 파일 목록과 원본 인덱스
- _manifest/removed_duplicates.csv: 제거된 중복 파일 목록
- _preview/contact_sheet_cleaned.png: 최종 파일 미리보기

주의
- 자동 배경 제거 특성상 아주 밝은 크림색 가장자리/그림자는 Unity에서 한 번 확인하는 것을 권장합니다.
