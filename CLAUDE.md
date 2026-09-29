# DEFRAG (DeFrag_HDRP) — Claude 작업 인수인계

@AGENTS.md

이 저장소는 한 사용자가 **메인 PC와 노트북**을 오가며 Claude Code로 작업합니다. Claude 세션은 기기 간에 공유되지 않으므로, **이 파일이 두 컴퓨터의 Claude가 소통하는 유일한 채널**입니다. 팀원들도 같은 저장소를 쓰므로 개인 정보나 비밀 값은 적지 마세요.

## 세션 프로토콜 (반드시 지킬 것)

**시작할 때**
1. 이 파일 전체, 특히 `현재 상태`와 `작업 로그`의 최신 항목을 읽는다.
2. `git log --oneline -10`과 `git status`로 다른 기기나 팀원의 변경을 확인한다. pull·커밋은 사용자가 GitHub Desktop으로 하므로, 요청이 없으면 직접 하지 않는다.
3. 이 PC에 `.mcp.json`이 없으면 아래 `기기별 환경`을 보고 사용자에게 안내한다. 이 파일은 gitignore 대상이라 기기마다 따로 필요하다.

**끝날 때 (또는 큰 작업 단위를 마칠 때)**
1. `작업 로그` 맨 위에 항목을 추가한다: 날짜(YYYY-MM-DD), 기기(메인 PC/노트북), 한 일, 검증한 것, 검증 못 한 것.
2. `현재 상태`의 미완료·주의 목록을 갱신한다. 끝난 항목은 지우고 새로 생긴 항목은 추가한다.
3. 설계 결정이나 함정을 새로 알게 됐으면 해당 섹션에 반영한다. 로그만 늘리지 말고 요약도 최신으로 유지한다.
4. 사용자에게 씬 저장(Ctrl+S)과 커밋할 파일 목록을 알려준다.

## 프로젝트 개요

- **게임:** 2인 협동 호러. 졸업 전시용이며, 관람객은 약 5분짜리 "킬링 파트"(발전기 B → 탈출)를 플레이한다.
  - 처음 하는 관람객 기준 난이도로 맞춘다.
  - 모든 미니게임은 역할별 튜토리얼을 플레이어마다 1번만 보여준다 (실패해도 다시 뜨지 않음).
- **B1F 퀘스트 흐름:** 장비 → 관제실 → 배전함 A → Connect Server → 다운로드 → (탈출 시퀀스 인터럽트) → 발전기 B → 다운로드 재개 → 탈출.
- **디버그:** MainLobby를 거쳐 B1F에 들어가며, `B1F Story Debug Checkpoint`의 **Start Checkpoint** 드롭다운을 쓴다.
  - *After Distribution Box A*: 배전함 A 앞. *After Connect Server*: 관제실 안(탈출 시퀀스 시작). *After Generator B*: 발전기 B 앞(가동 직후, 전력 복구 → 다운로드 재개, 몬스터가 발전기로 옴).
  - *Give Role Items*: 해킹패드와 카메라를 호스트/클라이언트에게 랜덤으로 1번 칸에 넣는다 (혼자면 둘 다).
  - 리스폰 위치는 `PlayerSpawnPoints`(GameplaySpawnPointRegistry)의 Checkpoint 1~3 Transform이며, 사망 후 체크포인트 리스폰도 같은 지점을 쓴다.
- **엔진:**
  - Unity 6000.4.7f1, HDRP 17.4, Netcode for GameObjects 2.13, 새 Input System (레거시 폴백 있음).
  - Cinemachine과 Timeline도 사용한다.
- **저장소:** GitHub `Work-hard-game-harder/DeFrag_HDRP`, Git LFS 사용. 사용자는 GitHub Desktop으로 커밋한다.

## 절대 규칙 / 제약

- **네트워크 구조 보존 (AGENTS.md):**
  - 공유 상태는 서버 권한으로 둔다.
  - 카메라·오버레이·입력 같은 표현은 소유한 로컬 플레이어에서만 실행한다.
  - 네트워크 팀원의 코드는 좁은 어댑터나 이벤트로만 연결한다.
- **탈출 시퀀스 (`B1F Escape Sequence`, Astra 설계):** 컷씬마다 담당자가 있다.
  - 컷씬0 경고(혜준), 컷씬1 접근(영주), 컷씬2 문 파손 간접연출1(서연), 컷씬3 문 파손 2(**은서 = 이 사용자**), 비상구(탈출).
  - Claude는 사용자 몫인 **문 파손 2(BreachVideo 단계)만** 작업했다. 다른 단계는 사용자가 지시하기 전에는 손대지 않는다.
- **MPPM 클론 에디터(Player 2 등)에는 절대 연결하지 않는다.** 클론은 사용자의 디버깅 전용이다. Unity MCP는 항상 메인 에디터로 고정(`--project-path`)해서 쓴다.
- **씬 저장:** 사용자가 직접 한다. MCP로 씬을 바꿨으면 저장하라고 알린다.
- **Artlist 크레딧:**
  - 추가 제작 예산은 1000크레딧이고, 그 이상은 사용자 승인이 필요하다.
  - 생성 전에는 반드시 `get_generation_cost`로 견적을 받는다.
  - 음악 견적: Lyria 3 Pro 300, Lyria 3 150 (30초 기준).
- **보안 (다시 시도하지 말 것):**
  - `~/.claude.json`은 자격 증명 파일이라 읽지 않는다.
  - 파일로 에디터 코드를 실행하는 브리지는 만들지 않는다.
  - `claude_desktop_config.json`은 MCP 설정 파일이 아니다.

## 기기별 환경 (각 PC마다 한 번씩)

프로젝트 루트에 `.mcp.json`을 만든다 (gitignore 대상).

```json
{
  "mcpServers": {
    "artlist": { "type": "http", "url": "https://mcp.artlist.io/mcp" },
    "unity-mcp": {
      "type": "stdio",
      "command": "C:\\Users\\<사용자>\\.unity\\relay\\relay_win.exe",
      "args": ["--mcp", "--project-path", "<이 PC의 프로젝트 경로>"]
    }
  }
}
```

- **Artlist:** 처음 연결할 때 OAuth 로그인이 필요하다.
- **Unity MCP:**
  - Unity 에디터가 켜져 있어야 하고, 연결 확인은 AI Assistant 창의 Connected Clients에서 한다.
  - 도구 이름은 `mcp__unity-mcp__Unity_*`다.
  - 오픈소스 CoplayDev unity-mcp 패키지도 설치돼 있다.
  - **`--project-path`를 꼭 넣는다.**
    - 빠지면 relay가 먼저 찾은 에디터에 붙는다. Multiplayer Play Mode의 가상 플레이어 클론(`Library/VP/mppm...`)이 그 대상이 될 수 있다.
    - 그러면 메인 에디터의 Connected Clients에 보이지 않고, 스크립트 수정이나 컴파일도 메인에 반영되지 않는다.
    - 연결된 곳은 `Unity_ManageEditor GetProjectRoot`로 확인한다.
    - 경로 없는 relay가 떠 있으면, MPPM Player 2 클론이 켜질 때 그 클론에 붙으려고 한다. 클론은 MCP 연결 승인 창을 지원하지 않아서 그대로 꺼진다. (2026-09-29 메인 PC에서 사용자 범위 `unity-mcp`를 제거해 해결)
  - 세션 도중에 `.mcp.json`을 고쳤으면 새 세션부터 적용된다.
    - 그 사이에는 `Temp/ClaudeMcp/umcp.py`(프로젝트 경로를 고정한 relay 호출 스크립트)를 쓴다.
    - `Temp/`는 git에 올라가지 않으므로, 없으면 새로 만든다.
- **효과음 합성(파이썬, 외부 패키지 불필요):** `python Tools/Claude/gen_locker_sfx.py <출력폴더>`
  - 락커 끼익·닫힘·강제 개방·노크, 아날로그 TV 잡음, 심장 박동을 만든다.
