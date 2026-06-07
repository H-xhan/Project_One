Project ONE - Stamina Arc Gauge UI v2

이 패키지는 업로드한 스태미나 게이지 배경/초록 게이지 이미지를 Unity UI에서 바로 쓸 수 있도록 정리한 버전입니다.
- 체크무늬 배경 제거
- 배경 프레임과 초록 Fill 분리
- 초록 Fill이 배경 이미지의 파란 영역만 덮도록 aligned 버전 생성
- 캐릭터 옆에 따라다니는 UI용 C# 스크립트 포함

추천 사용 파일:
Sprites/CroppedAligned/StaminaArc_background_empty_aligned.png
Sprites/CroppedAligned/StaminaArc_fill_green_aligned.png

옵션 파일:
Sprites/CroppedAligned/StaminaArc_fill_green_noDecor_aligned.png  // 별/점 장식 없는 순수 Fill
Sprites/CroppedAligned/StaminaArc_fill_mask_aligned.png           // Fill 알파 마스크 확인용
Sprites/FullCanvas/*                                             // 원본 1254x1254 위치 유지 버전
Sprites/Preview/*                                                // 미리보기

Unity Import Settings:
Texture Type: Sprite (2D and UI)
Sprite Mode: Single
Alpha Is Transparency: On
Compression: None
Filter Mode: Bilinear
Mesh Type: Full Rect
Pixels Per Unit: 100

권장 Hierarchy:
Canvas
└── StaminaGaugeRoot
    ├── BackgroundImage        // StaminaArc_background_empty_aligned.png
    └── FillMask               // RectMask2D 컴포넌트 추가
        └── FillImage          // StaminaArc_fill_green_aligned.png

중요:
- BackgroundImage와 FillImage는 같은 크기와 같은 위치여야 합니다.
- FillMask는 FillImage를 감싸는 부모입니다.
- ProjectOneStaminaArcGaugeUI.cs의 fillMask / fillImage에 연결 후 SetValue(0~1)를 호출하면 됩니다.
- 캐릭터 옆에 따라다니게 하려면 StaminaGaugeRoot에 ProjectOneWorldUIFollowTarget.cs를 붙이고 target에 캐릭터 Transform을 연결하세요.
