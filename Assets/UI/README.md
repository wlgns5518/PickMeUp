# Neon HUD UI Kit — Unity 사용 가이드

## 설치
1. Unity 프로젝트에 `Assets/NeonUIKit/` 폴더를 만들고 이 폴더의 내용을 그대로 복사합니다.
   - `Assets/NeonUIKit/Sprites/*.png`
   - `Assets/NeonUIKit/Editor/NeonUIKitImportSettings.cs`
   - `Assets/NeonUIKit/Scripts/OrnateMessageBox.cs`, `MessageBoxDemo.cs` (TextMeshPro 필요)
2. Unity가 리임포트하면 에디터 스크립트가 모든 스프라이트에 Sprite 타입 · 알파 투명 · 9-슬라이스 보더를 자동 적용합니다.
   - 자동 적용이 필요 없으면 `Editor` 폴더만 지우고 인스펙터에서 직접 설정하세요.
3. 스프라이트를 쓸 Image 컴포넌트는 **Image Type = Sliced**, 스트레치 안 하는 pip/ring은 **Simple** 로 둡니다.

## 스프라이트 목록

| 파일 | 크기 | 용도 | Image Type | 보더 (L,B,R,T) |
|---|---|---|---|---|
| `btn_primary.png` | 400×112 | 주요 버튼 (밝은 그라디언트) | Sliced | 44,44,44,44 |
| `btn_primary_glow.png` | 440×152 | 주요 버튼 + 외곽 글로우 | Sliced | 72,72,72,72 |
| `btn_secondary.png` | 400×112 | 보조 버튼 (네온 아웃라인) | Sliced | 36,36,36,36 |
| `btn_ghost.png` | 400×112 | 3차 버튼 / 취소 | Sliced | 36,36,36,36 |
| `btn_danger.png` | 400×112 | 경고·이탈 버튼 | Sliced | 36,36,36,36 |
| `btn_disabled.png` | 400×112 | 비활성 상태 | Sliced | 36,36,36,36 |
| `input_idle.png` | 400×112 | 입력 필드 기본 | Sliced | 40,40,40,40 |
| `input_focus.png` | 400×112 | 입력 필드 포커스 | Sliced | 40,40,40,40 |
| `input_error.png` | 400×112 | 입력 필드 오류 | Sliced | 40,40,40,40 |
| `icon_btn.png` | 144×144 | 정사각 아이콘/스킬 버튼 | Sliced | 36,36,36,36 |
| `panel.png` | 560×400 | 모달·패널 배경 | Sliced | 62,62,62,62 |
| `gauge_track.png` | 400×64 | 게이지 트랙 (빈 바) | Sliced | 30,30,10,10 |
| `gauge_fill_hp.png` | 400×64 | HP 채움 (레드-핑크) | Sliced (Filled 가능) | 8,0,8,0 |
| `gauge_fill_mp.png` | 400×64 | MP 채움 (퍼플) | Sliced | 8,0,8,0 |
| `gauge_fill_xp.png` | 400×64 | XP 채움 (라벤더) | Sliced | 8,0,8,0 |
| `pip_on.png` | 64×64 | 스태미나 세그먼트 ON | Simple | — |
| `pip_off.png` | 64×64 | 스태미나 세그먼트 OFF | Simple | — |
| `divider.png` | 400×32 | 섹션 구분선 (페이드) | Sliced | 8,0,8,0 |
| `ring_radial.png` | 320×320 | 원형 게이지 링 | **Filled / Radial 360** | — |
| `namebar_frame.png` | 원본 | 다크 테마 네임바 프레임 | Sliced | 120,60,220,60 |
| `namebar_frame_light.png` | 원본 | 라이트 테마 프레임 (업로드 원본) | Sliced | 120,60,220,60 |

모든 PNG는 투명 배경이며, 글로우가 잘리지 않게 여백(padding)을 포함해 캡처했습니다.

## 장식 메시지 박스 (msgbox_*)
글자 길이에 따라 가로·세로가 자동으로 늘어나는 장식 프레임 박스입니다.