- **컴파일 확인:** `node Tools/Claude/compile_check.js`
  - Unity에 포함된 Roslyn으로 Assembly-CSharp를 에디터 밖에서 컴파일한다.
  - NGO IL 후처리는 검증하지 않는다.
  - Unity가 기본 Hub 경로가 아니면 `UNITY_EDITOR_DATA`를 지정한다.
- **효과음·텍스처 재생성:** `node Tools/Claude/gen_breach_sfx.js <출력폴더>`, `node Tools/Claude/gen_textures.js <출력폴더>`

## Unity MCP / 검증 요령 (함정 모음)

**`Unity_RunCommand` 제약**
- 클래스 이름은 `CommandScript : IRunCommand`여야 한다.
- `System.Reflection`은 쓸 수 없다.
- `Mesh`는 `UnityEngine.Mesh`로 적는다 (네임스페이스 충돌).
- `GameObject.Find`는 비활성 오브젝트를 못 찾는다. 부모의 `transform.Find`를 쓴다.
- `AudioLowPassFilter`는 AudioSource를 먼저 붙인 뒤에 추가한다.

**컴파일·리로드**
- 스크립트를 수정하면 도메인 리로드 중에 "Unity not detected"가 난다.
- `%LOCALAPPDATA%/Unity/Editor/Editor.log`에서 `Domain Reload Profiling`을 기다리거나, `Library/ScriptAssemblies/Assembly-CSharp.dll`의 수정 시각을 확인한다.
- Unity가 포커스를 잃으면 자동 컴파일이 늦어질 수 있다.

**렌더 확인**
- 에디터 모드에서 임시 Camera와 RenderTexture로 PNG를 찍을 수 있다 (HDRP도 됨). 노출이 실제보다 밝게 나온다.
- 플레이 모드에서는 `ScreenCapture.CaptureScreenshot`으로 오버레이 UI까지 찍힌다.

**플레이 모드 미리보기**
- 네트워크 없이 B1F를 틀면 `B1FPowerController`가 PowerOff라 방이 깜깜하다.
- `Power/EmergencyPower`를 켜고 `PowerOff`를 끈 뒤 확인한다.
- `Time.timeScale`을 낮추고 명령을 여러 번 보내 원하는 시점을 찍는다.

**UI와 폰트**
- DungGeunMo SDF에 없는 글자: ▲ ▼ ▶ □ · ✓ ✗ × ↔
- 쓸 수 있는 글자: ● ○ ■ • ← → ↑ ↓ █ ★
- TMP의 `<alpha>` 태그는 글자 그대로 출력된다. `<color=#RRGGBBAA>`를 쓴다.

**HDRP 파티클**
- `Sprites/Default`와 `Legacy Shaders/Particles/Additive`는 LightMode가 없어서 렌더된다.
- 대신 조명을 받지 않으므로, 컷씬에서는 MaterialPropertyBlock의 `_Color`로 장면에 맞게 틴트한다.

## Claude가 만든 시스템 지도

**공통 UI·사운드**
- `Assets/Resources/DefragUiTheme.asset`과 `Scripts/UI/DefragUiTheme.cs`: 색·폰트 테마.
- `Scripts/UI/RuntimeUi.cs`: 코드로 UI를 만드는 빌더 (FramedPanel, KeyCap, Scanlines 등).
- `Scripts/UI/MinigameTutorial.cs`: 플레이어별 1회 튜토리얼 (`ShowBlocking`, `ShowBlockingOverlay`, `ShowFloating`). 네트워크 세션이 끝나면 초기화된다.
- `Scripts/Audio/UiSfx.cs` (`UiCue` 약 36종)와 `Scripts/Audio/ProceduralSfx.cs`: 코드로 합성하는 효과음.
- `Scripts/UI/FacilityRadarView.cs`, `FacilityBlueprint.cs`, `RuntimeUiSprites.cs`: 레이더와 도면.

**발전기 B**
- `GeneratorBController`(서버 로직), `GeneratorBCrankSimulation`, `GeneratorBPanelView`, `GeneratorBCrankView`, `GeneratorBLocalSession`, `GeneratorBRadarContent`, `GeneratorBToast`.
- 에셋: 크랭크 프롭 `Prefabs/B1F/GeneratorCrank/`, BGM과 PA 음성 `SoundSources/B1F/`.

**터미널**
- `TerminalScreenController` (메뉴 재디자인).
- `PasswordCrackingMinigame` (리듬, A·S·D·F): 5번까지 실패 허용.
- `MemoryAddressRecoveryMinigame` (주소 찾기): 주소 3개, 틀려도 벌점 없음.
- `DownloadDataTypingMinigame`, `CooperativeTerminalHintRelay`.

**Connect Server**
- 해커 쪽: `ConnectServerMinigame`, `ConnectServerCircuitView` (목표 모양은 숨김, 틀리거나 25초가 지나면 윤곽 표시), `ConnectServerRadarContent`.
- 카메라 담당 쪽: `ConnectServerPartnerHud`.

**배전함 A**
- `DistributionMonitorMinigame`: 터미널 25번의 CIRCUIT MONITOR, 프리팹 `Prefabs/Hacking/DistributionMonitorMinigame.prefab`.
- `DistributionOperatorHud`, 그리고 `DistributionBoxController` / `DistributionBoxLocalSession`의 모니터 연동.

**문 파손 2 실시간 컷씬** (`Scripts/B1F/Cutscene/`)

*탈출 시퀀스 연결*
- `B1FStoryCutscene` (추상 클래스): 로컬 전용 실시간 컷씬.
- `B1FEscapeSequence`에 단계별 슬롯 `warning/approach/impact/breach/exitCutscene`를 추가했다.
  - 지정하면 해당 단계에서 영상 대신 실시간 컷씬이 재생되고, 서버 타임아웃은 컷씬 `Duration`을 쓴다.
  - 비어 있으면 기존처럼 VideoClip이 재생된다.
- `B1FEscapePresentation`: 컷씬을 재생하고, 끝나면 검은 화면에 "WAITING FOR PARTNER..."를 띄운다.

*컷씬 구성 요소*
- `B1FDoorBreachCutscene` (연출 총괄): 타이밍, 샷, 배우, 조명, 이펙트, 오디오를 인스펙터에서 편집한다.
  - 씬 오브젝트는 `B1F Door Breach Cutscene`이고, 샷과 위치는 하위 `Shots/`, `Marks/`의 Transform이다.
- `LocalCutsceneStage`: 카메라 전환, HUD와 플레이어 아바타 숨김 및 복원.
- `CutsceneActor`: 대역 캐릭터. 걷기, 고개 돌리기, 움찔 등을 처리한다.
- `CutsceneCameraRig`: 샷 이동, 핸드헬드 흔들림, 충격 셰이크, 카메라 쓰러짐.
- `CutsceneOverlay`: 레터박스, CCTV 화면, 노이즈, 글리치, 렌즈 먼지, 페이드.
- `DoorBreachDebris`: 로컬 물리 파편. 끝나면 고정되고 충돌이 꺼진다.

*문 상태*
- `BreachableDoor`는 관제실 3번 문(`New_Doors/Door_ver1_locked (관제실 3번)`)에 붙어 있다.
- NetworkObject를 끄지 않고 렌더러와 통로 충돌만 전환한다.
- 이전의 `intactDoor`(복제 문) 연결은 진짜 문과 겹치는 버그가 있어서 제거했다.

*에셋*
- 파티클 프리팹 7종: `Prefabs/B1F/Cutscene/`.
- 텍스처와 재질: `Art/B1F/Cutscene/`.
- 합성 효과음 14종: `SoundSources/B1F/Cutscene/`.
- 시퀀스의 `breachDust`는 `Breach Lingering Dust`, `breachSound`는 `Breach_DebrisSettle`이다.

*미리보기*
- 플레이 모드에서 컴포넌트를 우클릭하고 **Preview (Play Mode)**를 누르거나, `PlayPreview(시작시각)`를 호출한다.

**락커 숨기 + TV 몬스터 락커 수색** (락커 원본은 사용자 작업, 2026-09-28 확장)
- `LockerHiding` (서버 권한 점유):
  - 새 상태 `ForcedOpen`을 enum 끝에 추가했다.
  - 숨 참기와 헐떡임을 ServerRpc로 받는다. 헐떡이면 `WorldNoiseSystem`에 소음을 내고 `ServerOccupantGasped` 이벤트를 보낸다.
  - `TryForceOpenServer`로 문을 강제로 연다. 문 효과음(끼익, 쾅, 노크)은 복제된 상태 변화를 보고 모든 피어가 3D로 재생한다.
  - `MonsterStandPoint`, `DoorCenter`, `Outward`를 제공한다.
- `LockerLocalSession` (소유자 전용):
  - 마우스로 제한된 범위를 둘러본다 (좌우 ±38°, 위 20°, 아래 26°).
  - Space로 숨을 참는다 (6.5초). 다 쓰면 강제로 헐떡이고, 35%까지 회복되기 전에는 다시 참을 수 없다.
  - `ForcedOpen`이 되면 흔들리며 밖으로 끌려나온다.
- `LockerHidingPresentation` (소유자 전용):
  - 비네트, 숨 게이지, 키 안내(몬스터가 5m 안에 오면 "숨 참기!"가 깜빡임)를 띄운다.
  - 몬스터 거리와 의심도에 따라 심장 박동이 빨라지고, 숨을 참지 않으면 헐떡임이 들린다.
  - 몬스터가 내 락커를 노리면 긴장 BGM이 깔리고, 강제 개방 때는 글리치 점프스케어가 나온다.
- `MonsterLockerHunter` (TvMonster 프리팹, 서버 판정 + ClientRpc 연출). 단계는 Approach → Lurk → (ForceOpen):
  - **목격:** 추격 중인 대상이거나 12m 안의 시야에서 락커에 들어가면 곧장 온다. 의심도 55에서 시작한다.
  - **냄새:** 숨은 락커가 4.5m 안에 있으면 확인하러 온다. 의심도 15에서 시작한다.
  - **헐떡임:** 15m 안에서 들리면 온다. 의심도 45에서 시작하고, 수색 중에 헐떡이면 +60이다.
  - **의심도 변화:** 숨을 참지 않으면 초당 +14, 참으면 초당 -5. 100이 되면 0.6초 뒤 문을 뜯고 추격·공격으로 넘어간다.
  - **수색 시간:** 목격했을 때 9~12초, 냄새로 왔을 때 5~7초. 버티면 물러간다. 락커마다 20초 쿨다운이 있다.
  - **협동:** 숨지 않은 플레이어가 7m 시야에 들어오거나 긴급 소음(발전기 미끼 등)이 나면 수색을 멈추고 그쪽으로 간다.
  - **애니메이션:** TV 몬스터에 NetworkAnimator가 없어서, 수색 중 포즈는 이 컴포넌트가 클라이언트 애니메이터에 따로 복제한다.
- `MonsterAI`에는 좁은 API만 추가했다.
  - `TryBeginScriptedControl`, `SetScriptedPose`, `FaceScripted`, `EndScriptedControl`.
  - 락커 안 플레이어는 시각 대상에서 제외(`IsPlayerInLocker`)한다.
- **환기구 문:** 상단 메뉴 `DEFRAG > Locker > Build Vented Door Mesh`(에디터 스크립트 `Assets/Editor/LockerVentDoorBuilder.cs`).
  - `Locker.fbx`의 문 메시를 View Anchor 높이에서 62×42cm로 잘라내고, 루버 6개와 창틀을 붙인다.
  - 결과는 `Art/Locker/Door_Locker2_Vented.asset`이고, `Locker.prefab`에 연결했다.
  - 원본 FBX는 건드리지 않았다.
- **소리:** `SoundSources/B1F/Locker/`, `SoundSources/B1F/TvMonster/`.
  - Artlist 생성: 몬스터 그르렁·킁킁·숨소리·비명(MONSTER 효과), 한국어 라디오 속삭임 "거기… 있지? / 숨소리… 들려… / 나와… / 찾았다", 플레이어 헐떡임, Lyria 긴장 BGM(27.9초 루프).
  - 나머지는 `gen_locker_sfx.py`로 합성했다.

**기기 화면·카메라 연출** (2026-09-29)
- 공용 유틸리티:
  - `Scripts/Devices/OffscreenUiSurface`: 런타임 UI를 멀리 떨어진 전용 카메라로 RenderTexture에 그린다. 후처리를 끄고 프레임 수를 제한한다.
  - `Scripts/Devices/DeviceScreenQuad`: 모델 위에 발광 화면 판을 붙인다. 콜라이더는 없다.
  - 재질 템플릿은 `Resources/Devices/DeviceScreen.mat`(HDRP Lit, 발광 맵)이다.
- **해킹패드** `HackingPadScreen` (HackingPad 프리팹):
  - 화면 위치는 메시 좌표로 측정했다 (중심 (0.0134, 0.0131, -0.0284), 크기 0.150×0.101, +Y 방향).
  - 내 손(1인칭, 부모에 EquipmentController가 있음)에서는 H-PAD OS 실시간 UI를 12fps로 그린다.
    - 가장 가까운 터미널, 거리, 신호 막대, 파형, LINK READY(사거리 6m), 현재 목표를 보여준다.
    - TV 몬스터가 14m 안에 오면 화면이 흔들리고 "SIGNAL INTERFERENCE" 표시가 뜬다.
  - 바닥이나 상대 손에 있을 때는 공유 STANDBY 화면을 쓴다.
  - 청록 포인트 라이트와 발광이 맥동한다. NetworkWorldItem이 렌더러를 끄면 따라서 꺼진다.
- **카메라 들기(C)** `CameraViewSwitcher`:
  - 흐름: 들어 올리기 0.36초 → LCD가 눈을 향함 → 화면이 꽉 차는 거리(약 5cm)까지 가속 0.24초 → 전체 화면 카메라 뷰로 전환 → 뷰파인더 부팅.
  - 내릴 때는 이 과정을 역순으로 재생한다.
  - LCD 위치는 Camera(Item) 메시의 -X 면, 중심 (-0.0772, -0.0548, -0.011), 크기 0.215×0.154다.
  - 전환 중에는 ItemCam이 LCD용 RenderTexture에 그린다. 이때 손에 든 카메라 레이어는 촬영에서 뺀다.
  - 잠금, 장착 해제, 컷씬 상황에서는 `CancelToLowered`로 즉시 원래 상태로 돌아간다.
  - 손에 든 카메라가 옆으로 누운 이유: 장착 코드(`EquipmentController.Equip`)가 heldPrefab의 루트 회전(270°)을 (0,-180,0)으로 덮어쓰기 때문이다.
- **뷰파인더** `CameraViewfinderHud`:
  - 구성: 브래킷, 조준점, REC와 타임코드, 배터리 5칸, MODE NORMAL/IR NIGHT, 노출계, 키 안내, 셔터 섬광, IMG 번호, IR 녹색 톤, LOW BATTERY 경고.
  - 기존 Cam Canvas의 "focus image"와 "Battery UI"는 런타임에 숨긴다. `useViewfinderHud`를 끄면 기존 UI로 돌아간다.
- **터미널 접속 연출** `HackingSessionController`:
  - 카메라가 모니터 유리 정면으로 날아가며 FOV가 85%로 줄어든다 (0.55초).
  - 이어서 `TerminalCrtOverlay`로 CRT가 켜지는 효과(0.3초)와 함께 UI가 나타나고, 패널 뒤에 어두운 배경이 깔린다.
  - 나갈 때는 CRT가 꺼지는 효과(0.26초) 뒤 카메라가 원래 자리로 돌아온다 (0.42초).
  - `TerminalWorldScreenPresenter.TryGetScreenPose`: 기울어진 키오스크 유리도 가로축과 (세로+깊이) 대각선으로 법선을 계산한다.