| 파일 | 크기 | 역할 |
|---|---|---|
| `msgbox_corner_tl/tr/bl/br.png` | 90×90 | 모서리 장식 — **크기 고정** |
| `msgbox_edge_top/bottom.png` | 8×22 | 상·하 테두리 — 가로로만 늘림 |
| `msgbox_edge_left/right.png` | 22×8 | 좌·우 테두리 — 세로로만 늘림 |
| `msgbox_frame.png` | 240×240 | 위 조각을 합친 9-슬라이스 한 장 (Border 110) |
| `msgbox_panel.png` | 256×256 | 안쪽 검은 패널 (Sliced, Border 40) |

### 바로 쓰는 방법 (스크립트)
1. Canvas 아래 빈 UI 오브젝트를 만들고 `OrnateMessageBox` 를 붙입니다.
2. 인스펙터에 스프라이트 9장을 지정합니다 — Corner TL/TR/BL/BR, Edge Top/Bottom/Left/Right, Panel(`msgbox_panel.png`).
3. TMP Font Asset(한국어 폰트)과 Max Text Width(기본 560)를 지정합니다.
4. 코드에서 호출:
```csharp
box.SetMessage("몰몬트(★★)가 여신의 품으로 돌아갔습니다.\n그의 투지는 영원히 기억될 것입니다.");
```
계층은 Awake에서 자동 생성되고, `SetMessage` 마다 TMP의 Preferred Size로 박스 가로·세로를 다시 계산합니다.
`MessageBoxDemo.cs` 를 함께 붙이면 클릭/스페이스로 1줄·2줄·5줄 샘플을 순환하며 확인할 수 있습니다.

### 직접 조립할 경우의 계층 구조:
```
MessageBox            RectTransform + Vertical Layout Group + Content Size Fitter
├─ Panel             검은 패널 Image (inset 7px), 프레임보다 아래
├─ Text (TMP)        Auto Size 끄기 / Wrapping 켜기 / 가운데 정렬
└─ Frame             빈 오브젝트, Raycast Target 끄기
   ├─ Corner_TL/TR/BL/BR   90×90 고정 앵커
   └─ Edge_T/B/L/R         코너 사이를 채우는 스트레치 앵커
```
- Content Size Fitter: Horizontal/Vertical Fit = **Preferred Size**
- Layout Group Padding: 좌우 **95**, 상하 **80** (원본 비율)
- 최대 폭은 Text에 Layout Element → Preferred Width 약 **560** 으로 제한
- 한국어 줄바꿈은 TMP의 Word Wrap + `Kerning` 켜고, 어절 중간에서 끊기면 `Word Wrapping` 옵션 대신 CJK 전용 폰트 에셋을 사용하세요.
- 프레임 글로우는 Frame 오브젝트에 Additive 머티리얼 또는 Bloom 포스트프로세싱으로 재현합니다.
- 한 장(`msgbox_frame.png`)으로 쓸 경우: Image Type **Sliced**, Border **110,110,110,110**, Fill Center **끄기**.

## 게이지 만드는 법
- **가로 바**: `gauge_track` 을 배경 Image로, 그 자식에 `gauge_fill_hp` Image → Image Type **Filled**, Fill Method **Horizontal**, Fill Origin **Left**. 코드에서 `fillAmount = hp / maxHp`.
- **원형**: `ring_radial` → Image Type **Filled**, Fill Method **Radial 360**, Fill Origin **Top**, Clockwise 체크.
- **세그먼트**: Horizontal Layout Group + `pip_on` / `pip_off` 스왑.

## 색상 토큰

| 이름 | HEX |
|---|---|
| Accent (라벤더) | `#CBA6FF` |
| Accent Light | `#F0E4FF` |
| Accent Deep | `#8F6BFF` |
| Danger | `#FF5F7E` |
| BG Deep | `#05040A` |
| BG Panel | `#0C0818` |
| Text Primary | `#ECE5F7` |
| Text Muted | `#8B81A3` |

## 폰트
원본 디자인은 Orbitron (숫자·영문 헤드라인) + Noto Sans KR (한국어 본문) + JetBrains Mono (라벨) 조합입니다.
TextMeshPro Font Asset으로 구워서 쓰세요. 한국어는 Noto Sans KR Bold, 슬라이스 수치 대비 최소 24px 이상 권장.

## 참고
- 스프라이트는 400px 기준 1x 캡처입니다. 모바일 2x/3x 해상도가 필요하면 말씀해 주세요 — 2배 크기로 다시 내보내 드립니다.
- 배치 예시와 상태 전환은 프로젝트의 `Game HUD Kit.dc.html` 에서 확인할 수 있습니다.