- **동료 관전(미러)**:
  - `TerminalScreenStreamer`(조작자 전용): 4fps로 320×180 JPEG를 만들어 `CooperativeTerminalHintRelay`로 보낸다.
    - 조각 크기 3000B(UnityTransport MaxPayloadSize 6144 이하)로 나눠 서버를 거쳐 동료에게 전달한다.
    - 에디터에서는 `CaptureScreenshotAsTexture`, 빌드에서는 `CaptureScreenshotIntoRenderTexture`(상하 반전)로 캡처한다.
  - 받는 쪽에서는 `TerminalMirrorAssembler`가 조각을 다시 합치고, 모니터 캔버스에 "● LIVE // OPERATOR VIEW"로 띄운다. 1.5초 동안 프레임이 없으면 상태 화면으로 돌아간다.
  - 테스트용으로 에디터·개발 빌드 전용 `TerminalScreenStreamer.BeginLoopback`(내 모니터에 표시)이 있다.
- **월드 스크린 버그 수정**: 기존 모니터가 늘 검게 보였다.
  - 원인 1: 표시 카메라의 near clip이 0.3인데 캔버스가 0.1 거리에 있어 잘려 나갔다.
  - 원인 2: Awake에서 한 번만 렌더했다.
  - 수정: near를 0.02로 바꾸고, 후처리를 끄고, 시작 후 3프레임 동안 다시 렌더한다.
  - 키오스크 유리(ConnectionDevice_glass)는 UV가 세로라서 내용을 270° 돌려 그린다(`contentRotation`, -1이면 자동).
- **효과음:** `Resources/DeviceSfx/`의 Camera_Raise·PowerOn·Lower, Terminal_DiveIn·CrtOn·CrtOff (`Tools/Claude/gen_device_sfx.py`로 합성, Artlist 0크레딧).

**LobbyF 벽 모니터 영상** (2026-09-29, 사용자 코드 `MaterialVideoPlaylistPlayer`·`SetupLobbyFScreenVideosOnce` 확장)
- **원인:**
  - 화면 메시(Screen_A.*, 대형스크린)의 유리 UV는 텍스처 전체가 아니라 일부만 쓴다. 세로는 U 0~0.719·V 뒤집힘, 대형은 U 0~0.325에 90° 돌아감.
  - 그래서 세로는 오른쪽이 잘리고 가로로 늘어났고, 대형은 누운 띠만 보였다. `Monitor_glass가로5/6.mat`의 Base Map Tiling(-1.46, -1)은 이전 수동 보정 흔적이다.
  - 재생 멈춤: VideoPlayer의 `skipOnDrop=true`는 로딩 끊김 뒤 한 프레임에 멈춰 루프가 돌 때까지 안 풀렸다. 재생목록 클립 교체 직후 0프레임에 멈추는 현상도 있었다. 에디터 로그에는 "Unexpected timestamp…baseline profile" 경고가 떴다.
- **수정:**
  - `MaterialVideoPlaylistPlayer`에 `ScreenUvMapping[]`(메시 UV→화면 좌표 아핀 변환 + 실제 가로세로비)를 추가했다. 컴포넌트 우클릭 **Bake Screen UV Mappings**로 메시에서 측정한다.
  - `Hidden/DeFrag/VideoScreenFit`(`Assets/Shaders/HiddenVideoScreenFit.shader`)로 UV 배치별 텍스처에 영상을 똑바로, 원래 비율로 그린다. 남는 여백은 같은 프레임을 흐리게 깔아 채운다. `fitMode`(Fit/Fill/Stretch), `fillBlur`, `fillBrightness`로 조정한다.
  - 재질의 Tiling/Offset은 PropertyBlock으로 (1,1,0,0)으로 덮어쓴다. 재질 파일은 손대지 않았다.
  - `skipOnDrop=false`, 2.5초 동안 프레임이 멈추면 재시작하는 감시를 넣었다. LobbyF의 다른 VideoPlayer 2개(nexus 안내, OFFICE3 TV)도 skipOnDrop을 껐다.
  - **공유 디코딩(아틀라스):** `decodeSource` + `sourceRect`. 세로 영상 3~7을 3600×1280 한 파일에 나란히 넣고, Vertical Screen 3이 디코딩하면 4~7은 자기 칸만 가져다 쓴다. 디코더가 7개에서 3개로 줄었다.
  - 영상 파일은 `Assets/Movies/Screens1080/`에 새로 만들었다. 모두 H.264 **Baseline**(B-프레임 없음)이고 오디오는 없다.
    - `대형스크린_1-2_연속.mp4`: 1920×1080, 원본 1과 2를 이어 붙여 41초. 클립 교체가 없게 했다.
    - `세로스크린_아틀라스_3-7.mp4`: 칸당 720×1280, 60초. 각 영상을 반복해 채웠다.
  - 원본 4K HEVC(`Assets/Movies/대형스크린1/2`, `세로스크린3~7.mp4`, 약 200MB)는 더 이상 참조되지 않는다. 지워도 되지만 사용자 판단에 맡긴다.
  - 재생성: ffmpeg `-profile:v baseline -crf 20~21 -g 60 -an`, 아틀라스는 `-stream_loop -1` 입력 5개를 `hstack`하고 `-t 60`.
- **셋업:** `Tools/LobbyF/Setup Screen Videos` 메뉴 또는 LobbyF를 열면 자동 실행된다(SessionKey v6). **이 스크립트는 씬을 자동 저장한다.**
- **Nexus 홍보 영상으로 교체 (2026-09-29 노트북):** 위의 두 파일은 이제 참조되지 않는다.
  - 새 파일(H.264 Baseline, 30fps, 무음, 루프):
    - `Nexus_Wide_Promo.mp4`: 2464×800(대형 화면 비율 3.08). 40초, 아트리움 패닝 → 데이터 구체 HUD → 흰 엔드카드.
    - `Nexus_Portrait_Atlas.mp4`: 720×992 칸 5개(가로 화면 비율 약 0.72), 30초. 칸마다 6초씩 어긋나게 순환한다.
  - 플레이어 설정: `fitMode`를 Fill로 바꿨다. 영상 비율이 화면과 같아서 흐린 여백 없이 가장자리 1% 정도만 잘린다. `SetupLobbyFScreenVideosOnce`도 새 경로, 크기, Fill을 쓰도록 고쳤다.
  - 제작 방식: Artlist 정지 이미지 2장(4:1, 8256×2048)을 `Assets/Art/LobbyF/Screens/Source~/`에 두었다. `~` 폴더라 Unity가 임포트하지 않는다.
    - 에디터 스크립트 `Assets/Editor/NexusScreenPromoBaker.cs`(메뉴 **DEFRAG > LobbyF > Bake Nexus Screen Videos**)가 uGUI로 켄 번스 이동과 문구를 그려 `MediaEncoder`로 굽는다. ffmpeg가 필요 없고 약 2분 걸린다.
    - `RenderStills(folder, times)`로 원하는 시점의 PNG를 먼저 확인할 수 있다.
  - **세계관 (사용자 지시, 중요):**
    - 플레이어는 연구소의 비밀을 캐러 온 정부 비밀 요원이다. 가스마스크와 고양이귀 후드는 플레이어 디자인이라 연구소 홍보물에 쓰면 안 된다.
    - 연구소 직원은 3등신에 흰 연구복이나 흰 정장을 입고, 얼굴이 드러나지 않는 모브 캐릭터다.
    - 실제 사람이 나오는 사진풍은 쓰지 않는다. 게임 아트(로우폴리, 리썰 컴퍼니나 미메시스 느낌)에 맞춘다.
  - 함정:
    - HDRP 카메라는 새로 만든 뒤 처음 몇 프레임을 검게 그린다. 그래서 굽기 전에 8프레임을 워밍업한다.
    - UI를 그린 RenderTexture는 알파가 섞여 있다. PNG로 확인할 때는 알파를 255로 채워야 한다.
    - 4:1 패널 이미지의 흰 틈 좌표는 `Posters` 배열에 픽셀로 적혀 있다. 원본 이미지를 바꾸면 다시 측정해야 한다.

**LobbyF 엘리베이터 SF 터치 키패드** (2026-09-29, 사용자 코드 `ElevatorPanel` 확장)
- **사용자 방향 (중요):** SF 화이트 연구소 느낌의 전자식 패널이다. 위는 표시창, 아래는 글자 없는 터치 패드다. **입력은 키보드로만 받는다** (마우스 클릭 없음). 한 글자 칠 때마다 **손 모델이 터치 패드를 누르는** 애니메이션이 나온다. 아날로그 키캡 방식(첫 시안)은 사용자가 반려해 걷어냈다.
- **구조:**
  - `ElevatorPanel`(사용자 작성)이 정답 판정, 오답 경보(`ElevatorWrongCodeAlarm`), 씬 이동을 그대로 맡는다.
  - 새 필드 `keypad3D`가 지정되면 기존 `KeypadUIPanel` 대신 이 패널로 표시한다. 비우면 예전 UI로 돌아간다.
- **원화:** `Art/LobbyF/Keypad/Keypad_Concept.png` (Artlist Nano Banana 2, 90크레딧). Artlist 3D 생성은 이 계정에 없어서(model3d 목록이 비어 있음), 원화를 보고 형태를 코드로 만들었다.
- **`Scripts/Elevator/ElevatorKeypad3D`** (씬 오브젝트 `Lobby/Entrance/Elevator Keypad 3D`, 위치 (1.298, 2.135, 2.907), 앞(+Z)이 벽 바깥):
  - 엘리베이터 왼쪽 기둥의 모델 키패드 판(0.305×0.51m)을 덮는 0.32×0.60m 패널을 런타임에 만든다. 씬에는 빈 오브젝트 하나만 저장된다.
  - 구성: 은색 베젤, 광택 흰 셸, 양옆 시안 LED 줄, 표시창 유리, 터치 패드 유리, 지문 링. 둥근 판은 모두 `Devices/RoundedSlabMesh`로 만들고, 앞면 UV가 평면이라 화면 텍스처가 둥근 모서리까지 맞는다.
  - 표시창: `ElevatorKeypadDisplay` (640×436, 괄호 속 코드 6칸, SYSTEM 줄, 상태 줄. 사용 중 24fps, 대기 중 2fps).
  - 터치 패드: `KeypadTouchPad` (448×520, 반투명 그라데이션, 육각 격자, 숨쉬는 가운데 링, 터치 지점마다 퍼지는 파문).
  - 글자마다 패드 위 보이지 않는 4×5 격자의 고정 위치를 누른다. DEL은 왼쪽 아래, ENTER는 지문 링을 누른다.
  - 오답이면 칸별로 초록/빨강을 칠하고(판정은 그 칸 글자를 바꾸기 전까지 유지), LED·잠금 표시가 빨갛게 깜빡이며 빨간 파문과 표시창 흔들림이 나온다. 정답이면 초록으로 바뀌고 "ACCESS GRANTED"가 뜬다.
- **손:** `KeypadTouchHand` + `Art/LobbyF/Keypad/Keypad_Hand.prefab` (메시 `Keypad_Hand_Point.asset`).
  - 플레이어 캐릭터(0724_PlayerCharacter)의 오른쪽 아래팔(검은 정장 소매, 흰 커프)과 손을 포인팅 자세로 구워 만든 정적 메시다. 피벗은 검지 끝, +Z가 가리키는 방향이다.
  - 다시 만들 때는 메뉴 `DEFRAG > Keypad > Bake Pointing Hand` (`Assets/Editor/KeypadHandBaker.cs`)를 쓴다. 이 리그의 손가락 굽힘 축은 로컬 X다. 두 번째 손가락은 Ring 뼈에 붙어 있다.
  - 오른쪽 아래 화면 밖에서 들어와 패드 앞에서 대기하고, 누를 때 0.075초 만에 찍고(닿는 순간 파문과 소리) 돌아간다. 0.9초 동안 입력이 없으면 쉬는 자리로 물러난다. 빠르게 입력하면 앞 동작을 바로 끝내서 파문이 빠지지 않는다.
  - 팔은 아래·오른쪽으로 빠지게 기울여 잘린 소매 끝이 화면에 보이지 않게 했다. 가까이서 빠르게 움직여 모션 블러로 번지므로 모션 벡터는 끈다(ForceNoMotion).
- **`Scripts/Devices/DeviceFocusCamera`** (재사용 가능):
  - 플레이어 카메라를 장치 앞으로 날려 보내고(0.6초), 누를 때 기울임과 흔들림을 준 뒤, 원래 자리로 돌려놓는다(0.45초).
  - 켜져 있는 동안 PersonController를 끄고(WASD 입력 중 이동 방지), 카메라 아래 손에 든 아이템 렌더러를 숨긴다.
  - 씬이 바뀌어도(정답 → B1F) OnDisable/OnDestroy에서 즉시 복원한다.
- **기존 문제 참고:** 예전 UI는 PersonController를 끄지 않아서, 입력 중 W/A/S/D를 누르면 캐릭터가 움직일 수 있었다. 새 패널에서는 막힌다.
- **소리:** `UiSfx`의 KeyType(터치 위치에 따라 음높이), MenuBack(DEL), MenuConfirm(ENTER), AccessDenied, TaskSuccess, TerminalBoot/Close를 쓴다.

## 현재 상태 (최신화할 것)

- **진행 중: LobbyF 개선 (2026-09-29 시작, 메인 PC).** 작업량 제한으로 끊기면 새 세션은 이 목록에서 체크 안 된 항목부터 이어서 한다. 항목을 끝낼 때마다 체크하고, 작업 로그에 한 줄씩 남긴다.
  - Artlist 예산: 이 작업에만 3000크레딧까지 쓸 수 있다. 사용량은 아래 크레딧 장부에 기록한다.
  - **다음 세션은 b-1(단서 추가)부터 시작한다.** a-1/a-2는 아래 "LobbyF 엘리베이터 3D 키패드" 섹션 참고.
  - [x] a-1 엘리베이터 비밀번호 오답 시 자리별 색 표시 (맞은 글자 초록, 틀린 글자 빨강) (2026-09-29 노트북, 플레이 캡처로 확인)
  - [x] a-2 엘리베이터 패널을 세련된 3D 느낌으로 리디자인. 입력할 때 플레이어가 3D 키패드를 누르는 애니메이션 (2026-09-29 노트북, SF 화이트 터치 패널과 손 탭으로 재작업. 테스트 카메라로 확인했고, 실제 플레이어 흐름은 미검증)
  - [ ] b-1 단서 난이도 완화: 추가 단서 배치
  - [ ] b-2 OFFICE2 MONITOR 패널의 메모와 휴지통에 단서와 이스터에그 추가
  - [x] c 벽의 세로 모니터 영상 비율과 잘림 수정 (2026-09-29, 플레이 캡처로 확인)
  - [x] d 벽의 대형 가로 모니터 영상이 재생되지 않는 문제 수정 (2026-09-29, 플레이 캡처로 확인)
    - c·d 내용은 아래 "LobbyF 벽 모니터 영상" 섹션 참고. LobbyF 씬은 셋업 스크립트가 자동 저장했고, 이후 skipOnDrop 변경분은 사용자 저장이 필요하다.
  - [x] e 가로·대형 스크린 영상을 Nexus 홍보 영상으로 교체하고 화면 비율에 맞춤 (2026-09-29 노트북, 플레이 캡처로 확인. **LobbyF 씬 저장 필요**)

- **완료 (커밋 1caa575까지):**
  - 발전기 B 개편.
  - 모든 미니게임과 터미널 UI 개편, 1회 튜토리얼, 효과음.
  - Connect Server와 배전함 A 협동 방식 개편.
  - 문 파손 2 실시간 컷씬 (약 22.6초).
- **B1F 디버그 체크포인트 개편 (2026-09-28, 미커밋·미검증):** 아래 작업 로그 참고. 2인 플레이로 세 체크포인트를 각각 확인해야 한다.
- **락커 숨기 긴장감 개편 (2026-09-28, 미커밋·플레이 미검증):**
  - 2인 플레이로 확인할 것: 목격 후 락커 수색, 숨 참기와 의심도, 강제 개방 후 공격, 동료 미끼로 주의 돌리기, 클라이언트 쪽 몬스터 포즈와 소리.
  - 사용자가 만든 플레이어 락커 입·출입 모션이 아직 임포트되지 않았다.
    - 플레이어 Animator에 `Base Layer.LockerEnter`, `LockerHidden`, `LockerExit` 상태로 넣으면 원격 플레이어에게 자동으로 재생된다.
    - 강제 개방도 현재 `LockerExit`를 재생한다.
- **기기 화면·카메라 연출 (2026-09-29, 미커밋):**
  - 플레이 모드에서 확인한 것 (네트워크 없이 테스트 리그 사용):
    - 해킹패드 화면(1인칭 OS, 바닥 STANDBY).
    - 터미널 접속과 복귀. 카메라 위치·FOV, 입력 잠금이 원래대로 복원됐다.
    - 키오스크 대기 화면이 똑바로 나온다.
    - 루프백 미러가 동작한다.
  - 카메라 C 연출은 실제 장착 흐름으로는 확인하지 못했다. LCD 자세, 화면 채우기, HUD만 따로 캡처했다.
  - 2인 네트워크 미러 전송도 아직 확인하지 못했다.
- **미검증:** 실제 2인 네트워크 플레이.
  - 컷씬: 양쪽 화면에 재생되는지, 끝나고 조작이 복구되는지, 파손된 문을 통과할 수 있는지.
  - 미니게임: 튜토리얼이 1회만 뜨는지, 모니터와 조작자 동기화가 되는지.
- **주의 사항:**
  - 파편은 각 클라이언트에만 있는 장식이라 두 화면에서 위치가 다를 수 있다.
  - `LocalCutsceneStage`의 HUD 숨김 로직은 팀원 코드 `B1FMonsterSpawnTimeline`과 비슷하다. 공용화는 팀원과 상의해야 한다.
  - TV 몬스터 프리팹에 NetworkAnimator가 없어서, 평상시 이동과 추격 애니메이션이 클라이언트에 동기화되지 않는 것으로 보인다.
    - 락커 수색 포즈만 `MonsterLockerHunter`가 따로 복제한다.
    - 근본 수정은 몬스터 담당 팀원과 상의해야 한다.
  - `Assets/Prefabs/Lockers(w.text).fbx`는 사용자가 새로 넣은 미추적 파일이다.
    - 락커 모델을 이것으로 바꾸면 `LockerVentDoorBuilder`의 원본 경로를 수정한 뒤 다시 실행해야 한다.
- **다음 후보:**
  - 2인 테스트 결과에 따라 연출 타이밍이나 난이도 조정.
  - 사용자가 지시하면 다른 탈출 단계도 실시간 컷씬 슬롯으로 연결 가능.

## Artlist 크레딧 장부

- **누적 사용:** 1853 (플랜 16,500 중).
- **LobbyF 개선 (별도 예산 3000 중 1270):**
  - 엘리베이터 키패드 원화 1장(Nano Banana 2, 90).
  - 벽 스크린 이미지 1180:
    - 사진풍 2장(90×2). 실제 사람이 나와서 폐기했다.
    - 게임 캐릭터 레퍼런스를 넣은 이미지-투-이미지 3장(200×3). 가스마스크 캐릭터가 나와서 폐기했다.
    - 얼굴 없는 직원으로 고친 편집 2장(200×2). 최종본이다.
  - 참고: 텍스트-투-이미지는 90, 레퍼런스를 넣은 이미지-투-이미지는 200이다. 영상 생성은 5초에 175~500이라 쓰지 않았다.
- **락커·TV 몬스터 (2026-09-28, 별도 예산 500 중 266):**
  - 몬스터 음성 묶음(MONSTER) 53, 라디오 속삭임(VINTAGE_RADIO, 한국어) 24, 플레이어 헐떡임 39, Lyria 3 긴장 BGM 150.
  - 음성은 ElevenLabs v3를 썼다 (몬스터는 voice "Shadow", 플레이어는 "Mild").
- **추가 예산 사용:** 1000 중 17. 발전기 B BGM `GeneratorColdStart_Loop`와 PA 음성 3종에 썼다.
- **문 파손 2:** 0크레딧 (효과음은 코드로 합성).

## 작업 로그 (최신이 위)

### 2026-09-29 · 노트북 · LobbyF 가로·대형 스크린 Nexus 홍보 영상
- **한 일:** 위 "Nexus 홍보 영상으로 교체" 참고.
  - Artlist 이미지로 두 화면의 영상을 새로 구웠다. 새 에디터 스크립트는 `NexusScreenPromoBaker`이다.
  - `SetupLobbyFScreenVideosOnce`를 새 영상, 새 크기, Fill 모드에 맞게 고쳤다.
  - LobbyF 씬의 플레이어 6개에 새 클립, 크기, Fill을 넣고 UV 매핑을 다시 구웠다 (MCP, **저장 안 함**).
  - Artlist 1180크레딧. 사용자 피드백이 두 번 있었다. 첫째, 실제 사람은 안 되고 3등신 게임 스타일이어야 한다. 둘째, 가스마스크는 플레이어 디자인이니 얼굴 없는 모브 직원으로 바꿔야 한다.
- **검증한 것:**
  - 에디터 컴파일 에러 0.
  - 스틸 캡처로 문구 배치, 구체 추적 브래킷, 루프 이음새(흰 화면)를 확인했다.
  - 플레이 모드 캡처: 대형 화면과 세로 화면에 영상이 비율대로 꽉 차고, 뒤집힘이나 흐린 여백이 없다.
- **검증 못 한 것:**
  - 실제 플레이어 카메라의 노출. 화면 재질의 발광이 강해 흰 영상이 더 하얗게 보일 수 있다. 너무 밝으면 재질 발광을 낮추는 것을 검토한다.
  - 빌드에서의 재생.
- **정리할 파일 (사용자 판단):** 예전 `대형스크린_1-2_연속.mp4`, `세로스크린_아틀라스_3-7.mp4`.

### 2026-09-29 · 노트북 · 엘리베이터 키패드 재작업 (SF 화이트 터치 패널 + 손 탭)
- **사용자 피드백:** 아날로그 키캡 방식이 아니라 SF 연구소 느낌의 전자식 터치 패널이어야 한다. 키보드로만 입력하고, 입력할 때마다 손이 패드를 누르는 애니메이션이면 된다. 모델링은 Artlist를 활용한다.
- **한 일:**
  - Artlist로 원화 1장을 생성했다 (90크레딧). 3D 생성은 계정에 없어서 원화를 보고 코드로 형태를 만들었다.
  - 키캡과 마우스 입력을 걷어내고 `ElevatorKeypad3D`를 새로 썼다.
  - 새 스크립트: `Devices/RoundedSlabMesh`, `Elevator/KeypadTouchPad`, `Elevator/KeypadTouchHand`, `Editor/KeypadHandBaker`.
  - `Devices/BeveledBoxMesh`는 삭제했다.
  - `ElevatorPanel`에서 클릭 이벤트 연결을 지웠다.
  - 손 메시와 프리팹을 구웠다.
  - `Tools/Claude/compile_check.js`가 삭제된 파일(오래된 csproj 목록)을 건너뛰도록 고쳤다.
- **검증한 것:**
  - 컴파일 에러 0, 키패드 관련 예외 없음.
  - 테스트 카메라 캡처로 확인: 패널 모양, 표시창(칸별 초록/빨강), 패드 육각 격자와 파문, 손이 오른쪽 아래에서 들어와 검지로 누르는 모습(소매 끝은 화면 밖).
- **검증 못 한 것:**
  - 실제 플레이어로 E를 눌러 여는 흐름 (카메라와 조작 복원).
  - 실제 플레이어 카메라의 노출에서 보이는 모습 (테스트 카메라는 볼륨 설정이 달라 밝고 뿌옇게 나온다).
- **LobbyF 씬 저장 필요.**

### 2026-09-29 · 노트북 (C:\Dev\DeFrag_HDRP) · LobbyF 엘리베이터 3D 키패드 (a-1, a-2)
- **참고:** 이 기기가 노트북이다. 2026-09-27과 2026-09-28 첫 항목에 "메인 PC"로 적힌 것은 사실 이 노트북이었다. 메인 PC는 EUNSEO(D:\unity\DeFrag_HDRP)다.
- **한 일:** 위 "LobbyF 엘리베이터 3D 키패드" 섹션 참고.
  - 새 스크립트: `Elevator/ElevatorKeypad3D`, `Elevator/ElevatorKeypadDisplay`, `Devices/DeviceFocusCamera`, `Devices/BeveledBoxMesh`.
  - 기존 수정: `ElevatorPanel` (3D 키패드 연결, 자리별 판정, 입력 처리를 `RemoveLastCharacter`로 정리).
  - LobbyF 씬: `Lobby/Entrance/Elevator Keypad 3D`를 추가하고 `ElevatorPanel.keypad3D`를 연결했다. **사용자 저장 필요.**
- **검증한 것:**
  - 컴파일 에러 0. 콘솔에 키패드 관련 에러 없음.
  - 플레이 모드에서 테스트 카메라로 BeginFocus → 입력 → 오답을 캡처했다. 키 라벨, 표시창, 칸별 초록/빨강(7H36AE → A만 빨강)을 확인했다.
- **검증 못 한 것:**
  - 실제 네트워크 플레이어로 E를 눌러 여는 전체 흐름: 카메라 복귀, PersonController 복원, 커서 잠금.
  - 마우스 클릭 입력, 정답 후 B1F 이동.
  - HUD(자막, 인벤토리)가 키패드 아래쪽을 가릴 수 있다. 필요하면 포커스 중 HUD 숨김을 추가한다.
- **Artlist:** 0크레딧.

### 2026-09-29 · 메인 PC (EUNSEO) · LobbyF 벽 모니터 영상 (c, d)
- **한 일:** 위 "LobbyF 벽 모니터 영상" 섹션 참고.
  - 스크립트: `MaterialVideoPlaylistPlayer`(UV 매핑, 비율 맞춤, 공유 디코딩, 멈춤 감시)와 `SetupLobbyFScreenVideosOnce`(아틀라스, 새 클립, 베이크)를 확장했다.
  - 셰이더: `HiddenVideoScreenFit.shader`를 새로 만들었다.
  - 영상: `Assets/Movies/Screens1080/`에 2개를 새로 만들었다.
- **검증한 것:**
  - 컴파일 에러 0.
  - 플레이 모드에서 대형·아틀라스 플레이어가 처음부터 30fps로 계속 재생된다 (프레임 수 샘플링).
  - 캡처로 확인: 세로 화면 Screen_A.006·.009·.011은 좌우·상하 반전 없이("NEXUS" 글자로 확인) 가운데에 원래 비율로 나오고, 양옆은 흐린 여백이다. 대형 화면도 똑바로 나온다.
- **검증 못 한 것:**
  - 빌드에서의 재생.
  - 실제 플레이어 시점에서의 밝기. 화면 재질 발광이 4000이라 영상이 하얗게 날아 보인다. 원래 설정이라 건드리지 않았다.
  - 대형 재질을 쓰는 2층 위쪽 화면은 이름이 "대형스크린"이 아니라 대상에 포함되지 않았다.
- **참고:** 에디터가 포커스를 잃으면 영상 재생이 멈춘 것처럼 보일 수 있다. 프레임 확인은 여러 번 샘플링해서 해야 한다.
- **Artlist:** 0크레딧.

### 2026-09-29 · 메인 PC (EUNSEO) · 카메라 C 연출, 뷰파인더, 해킹패드 발광 화면, 터미널 접속 연출과 동료 미러
- **한 일:** 위 "기기 화면·카메라 연출"을 추가했다. 새 스크립트 8개를 만들었고, 기존 스크립트 6개를 좁게 수정했다.
  - 기존 수정: CameraItem(Battery getter), CameraViewSwitcher, ConnectionDevice(All, WorldScreen, ApplyMirrorFrame), CooperativeTerminalHintRelay(미러 RPC), HackingSessionController, TerminalWorldScreenPresenter.
  - 프리팹: HackingPad에 `HackingPadScreen`을 붙였다.
  - 리소스: `Resources/Devices`, `Resources/DeviceSfx`.
- **버그 수정:** 월드 스크린이 검게 보이던 문제 (near clip과 Awake 렌더 시점).
- **검증한 것:**
  - 컴파일 에러 0.
  - 플레이 모드 캡처(Terminal_31 앞 테스트 리그): 패드 OS, 접속 후 UI, 복귀 상태, 키오스크 대기 화면, 정면 관전 시점의 미러(루프백).
  - 카메라 LCD 자세(좌우·상하 반전 없음), 화면 채우기, 뷰파인더 HUD.
  - 콘솔 에러 0. 처음 생긴 Quad 콜라이더 에러는 콜라이더 없는 메시로 바꿔 해결했다.
- **검증 못 한 것:**
  - 실제 플레이어 프리팹에서 C를 눌렀을 때의 전체 들어 올리기 흐름.
  - 2인 네트워크 미러 전송과 대역폭.
  - 빌드에서 `CaptureScreenshotIntoRenderTexture`의 상하 방향.
  - 상대가 든 패드의 STANDBY 화면 (PlayerHeldItemVisualPresenter 복제본에서 컴포넌트가 유지되는지).
- **참고:** 플레이 모드 중 `LiberationSans SDF - Fallback.asset`이 동적 글리프 추가로 바뀐다. 커밋하지 않고 되돌려도 된다.

### 2026-09-28 · 메인 PC (EUNSEO) · 락커 숨기 긴장감 + TV 몬스터 락커 수색
- **해결한 문제:** 락커 바로 앞에서 숨으면 추격이 계속 이어졌다.
  - 원인: `MonsterAI`의 시야 판정이 락커 숨기를 모르고 웅크리기 숨기만 봤다. 락커 안에서는 CharacterController가 꺼져 있어 공격도 맞지 않았다.
  - 그래서 추격은 계속되는데 플레이어는 사실상 무적인 상태가 됐다.
- **한 일:**
  - 위 "락커 숨기 + TV 몬스터 락커 수색" 시스템을 추가했다. 수정한 코드는 `LockerHiding`, `LockerLocalSession`, `MonsterAI`이고, 새로 만든 코드는 `LockerHidingPresentation`, `MonsterLockerHunter`, `LockerVentDoorBuilder`다.
  - 프리팹을 수정했다. `Locker.prefab`에는 환기구 문 메시와 소리를 연결했고, `TvMonster.prefab`에는 `MonsterLockerHunter`와 소리를 넣었다.
  - Artlist를 266크레딧 썼다. 합성 효과음 9종은 `Tools/Claude/gen_locker_sfx.py`로 만들었다.
- **MCP 연결 문제 발견:**
  - 사용자 범위의 `unity-mcp`에 `--project-path`가 없어서, 세션이 MPPM 클론 에디터에 붙어 있었다. 이 때문에 메인 에디터의 Connected Clients에 보이지 않았다.
  - 프로젝트 `.mcp.json`(gitignore 대상)에 `--project-path D:\unity\DeFrag_HDRP`를 넣어 만들었다. 다음 세션부터 적용된다.
- **검증한 것:**
  - 메인 에디터에서 컴파일 에러와 경고 0.
  - 환기구 문을 안팎에서 캡처했다. 안에서 루버 사이로 바깥이 보이고, 밖에서는 환기구가 보인다.
  - 소리 파형과 스펙트로그램을 확인했다.
- **검증 못 한 것:** 플레이 모드와 2인 네트워크 동작 전체. 수색 수치(의심도 등)는 첫 관람객 기준으로 잡은 추정값이라 테스트 후 조정이 필요하다.

### 2026-09-28 · 메인 PC (EUNSEO) · B1F 디버그 체크포인트 개편
- **`B1FStoryDebugCheckpoint`:** bool 두 개를 `Start Checkpoint` enum(None / After Distribution Box A / After Connect Server / After Generator B)으로 바꿨다.
  - *After Generator B* 흐름: 탈출 시퀀스를 RestoringGenerator로 점프(문 파손 상태, 다운로드 35%에서 대기) → 배전함 A, Connect Server 완료 → Emergency Power와 TV 몬스터 스폰까지 대기 → 스토리 정전 → 퀘스트 `b1f_download_initial`까지 완료 → 발전기의 실제 완료 경로 실행(FullPower, 몬스터 발전기 조사, `B1F_GENERATOR_B_COMPLETED` 신호).
  - 리스폰: 플레이어 NetworkTransform이 Owner 권한이라, 각 피어가 복제된 상태(배전함 완료, Connect 완료, 발전기 완료)를 보고 자기 플레이어를 레지스트리 체크포인트 지점으로 Teleport한다.
  - 아이템: 서버가 씬의 HackingPad와 NVCam을 랜덤으로 나눠 `NetworkPlayerInventory.TryGiveWorldItemForStoryDebugServer`로 지급한다.
- **추가한 디버그 진입점 (모두 `UNITY_EDITOR || DEVELOPMENT_BUILD`):**
  - `B1FEscapeSequence.TrySkipToGeneratorRestorationForStoryDebugServer`
  - `GeneratorBController.TryCompleteForStoryDebugServer`
  - `NetworkPlayerInventory.TryGiveWorldItemForStoryDebugServer`
  - `B1FPowerController.IsTvMonsterSpawned`
  - `GameplaySpawnPointRegistry.GetCheckpointSpawnPoint(checkpoint, isHost)` (public)
- **B1F 씬 변경 (MCP, 사용자 저장 필요):**
  - 체크포인트 스폰 지점 이동:
    - Checkpoint 1: 배전함 A 아래 복도 (26.2/27.8, -89.3).
    - Checkpoint 2: 관제실 안 (-17.5/-13.5, -83.5), 3번 문 방향. 기존 지점은 관제실 밖 (-43.8, -56.9)에 있었다.
    - Checkpoint 3: 발전기 B 제어 패널 정면 (-45.3/-42.7, -131.0).
  - 디버그 컴포넌트에 HackingPad, NVCam, Escape Sequence, Generator_B, PlayerSpawnPoints를 연결하고 Start Checkpoint를 After Connect Server로 두었다.
- **퀘스트 설정 버그 수정 (정상 플레이에도 영향):**
  - `b1f_download_initial`과 `b1f_download_resumed`가 Persist Until Scene Change=true, Target 0, AfterSubtitle이었다.
  - 이 설정 때문에 다운로드 중단·완료 신호가 거부됐다. 그 결과 `b1f_full_power`가 켜지지 않아 발전기 연료가 스폰되지 않고 패널도 쓸 수 없었다.
  - `EscapeSequenceSetup.md` 명세대로 Persist 끔, Target 1, Immediate로 바꿨다. `b1f_escape`(마지막 단계)는 그대로 두었다.
- **검증한 것:**
  - Unity 컴파일에서 에러 0.
  - 새 스폰 지점 6곳 모두 바닥 위에 있고 겹침이 없으며, 목표물이 보인다 (호스트 시점 캡처 3장으로 확인).
- **검증 못 한 것:** 플레이 모드와 2인 네트워크 실행 전체.
  - 특히 After Generator B는 몬스터 등장 컷씬이 끝날 때까지(최대 90초) 기다린 뒤 이동한다.
  - 아이템 지급 시 픽업 연출(애니메이션, onPickedUp)이 재생된다.
- **참고 (손대지 않음):**
  - `Terminal_22` 오브젝트의 terminal id도 `terminal_31`이라 관제실 Connect Server 터미널과 id가 겹친다.
  - `LobbyManager.RespawnGameplayPartyAtCheckpoint`는 서버에서 클라이언트 플레이어를 Teleport한다. 플레이어가 Owner 권한이라 클라이언트는 사망 후 리스폰 때 이동하지 않을 가능성이 있다.

### 2026-09-28 · 메인 PC (호스트명 EUNSEO)
- 노트북에서 만든 이 `CLAUDE.md`를 pull 받아 이 PC의 기준 문서로 삼았다. 이 PC의 전역 `~/.claude/CLAUDE.md`에도 이 파일을 읽고 쓰라고 적어 두었다.
- **Unity MCP 연결 검증:** `relay_win.exe --mcp --project-path D:\unity\DeFrag_HDRP`로 도구 41개를 조회하고, `Unity_ReadConsole`로 로그 99개를 읽었다. 에러 0, 경고 9.
  - 참고 경고: `B2F_Ghost` 애니메이터의 `Idle -> Moving` 전환에 조건이 없어 무시된다. NavMeshAgent와 NavMeshObstacle이 동시에 켜진 오브젝트가 있다. 둘 다 팀원 영역이라 건드리지 않았다.
  - 그 밖에 NGO `ServerRpc(RequireOwnership)` obsolete 경고가 다수 있다.
- **이 PC에 없는 것:**
  - `.mcp.json`: Unity MCP만 사용자 범위로 등록돼 있다.
  - Node.js: 그래서 `Tools/Claude/*.js`를 실행할 수 없다.

### 2026-09-28 · 메인 PC
- 기기 간 인수인계용으로 이 `CLAUDE.md`를 만들었다.
- 컴파일 체크와 효과음·텍스처 생성 스크립트를 `Tools/Claude/`로 옮기고, 경로를 기기와 무관하게 동작하도록 바꿨다.

### 2026-09-27 · 메인 PC
- **문 파손 2 실시간 컷씬 제작 (커밋 1caa575):** 흐름은 다음과 같다.
  - CCTV에 몬스터가 지나가고 조명이 순차 소등되다 신호가 끊긴다.
  - 첫 충격에 두 사람이 반응하고, 안도한 뒤 두 번째 충격에 조명이 고장 난다.
  - 두 사람이 낮은 앵글로 다가오고, 정적이 흐른다.
  - 문이 파손되며 카메라가 쓰러지고, 붉은 역광 속 TV 몬스터가 카메라를 발견한 뒤 암전된다.
- **검증:** 플레이 모드 캡처로 모든 샷을 확인했다 (비상 전원을 켠 상태). 컷씬 스크립트 에러는 없었다.
- **미니게임·터미널 개편 (커밋 cdc5412):**
  - 주소 찾기 `<alpha>` 태그 버그를 수정했다.
  - Connect Server 회로 전송 버튼이 초기에 비어 보이던 문제를 수정했다.
- **발전기 B 개편 (커밋 96e25eb).**
