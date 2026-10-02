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
  - **영상 작업 순서 (사용자 지시):** 영상을 먼저 만들지 않는다. 스토리보드 스틸을 한 장의 격자 이미지(Nano Banana 2 i2i, 200)로 일관되게 뽑아 보여주고, 사용자가 허가한 뒤에만 그 컷을 시작/끝 프레임으로 영상을 만든다.
  - 캐릭터 생김새를 모르면 추측하지 말고 Unity에서 플레이어 모델을 캡처하거나 사용자에게 이미지를 요청한다.
- **시네마틱 제작 규칙 (사용자 지시, 2026-10-01, 모든 시네마틱에 적용):**
  - 영상은 Artlist로 만든다.
  - 디자인은 맵 디자인(흰색 위주의 미래형 연구소 회사)을 최대한 참고해 같은 분위기를 낸다.
  - 모든 등장인물은 플레이어 캐릭터처럼 3등신이다. 그림체는 플레이어 캐릭터를 따르고, 플레이어 캐릭터를 그릴 때는 일관성에 특히 주의한다.
  - 연구소 직원은 모두 흰 가운을 입는다. 플레이어가 아닌 캐릭터는 얼굴을 묘사하지 않는다.
  - 대사는 모두 영어 음성이다. 장난스러운 목소리는 쓰지 않는다.
  - 품질은 게임 플레이 화면을 넘어 영화 같은 필터와 완성도를 목표로 한다.
  - 크레딧은 한 작업당 1000 이내로 쓰려고 하고, 더 필요하면 사용자에게 먼저 허가를 받는다.
  - **모델 지정 (사용자 지시, 2026-10-02):**
    - 스토리보드와 사진: 시드림 5.0 Flash (Edit 2K 장당 50).
    - 영상: 시댄스 2.0 720p (초당 115).
    - 영상이 길어질 것 같으면 먼저 클립별 길이와 예상 견적을 보여 주고 물어본다.
    - 합계 15초 이상이면 반드시 사용자 허가를 받고, 클링 3.0 Standard 720p(초당 80)로 만든다.
    - 생성 전에는 항상 클립별 길이와 합계 크레딧을 보여 준다. 시험 1개가 통과했다고 나머지를 묻지 않고 한꺼번에 생성하지 않는다.
  - **기본 영상 모델 (사용자 지시, 2026-10-02 갱신):** 시드댄스 2.0 또는 클링 3.0을 쓴다. 시드댄스 2.5 480p 초안 + 업스케일 방식은 쓰지 않는다 (480p도 초당 200, 1080p 렌더는 별도 비용).
    - 2026-10-02 견적(I2V 720p): 클링 3.0 Standard 무음 초당 80 / 오디오 포함 120, 시드댄스 2.0 초당 115(오디오 무관), 베오 3.1 라이트 오디오 포함 초당 94(최대 8초).
    - VO와 음악을 따로 만들므로 영상은 보통 무음으로 뽑는다.
  - **롱테이크 원칙 (사용자 지시, 2026-10-02):** 짧은 클립 여러 개 대신, 한 번에 길게(10~15초, 모델 최대치) 뽑는 롱테이크를 기본으로 한다.
    - 스토리보드도 "샷 단위"가 아니라 "롱테이크 단위"로 짠다. 한 테이크마다 시작 프레임 1장(필요하면 끝 프레임 1장)과 그 안의 카메라 이동·연기 흐름을 글로 적는다.
    - 한 테이크 안의 장소, 조명, 인물은 하나로 유지한다. 장소가 바뀌는 곳에서만 테이크를 나눈다.
    - 컷 전환이 필요한 부분은 편집(화면 전환, 암전, 글리치)이나 게임 실시간 컷씬으로 잇는다.
    - 생성 전에 테이크별 길이와 견적을 보여 주고 허가를 받는다. 한 테이크씩 생성해서 확인한다.
  - **예산 초과 반성 (2026-10-02):** 엘리베이터 시네마틱에 약 4,088을 썼다. 기본 예산 1000의 4배다. 스토리보드 수정을 반복했고(1200), 영상은 예고한 2,640보다 많은 2,880을 확인 없이 한꺼번에 생성했다.
  - 순서: 스토리보드에 맞춘 일관된 스틸을 먼저 만들어 보여준다. 사용자가 허가한 뒤에만 그 스틸로 영상을 만든다.
  - 캐릭터는 반질반질·매끈하지 않다. 무광 원단과 주름 질감으로 그린다. 배경은 로우폴리 카툰이 아니라 실사풍이다.
  - 정지 사진을 이어 붙인 듯한 정적인 영상은 안 된다. 실제로 움직이는 시네마틱 "영화"를 만든다.
  - 품질 기준: 사용자가 2026-10-02에 준 레퍼런스(모니터 앞 요원 클로즈업, KlingAI 렌더) 이상. 이 이미지에서 후드 귀는 끝이 뾰족하고 안쪽이 분홍인 고양이 귀, 고글형 청록 바이저 가스마스크, 검은 정장이다.
  - 사용자에게는 한국어로만 답한다.
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

**LobbyF 오프닝 시네마틱** (2026-10-01 노트북)
- LobbyF에 들어올 때마다 로컬 화면에서 약 29초짜리 시네마틱을 재생한 뒤 게임을 시작한다. 네트워크 상태는 만들지 않는다 (피어마다 각자 재생).
- **`Scripts/Lobby/LobbyIntroCinematic`** (씬 오브젝트 `LobbyF Intro Cinematic`):
  - `Shot` 목록: VideoClip 또는 정지 이미지(`still`). 샷별 시작·끝, 재생 속도, 페이드, 확대(push-in), 깜빡임(`flicker`), 영상 자체 소리(`playClipAudio`).
  - `SoundCue` 목록(효과음·VO·BGM, 페이드), `SubtitleLine` 목록(화자 + 대사), 시작 캡션, NEXUS 타이틀 카드, `revealAt`에서 게임 화면으로 페이드.
  - 모든 영상이 Prepare된 뒤 시작하고, `prepareTimeout`(10초) 안에 준비되지 않으면 건너뛴다. Space/Enter를 1.2초 누르면 스킵한다.
  - 재생 중에는 `GameplayInputGate`, 로컬 PersonController 비활성, `AudioListener.pause`(시네마틱 소리는 `ignoreListenerPause`)로 게임을 멈춘다.
  - 캔버스 sortingOrder 32500(자막 박스 32000보다 위). 정적 `IsPlaying`과 `Finished` 이벤트를 제공한다.
  - 필름 룩은 `Assets/Shaders/HiddenCinematicGrade.shader`(`Hidden/DeFrag/CinematicGrade`): 노출, 대비, 채도, 스플릿 톤, 비네트, 색수차, 그레인.
- **현재 구성:** 영상 3개 + 정지 컷 1개.
  - Shot1 `Intro_Shot1_Arrival.mp4`(건물 외관 → 걸어감, Veo 생성 발소리 포함) 0.6–4.6초.
  - Shot2 `Intro_Shot2_Lock.mp4`(유리문 잠금 따기, Veo 생성 소리 포함) 4.4–8.4초.
  - 정지 컷 `Art/LobbyF/IntroCinematic/Intro_Cut4_Entry.jpg`(문으로 들어가는 실루엣, 깜빡임) 8.2–10.6초.
  - Shot3 `Intro_Shot3_Lobby.mp4`(로비 안, 전화 통화, 무음) 10.4–23.2초, 속도 0.4 (영상이 끝나면 마지막 프레임에서 확대만 이어짐).
  - VO는 **영어**다 (사용자 요청: 한국어 발음이 어색함). 자막은 한국어 번역 그대로 둔다.
    - 요원 `Intro_VO_Agent_EN.mp3`(Wit, American, 11.0초): "So this is it... the AI lab we've only heard rumors about."
    - 본부 통화 `Intro_VO_Partner_EN.mp3`(Mono, PHONE_CALL, 16.3초): "Copy that. The first floor's dressed up like an ordinary office. Move quietly."
    - 예전 한국어 VO(`Intro_VO_Agent_A/B.mp3`, `Intro_VO_Partner.mp3`)는 더 이상 참조되지 않는다.
  - 타이틀 23.2–27.0초, 게임 화면 공개 27.5초. 스킵 안내는 타이틀이 시작되면 숨긴다.
- **영상 파일:** `Assets/Movies/LobbyF/Intro/`. Veo 3.1 Lite 결과(1280×720, 24fps, H.264 High + B-프레임)라서 VideoClipImporter의 **트랜스코딩을 켜 두었다**. 끄면 Windows 디코더에서 멈출 수 있다. Shot1·2는 `importAudio`를 켜야 소리가 난다.
- **소리:** `Assets/SoundSources/LobbyF/Intro/`. 합성음은 `node Tools/Claude/gen_intro_sfx.js Assets/SoundSources/LobbyF/Intro`로 다시 만든다. 영상 자체 소리와 겹치는 발소리·잠금 효과음 큐는 뺐다 (파일은 남아 있음).
- **스토리보드 원본:** `Assets/Art/LobbyF/IntroCinematic/Source~/` (승인본 `Intro_Storyboard_6.png`, 컷별 `Cut1~6.jpg`, 반려본 폴더들). `~` 폴더라 임포트되지 않는다.
- **팀원 코드와의 연결 (좁은 수정):**
  - `SubtitleTrigger.TryPlayForPlayer`: 시네마틱 중에는 대기한다.
  - `SubtitleIntroPresentation.PlayAfterInitialization`: 시네마틱이 끝난 뒤 시작 자막("성공적으로 잠입한 것 같군." 무전기 인트로)을 재생한다.
- **함정:** 이 프로젝트는 Enter Play Mode Options로 도메인·씬 리로드가 꺼져 있다. 정적 값과 직렬화 안 된 필드가 이전 실행에서 남으므로, `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`로 정적 값을, `Start`에서 phase를 초기화한다. 새 컴포넌트도 같은 처리가 필요하다.

**게임 시작 프롤로그 "1막 — PROLOGUE: 넥서스의 흥망"** (2026-10-02 메인 PC)
- **재생:** `Scripts/Lobby/GamePrologueCinematic.cs`. 씬이나 빌드 설정을 바꾸지 않는다.
  - `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`에서, 실행 후 처음 로드된 씬이 빌드 인덱스 0(MainLobby)이면 DontDestroyOnLoad 전체 화면 캔버스(sortingOrder 32700)를 띄운다.
  - `Resources/Prologue/Prologue_Cinematic.mp4`(그림)와 `Resources/Prologue/Prologue_Audio.wav`(사운드트랙, 렌더 스크립트의 `mix.wav` 복사본)를 따로 재생한다. VideoPlayer 오디오 출력은 끈다(`None`). AudioSource는 `ignoreListenerPause`를 켜고, `AudioListener.pause`로 로비 소리를 멈춘다.
  - 사운드트랙이 시계다. 그림이 0.2초 넘게 어긋나거나 0.5초 멈추면 `player.time`을 소리에 맞춰 다시 찾는다. MainLobby 로드 직후 프레임이 튀는 동안은 시작하지 않는다(0.6초 + 안정 프레임 12개).
  - **함정:** 예전처럼 VideoPlayer → AudioSource로 소리를 내면, 첫 씬 로드 끊김 뒤 `AudioSampleProvider buffer overflow`가 쏟아지고 소리가 계속 끊기며 그림이 멈추거나 검게 나온다(2026-10-02 사용자 제보). 영상 원본을 바꾸면 `render_prologue.py`가 wav도 같이 갱신한다.
  - 끝나거나 Space/Enter를 1.2초 누르면 0.8초 페이드로 로비가 드러난다. 정적 `IsPlaying`과 `Finished` 이벤트를 제공한다.
  - 에디터에서는 기본으로 꺼져 있다. 메뉴 **DEFRAG > Prologue > Play In Editor**(EditorPrefs)로 켠다. 테스트용으로 `GamePrologueCinematic.PlayForTesting()`(에디터·개발 빌드 전용)이 있다.
- **영상 제작:** `python Tools/Claude/render_prologue.py [--stills=t1,t2]`가 ffmpeg로 한 편을 굽는다 (약 5분).
  - 결과: 67.7초, 1920×1080, 30fps, H.264 Baseline + AAC, 약 41MB, 비트레이트 상한 7Mbps.
  - 원본은 `Assets/Art/Prologue/Source~/` (`~` 폴더라 임포트되지 않음):
    - `v6_cuts/Cut01~16.jpg`: 승인 스토리보드 v6의 컷.
    - `video_raw/`: Veo 영상 3개 (V09 안드로이드 눈, V13 복도 도주(소리 포함), V14 끌려감(소리 포함)).
    - `audio_raw/`: Lyria 3 Pro 음악 60초, VO 원본.
    - `build/`: 중간 결과물. 지워도 된다.
  - 가공한 VO WAV 7개는 `Assets/SoundSources/Prologue/`에 있다.
    - 내부고발자 음성은 원본을 잘라 피치를 0.85배로 낮추고, 대역을 제한하고, 리미터를 걸었다.
  - 스크립트가 하는 일:
    - 스틸에 켄 번스 카메라 이동을 준다.
    - 한글 자막을 넣는다. 영어 VO에 맞춘 번역이고, 화자는 앵커, 내부고발자, 연구원이다.
    - 화면 그래픽을 그린다: 연도 캡션, NEXUS NEWS 하단 자막, 광고 카피, 엘리베이터 B1→B5 숫자, 속보 바, 익명 게시판 카드, 다크웹 카드, 신문 1면 「일간 시사」, CCTV 타임스탬프, NO SIGNAL, 엔딩 문구.
    - 시대별로 색을 보정하고(2043 따뜻함, 2045 차가움, CCTV), 필름 그레인, 비네트, 2.0:1 레터박스를 넣는다.
    - 소리를 믹싱한다: 음악은 VO가 나올 때 사이드체인으로 줄인다. Veo 영상의 효과음을 쓰고, 합성음은 테이프 잡음과 신호 끊김 잡음만 최소로 넣었다.
  - 음악은 5.8초에 시작한다. 48.5초에 딱 끊기는 지점이 연구원이 끌려가는 순간(약 54.2초)에 맞춰져 있다. 컷 길이를 바꾸면 `music_at`, 자막, VO 시간도 같이 옮겨야 한다.
  - 규칙 반영:
    - 얼굴이 보이는 컷은 로컬에서 가렸다: 광고 홀로그램 사진은 블러, 도플갱어 얼굴은 어둡게 블러, 13컷 연구원은 1K 편집으로 캡과 그림자를 씌웠다.
    - 대본의 "얼굴만 비추고 표정이 무너진다"는 뒷모습과 떨리는 몸으로 대신했다.
- **함정:**
  - 영상 출력에서 오디오를 같은 ffmpeg 패스로 믹싱하면 타임스탬프가 깨진다. 그래서 `mix.wav`를 먼저 만든다.
  - drawbox 표현식의 `w`/`h`는 박스 자신의 크기다. 화면 크기는 `iw`/`ih`로 쓴다.
  - filter_complex 안의 표현식에 쉼표가 있으면 따옴표로 감싸야 한다.
  - 그레인 때문에 CRF만 쓰면 260MB가 나온다. 비트레이트 상한이 필요하다.

**LobbyF 엘리베이터 코드 퍼즐 완화 + OFFICE2 이스터에그** (2026-10-02 메인 PC)
- **정답은 그대로 `7H36BE`다.** 칸 구성: ID 7(명찰 07) · ZONE H(근무표 07번) · CH 3, 6(OFFICE3 TV에서 괴물이 나오는 채널) · LOG B, E(OFFICE2 화이트보드 290807 → 메모장 표 290=B, 807=E).
- **키패드 (`ElevatorKeypad3D`, `ElevatorKeypadDisplay`):**
  - 각 칸 위에 이름(`slotLabels`: ID/ZONE/CH/CH/LOG/LOG)을 표시한다. 판정 색도 같이 바뀐다.
  - `hintAfterWrongAttempts`(기본 2)번 틀리면 SYSTEM 줄에 가장 왼쪽 빨간 칸의 단서 위치(`slotHints`)를 띄운다. 피어별 로컬 표시다.
- **TV (`Scripts/Hints/BroadcastChannelScreen`, `OFFICE3티비`에 부착):**
  - 영상은 노이즈 구간까지 포함해 7구간이고, CH 01~07로 센다. 괴물 구간이 CH 03·CH 06이며, 영상 자체의 작은 "CAM 03/06 REC" 표기와 같다. 구간 시작 시각(0, 5.0, 6.2, 10.88, 12.08, 16.58, 21.12초)은 ffmpeg 장면 검출로 쟀다.
  - 방송이 재생되는 동안 유리 위 6mm에 발광 화면판(`DeviceScreenQuad`, 검은 금속 = 반사 0)을 띄우고, `OffscreenUiSurface`로 영상과 채널 OSD(오른쪽 위 + 바뀔 때 가운데 큰 숫자)를 그린다. 복제된 VideoPlayer 상태만 읽으므로 네트워크 상태는 따로 없다.
  - 팀원 코드 `HintCameraPresentation`은 건드리지 않았다.
- **TV "비율 이상·잘림" 버그 원인:** 영상 매핑은 정상이었다. 유리 재질이 광택 Lit(Smoothness 0.63, 발광 없음)이라 천장·바닥 반사가 영상 위에 덮여서, 위 1/3이 검게 잘리고 아래가 하얗게 뜬 것처럼 보였다.
  - 함정: 유리 메시의 **정점 법선이 약 2° 아래로 기울어** 있다. 그 법선으로 판을 세우면 아래 절반이 유리 뒤로 들어가 가로 경계선이 생긴다. 법선은 삼각형 외적(면 기하)으로 구한다.
  - 함정: `SmartTV.003`은 Read/Write가 꺼져 있다. 에디터 모드에서 `AcquireReadOnlyMeshData`가 될 때도 있고 안 될 때도 있다. 컨텍스트 메뉴 **Bake Screen From Material Slot**이 실패하면 플레이 모드에서 굽는다.
- **단서 문구 (SubtitleTrigger `mySubtitles`, 씬 데이터):** 명찰(→근무표 유도), 근무표(사원증과 대조), OFFICE3 회의록("N구역(OFFICE 2) 팀장님께"), TV 옆 회의록(채널 번호 안내), 클립보드(팀장 PC 메모장 유도), OFFICE2 화이트보드("근무 기록 코드", 마지막 290807).
- **OFFICE2 모니터 (`MonitorDesktopUI` 좁은 확장):**
  - `files` 배열(아이콘 Button, 제목, 본문)과 `FileViewerWindow`(NotepadWindow 복제)를 추가했다. 창 안 파일을 누르면 휴지통 창 위에 뷰어가 열린다.
  - 메모장: 변환 규칙과 예시 `290703 → B D`를 넣었다.
  - 휴지통 7개:
    - 단서: Readme.tmp(코드 칸 순서), Assign.tmp(07번 → H구역), 사직서(TV 세 번째 채널과 "그 두 배" 채널).
    - 이스터에그: 경비일지(고양이 귀 후드 2인 = 플레이어), 식단표(금요일 B1 식당 중단), 보도자료(TV 홍보 문구 + "거짓말."), ~$thanks.tmp(팀 인사).
  - 폰트(Galmuri11, NanumSquareB, DungGeunMo)에 빠진 글자가 없는 것을 확인했다.

## 현재 상태 (최신화할 것)

- **#2 탈출구 열림 시네마틱 (2026-10-02~03 메인 PC, 미커밋, B1F 씬 저장 필요):** 완성해서 `B1F Escape Sequence`의 `exitVideo` 슬롯에 연결했다. 다운로드 100% 직후 재생된다.
  - 결과: `Assets/Movies/B1F/ExitCinematic/B1F_Exit_Cinematic.mp4` (21초, 1920×1080, 24fps, H.264 Baseline + AAC, 17MB, 트랜스코딩 끔, importAudio 켬).
  - 다시 굽기: `python Tools/Claude/render_exit_cinematic.py [--stills=t1,t2]` (원본은 `Assets/Art/B1F/ExitCinematic/Source~/`, 중간 결과물은 `Source~/build/`).
  - 구성: 테이크 A 0~9초(무전, VO 1·2) → 글리치 → 실제 B1F 지도 인서트 5초(스캔 라인 공개, 비상구 빨간 마커, 비상구 쪽으로 줌) → 테이크 B 0~1.8초(끄덕임, VO 3) → 휩 컷 → 테이크 B-2 0~4.6초(옆 트래킹 질주, 페이드) → 검은 화면 0.6초.
    - 지도는 `FacilityBlueprint`(NavMesh 래스터)를 1024로 뽑은 `ref/B1F_Blueprint_1024.png`를 쓴다. 비상구는 `Exit23` 방(월드 (2.73, −165.19), B2 계단 방으로 추정)이다. **사용자 확인 필요:** 이 위치가 실제 B2 비상구가 맞는지.
    - 자막(한국어, 화자 "조력자"), 2.0:1 레터박스, 그레인, 비네트. 소리는 VO + 무전 잡음 + 지도 비프 + 휩 + 타일 발소리(`Footsteps - Essentials/Footsteps_Tile_Run`) + 저음 드론. 평균 −23.7dB, 최대 −7dB.
  - 원본: 스토리보드 `Exit_Storyboard.md`, 스틸 v2(`TakeA_start`, `TakeB_start`, `TakeB_end`, `TakeB2_start`), 영상 `video_raw/Exit_TakeA/TakeB/TakeB2.mp4`, VO `audio_raw/Exit_VO_1_2.mp3`, `Exit_VO_3.mp3`(Eleven v3, voice Mono, WALKIE_TALKIE), 반려본 `v1/`, 참고 `ref/`.
  - 테이크 B-2 생성 ID `01a0fd4f-34be-7b36-9b85-0529e050fa17` (480). 4.5초 이후 조명 아래에서 정장이 하얗게 바래므로 4.6초에서 자른다.
  - 남은 아쉬움: 테이크 A 6초 이후 바이저 안에 눈동자가 비친다. 테이크 B-1 아래쪽에 AI가 그린 가짜 지도 빛이 보인다(자막·레터박스에 대부분 가려짐). 시네마틱 캐릭터는 밝은 청록 바이저에 뾰족한 귀라서, 인게임 모델(진한 파랑 바이저, 옆으로 퍼진 귀)과 조금 다르다.
  - 확인 못 한 것: 소리를 귀로 들어 본 것, 2인 네트워크에서 Exit 단계 재생과 종료 후 조작 복원, 빌드 재생. `B1FEscapePresentation`은 오디오를 `Direct`로 내보낸다 (프롤로그처럼 끊기면 별도 AudioClip 방식으로 바꿔야 한다).
  - **플레이어 실제 비율 (`Assets/Player/0724_PlayerCharacter.fbx` 측정):** 키 2.86 기준으로 머리(목~정수리) 약 1.0(35%, 약 2.9등신), 골반 높이 1.17(다리 약 41%), 몸통(목~골반) 0.69. 후드가 머리 뒤로 늘어지고, 귀는 옆으로 넓게 벌어진다. 바지는 통이 넓다.
    - **교훈:** 전신 동작 스틸에서 비율이 가장 잘 무너진다. 사람 비율로 나온 스틸을 편집 대상으로 주면 그 비율에 끌려간다. 승인된 정면 스틸을 캐릭터 기준으로 넣고, 비율은 "키를 3등분: 머리 / 몸통 / 다리, 머리 높이 = 다리 길이, 머리 폭 = 어깨의 1.4배"처럼 수치로 적는다. "짧은 팔다리"처럼 과장하면 너무 짤막해진다. 이번에 B-2 스틸만 5번 만들었다(250).
    - **모델 렌더 방법:** 열린 씬에서 FBX를 임시로 Instantiate해 레이어 31에 두고, 카메라 cullingMask=1<<31, `HDAdditionalCameraData.clearColorMode=Color`로 단색 배경에 렌더한 뒤 삭제한다. 조명은 B1F `(−0.45, 0, −100)` 부근에 `Power/FullPower`를 잠깐 켜고 포인트 라이트를 추가해 잡는다. 씬은 dirty가 되지 않는다. 달리기 애니메이션 샘플링이나 뼈 회전으로 자세를 잡는 방법은 자세가 깨져 쓰지 못했다.
  - B1F 참고 렌더 방법: 열린 B1F 씬에서 `Power/FullPower`만 켜고 임시 카메라로 렌더한 뒤 원래 상태로 돌려놓는다(씬 dirty 안 됨). 좌표 (0,·,-100), (-44,·,-128) 부근이 어두운 복도다. 사용자가 플레이 중이거나 씬이 dirty면 하지 않는다.

- **게임 시작 프롤로그 시네마틱 (2026-10-02 메인 PC, 미커밋):** 영상 완성, 게임 연결, 플레이 모드 확인까지 했다. 위 "게임 시작 프롤로그" 섹션 참고.
  - 소리 끊김·영상 안 보임 문제는 사운드트랙 분리로 고쳤다 (MainLobby 플레이 모드에서 끝까지 확인). 커밋할 때 `Prologue_Audio.wav`와 `.meta`를 꼭 같이 올린다.
  - 사용자 확인 대기: 완성 영상 전체(소리 포함) 검토.
  - 3번째 영상(연구원이 끌려감) 피드백: "몬스터에게 위로 끌려간 느낌이 아니라 혼자 점프하는 것 같다."
    - 지금은 편집으로 보정했다: 슬로모션 정지, 천장의 어둠, 조명 꺼짐, 바닥을 잘라내고 위로 끌어올리는 프레이밍, 세로 블러와 흔들림.
    - 더 고치려면 재생성이 필요하다 (Veo 3.1 Lite, 소리 포함 376). 사용자 추가 허가가 필요하다. "violently yanked", "scream" 같은 표현은 안전 필터에 걸린다 (실패 시 크레딧은 차감되지 않음).
  - 확인 못 한 것: 소리를 귀로 들어 본 것(믹스 균형), 실제 빌드의 첫 실행 흐름(스플래시 → 프롤로그 → MainLobby), 스킵 키 입력(코드로만 확인).
- **B1F 진입 엘리베이터 시네마틱 (2026-10-02 노트북, 미커밋, B1F 씬 저장 필요).** Unity 조립을 마쳤고 플레이 모드 캡처로 확인했다.
  - 씬 오브젝트: B1F 루트의 `B1F Elevator Intro Cinematic`. `LobbyIntroCinematic` 컴포넌트를 재사용한다.
  - 타이틀 카드 색을 직렬화 필드로 추가했다: `titleCardColor`, `titleInk`, `titleSubInk`, `titleRuleColor`. 로비는 기존 흰색 기본값 그대로이고, B1F는 검은 카드에 빨간 선이다.
  - 영상: `Assets/Movies/B1F/ElevatorIntro/Elev_01~10.mp4` (클링 3.0, 1280×720, 24fps, 오디오 포함). 트랜스코딩과 importAudio를 켰다.
  - 음성: `Assets/SoundSources/B1F/ElevatorCinematic/Elevator_VO_AgentA/B.mp3` (MiniMax, 3.55초와 4.08초). 타이틀 효과음은 LobbyF의 `Intro_TitleHit.wav`를 재사용했다.
  - 타임라인: 0.5초부터 클립 10개를 0.1초씩 겹쳐 이어 붙였다. VO A 27.4초, VO B 31.9초, 타이틀 "B1 / NEXUS · SUBLEVEL 1 · RESTRICTED" 36.4–40.0초, 게임 화면 공개 40.5초.
  - 키프레임 원본: `Assets/Art/B1F/ElevatorCinematic/Source~/Keyframes/KF01~12.jpg` (v6 격자를 1920×1080으로 자른 것).
  - 확인 못 한 것: 소리 청취, 2인 네트워크(디버그 체크포인트로 B1F에 들어가도 재생된다. 필요하면 Space를 길게 눌러 스킵), 빌드.
  - 남은 아쉬움: 클립 길이가 3~5초라 컷 전환이 잦다. 다음에는 10~15초 롱테이크로 만든다 (사용자 요청).
  - **팀원 커밋 3e4f000과 병합 (2026-10-02):** `B1F.unity` 충돌 10곳을 정리했다.
    - 팀원 쪽을 따랐다: 새 필드 `subtitleInterlude`, 자막 색·오디오, `playOnPlayerEnter: 0`.
    - SceneRoots에는 양쪽 새 오브젝트(시네마틱과 팀원의 `Map`)를 모두 남겼다.
    - Unity에서 다시 열어 누락 스크립트 0, 시네마틱이 그대로 있는 것을 확인했다. 백업은 `Temp/B1F_conflict_backup.unity`.
    - 팀원의 새 `SubtitleSceneEntryPresentation`에도 시네마틱이 끝날 때까지 기다리는 한 줄을 넣었다.
  - 흐름: 엘리베이터 앞 두 요원 → 키패드 입력 → 초록 승인 → 흰 문이 열림 → 탑승 → 내부 버튼은 G와 B1뿐 → B1을 누르면 빨간 불 → 문이 닫히고 하강 → 대화 → 층 표시 B1 → 페이드아웃.
  - 대사(영어 음성, 한국어 자막):
    - 요원 A: "Didn't expect a way down to be this close." (의외로 가까운 곳에 지하로 내려갈 수단이 있었군.)
    - 요원 B: "This is where it really begins. Stay sharp." (지금부터가 시작이겠지. 긴장을 풀지 마.)
  - 소품: LobbyF에는 해킹패드도 카메라도 나오지 않는다 (사용자). A는 맨손, B는 **무전기**만 든다.
  - 스토리보드 v6는 사용자가 승인했고, 영상은 클링 3.0으로 만들었다 (사용자 허가).
    - 최종 파일: `Assets/Art/B1F/ElevatorCinematic/Source~/Elevator_Storyboard_v6.png`, 미리보기 `_v6_preview.jpg`.
      - v6 = v5에 맨손 수정본(`Elevator_FixHands_output.png`, 생성 ID `01a0fb09-5638-7860-bd3b-365229dcfd72`)의 1·2·5·7·9·10·11번 컷을 갈아 끼운 것이다.
      - **요원은 장갑을 끼지 않는다 (사용자).** 손은 맨손(밝은 살색), 흰 셔츠 커프와 검은 소매.
    - 이전 기준: `Elevator_Storyboard_v5.png`(6336×2688, 4×3), 미리보기 `_v5_preview.jpg`.
      - v5 = v3 격자에 6·7·8번만 갈아 끼운 것. 고친 컷은 `Elevator_Fix678_output.png`(생성 ID `01a0fb06-21e0-7c60-b507-769603c8298f`)에서 Unity로 잘라 붙였다.
      - 방법(재사용 가능): 고칠 컷을 원본 격자에서 잘라 2×2(16:9) 판(`_Fix678_input.png`)으로 모아 한 번만 편집하고, 검은 틈 위치를 찾아 다시 잘라 붙인다. 원본 격자의 틈: 열 x 1574–1590 / 3164–3179 / 4753–4768, 행(아래에서) y 884–898 / 1790–1804.
    - v3 생성 ID `01a0fb01-7c86-7689-81e5-ebc9307788db`.
    - v3 피드백(v5에서 반영): 6번 앞쪽 큰 요원 둘 삭제, 7·8번 버튼 패널이 너무 커서 플레이어 손 크기에 맞춤, 8번 큰 손 삭제.
    - 남은 점: 10번에서 요원 A도 무전기를 든 것처럼 보인다. 키프레임에서 A를 맨손으로 맞춘다.
    - 이력: v1 `01a0faf9-…` → v2 `01a0fafe-…` → v3. v4(`01a0fb02-…`)는 6·8번만 고치려 했지만 그림 전체가 뿌옇고 일러스트처럼 변해 버렸다 (`rejected/` 미리보기).
      - 교훈: 격자 이미지를 연달아 편집하면 화질과 그림체가 무너진다. 남은 컷 수정은 격자가 아니라 영상용 단일 키프레임에서 한다.
    - v3에서 남은 오류: 6번 CCTV 컷에 요원이 4명으로 겹친다 (앞쪽 큰 뒷모습 2 + 가운데 작은 2). 8번에 몸에 붙지 않은 손이 오른쪽 아래에서 하나 더 들어온다.
    - v3 사용자 피드백(반영 완료): 5번은 엘리베이터로 들어가니 등이 보여야 한다. 6번은 플레이어 뒤쪽 대각선 위 CCTV 시점. 7·8번은 클로즈업으로 넘어가지 말고 같은 샷 안에서 버튼을 누르게 한다.
    - 레퍼런스: 사용자 품질 기준 이미지, LobbyF 엘리베이터 정면 캡처(`Temp/ElevRef/wide.png`, 에디터 카메라 렌더), `Keypad_Concept.png`.
    - v1 피드백과 v2 반영: 캠코더를 무전기로 바꿨다. 3번 손이 너무 실사여서 3·8번 손을 캐릭터의 작고 흰 손 + 검은 소매 + 흰 커프로 바꿨다. 6번이 너무 클로즈업이어서 엘리베이터 천장 구석 CCTV에서 내려다보는 시점으로 바꿨다.
    - v2에서 남은 점: 5·10번에서 요원 A의 해킹패드가 잘 안 보이고, 둘 다 무전기를 든 것처럼 보이는 컷이 있다.
  - 영상 견적: Veo 3.1 Lite I2V 720p는 초당 약 63크레딧(4초 252, 6초 378, 8초 504)이고, 오디오를 켜도 같다.

- **LobbyF 오프닝 시네마틱 (2026-10-01 노트북, 미커밋):** 플레이 모드 캡처로 전체 흐름 확인. **LobbyF 씬 저장 필요.**
  - 확인 못 한 것: 소리를 귀로 들어 본 것(영상 자체 소리 볼륨, VO 타이밍), 실제 2인 네트워크 진입(호스트·클라이언트 각각 재생되는지, 끝난 뒤 조작 복원), 빌드에서의 영상 재생.
  - Shot3 중간(약 12~13초)에 Veo 전환 때문에 두 캐릭터 머리가 화면을 크게 가리는 프레임이 있다. 거슬리면 Shot3 시작 구간을 조정한다.

- **진행 중: LobbyF 개선 (2026-09-29 시작, 메인 PC).** 작업량 제한으로 끊기면 새 세션은 이 목록에서 체크 안 된 항목부터 이어서 한다. 항목을 끝낼 때마다 체크하고, 작업 로그에 한 줄씩 남긴다.
  - Artlist 예산: 이 작업에만 3000크레딧까지 쓸 수 있다. 사용량은 아래 크레딧 장부에 기록한다.
  - a~e 모두 끝났다 (b-1·b-2는 2026-10-02 메인 PC). a-1/a-2는 "LobbyF 엘리베이터 3D 키패드", b는 "LobbyF 엘리베이터 코드 퍼즐 완화" 섹션 참고.
  - [x] a-1 엘리베이터 비밀번호 오답 시 자리별 색 표시 (맞은 글자 초록, 틀린 글자 빨강) (2026-09-29 노트북, 플레이 캡처로 확인)
  - [x] a-2 엘리베이터 패널을 세련된 3D 느낌으로 리디자인. 입력할 때 플레이어가 3D 키패드를 누르는 애니메이션 (2026-09-29 노트북, SF 화이트 터치 패널과 손 탭으로 재작업. 테스트 카메라로 확인했고, 실제 플레이어 흐름은 미검증)
  - [x] b-1 단서 난이도 완화 (2026-10-02 메인 PC): 키패드 칸 이름·오답 안내, 단서 문구 정리, TV 채널 번호 표시와 화면 버그 수정. **LobbyF 씬 저장 필요, 미커밋.**
  - [x] b-2 OFFICE2 모니터 메모와 휴지통에 단서와 이스터에그 추가 (2026-10-02 메인 PC, 같은 씬 저장)
    - 확인 못 한 것: 실제 플레이어로 처음부터 단서를 따라가는 흐름, 2인 네트워크(TV 방송 중 상대 화면에도 CH 표시가 뜨는지), 빌드.
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

- **누적 사용:** 11,388 (플랜 16,500 중. 2026-10-02 병합 시점 잔액 5,112 확인. 노트북 엘리베이터 시네마틱 약 4,088과 메인 PC 프롤로그 2,422가 모두 포함된 값이다).
- **#2 탈출구 열림 (2026-10-02~03 메인 PC, 완료, 사용자 허가 범위):** 스틸 v1 3장 150 + v2 3장 150 = 300 (시드림 5.0 Flash Edit 1K, 장당 50). 영상: 테이크 A 800 (클링 3.0 Standard 720p 무음 10초), 테이크 B 480 (6초, 앞 1.8초만 사용), 테이크 B-2 480 (6초). VO 37 (Eleven v3 28 + 9). B-2 스틸 5장 250. 합계 2,347.
- **게임 시작 프롤로그 (2026-10-01~02, 기본 1000 + 사용자 추가 허가 1500 중 2422):**
  - 스토리보드 890: 격자 v1~v4 각 200 (v1 반려, v2 실패, v4 격자 깨짐), 5컷 단일 편집 90.
  - 제작 1532: 영어 VO 5회 138 (Eleven v3: 앵커 Bulletin, 광고 Nourish, 내부고발자 Revelation, 연구원 Esteem), Lyria 3 Pro 60초 음악 300, 13컷 얼굴 가림 편집 90, Veo 3.1 Lite 720p 4초 영상 3개 1004 (무음 252 + 소리 포함 376 × 2).
  - 끌려가는 장면의 첫 시도는 안전 필터에 걸려 0크레딧이었다.
- **B1F 엘리베이터 시네마틱 (2026-10-02, 약 4088 사용, 사용자가 영상 예산 허가):**
  - 스토리보드 1200: 격자 v1~v4 4장, 6·7·8번 수정판 1장, 맨손 수정판 1장 (Nano Banana 2 I2I 4K, 장당 200). v4는 실패했다.
  - 영상 2880: 클링 3.0 Standard 720p(오디오 포함, 초당 80) 클립 10개, 합계 36초.
  - 음성 8: MiniMax Speech 02 HD 2줄 (Wit, Gravity).
- **모델 단가 메모 (2026-10-02 견적, 720p):**
  - 영상: 클링 3.0 Standard 초당 80, 시댄스 2.0 초당 115, 시댄스 2.0 미니 초당 120, Veo 3.1 Lite 초당 63. 모두 최대 15초.
  - 이미지: 시드림 5.0 Flash Edit 2K 장당 50, Nano Banana 2 I2I 4K 장당 200.
  - 음성: MiniMax 한 줄 4, ElevenLabs v3 한 줄 8.
- **LobbyF 오프닝 시네마틱 (2026-10-01, 기본 1000 + 사용자 승인 약 960, 실제 약 2993):**
  - 반려·낭비: 승인 전에 만든 첫 영상 묶음 971, 키프레임 v2 200, 수정 편집 3회 600. 이 때문에 "스토리보드 먼저" 규칙이 생겼다.
  - 승인본 스토리보드 격자 1장 200, 최종 영상 3개 1004(Veo 3.1 Lite 720p 4초: 소리 포함 376, 무음 252), 한국어 음성 18, 영어 음성 2줄 32(Eleven v3, 한 줄 15~17).
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

### 2026-10-02~03 · 메인 PC (EUNSEO) · 팀원 탈출 시퀀스 영상 색보정
- **배경:** 팀원이 만든 `문 파손 연출 1-1`(관제실 두 요원, 붉은 경고등)과 `CutScene_김영주`(복도 접근, 빨간 TV 몬스터)를 B1F 톤에 맞추려는 작업. 원본은 `G:\내 드라이브\바빠도 게임은 해야지\Story CutScene\`. 사용자 결정: 이 둘만 활용하고, 몬스터 모양은 그대로 쓴다(우리 게임과 비슷하다고 판단).
- **한 일 (색보정, 크레딧 0):** `Tools/Claude/grade_team_cutscenes.sh "<원본 폴더>" <출력 폴더>`
  - 청록 쪽 톤 이동, 약한 대비, 비네트, 그레인, 오디오 리미터. 결과는 `Assets/Art/B1F/TeamCutsceneGrade/Source~/Breach1_graded.mp4`(10초), `Approach_graded.mp4`(18초), 비교 이미지 `Compare_*.jpg`. `~` 폴더라 임포트되지 않는다.
  - 처음 안은 대비와 감마를 세게 줘서 캐릭터가 검게 뭉개졌다. 감마를 올려 약하게 줄였다. 그레인이 있으면 CRF만으로는 270MB가 나오므로 `-maxrate 9M`이 필요하다.
- **참고 스틸 (50크레딧):** `Breach1_still_A_v1.jpg`. 팀원 영상 첫 장면을 우리 캐릭터(`ExitCinematic/Source~/TakeA_start.png` 기준)와 게임 관제실(에디터 렌더 `Temp/CtrlRef/`)로 다시 그린 것. 영상 자체를 바꾸려면 재생성이 필요하다(클링 3.0 Std 10초 약 800). 지금은 색보정만 적용하는 안으로 진행 중.
- **탈출 시퀀스 연결 (사용자 지시):** `B1F Escape Sequence`의 `approachVideo` = `Assets/Movies/B1F/EscapeSequence/Escape_Approach.mp4`(김영주, 15.8초), `impactVideo` = `Escape_Impact.mp4`(문 파손 연출 1, 10초). 둘 다 트랜스코딩과 importAudio 켬. `breachCutscene`(실시간 문 파손 2)은 그대로.
  - **장면 전환 (사용자 지시):** 영상 파일 자체에 구웠다(`grade_team_cutscenes.sh`의 3번째 인자로 `Assets/Movies/B1F/EscapeSequence`를 주면 재생성). 접근 영상은 TV가 사라지는 14.2~15.0초에 RGB 분리 글리치 + 노이즈 소리, 이어 암전, 15.8초에서 끝(원본 뒤 약 3초는 순수한 검정이라 잘랐다). 충격 영상은 0.35초 페이드인 직후 0.3~0.85초에 글리치.
  - 재생 순서: 경고 → 다운로드 → **접근 영상** → **충격 영상** → 실시간 문 파손 2 → 인터럽트. `warningVideo`와 `exitVideo`는 비어 있어 placeholder가 재생된다.
  - **B1F 씬 저장 필요** (MCP로 필드 2개를 바꾸고 씬을 dirty로 표시만 했다).
- **검증한 것:** 원본과 보정본을 4개 시점에서 나란히 비교했다. 영상 임포트 길이(17.9/10초)와 필드 할당, 콘솔 에러 없음까지 확인했다.
- **검증 못 한 것:** 소리를 귀로 들은 것, 플레이 모드에서 접근 → 충격 → 문 파손 2가 이어지는 흐름과 2인 네트워크 재생. 문 파손 영상의 요원은 여전히 바이저가 없고 배경이 회색 방이라 게임 관제실과 다르다.

### 2026-10-02 · 메인 PC (EUNSEO) · LobbyF 엘리베이터 코드 퍼즐 완화, OFFICE2 이스터에그, TV 화면 버그
- **사용자 요청:** 비밀번호 추론이 너무 어렵다는 평가가 있어 난이도를 낮추고, OFFICE2 메모와 휴지통에 단서와 이스터에그를 넣는다. 추천안(A 코드 구조 노출, B 문구 정리, C TV 채널 번호, D 오답 안내, E 이스터에그)을 사용자가 전부 승인했다. TV 비율·잘림 버그 수정도 함께 요청했다.
- **한 일:** 위 "LobbyF 엘리베이터 코드 퍼즐 완화" 섹션 참고.
  - 새 스크립트: `Scripts/Hints/BroadcastChannelScreen.cs`.
  - 수정한 스크립트: `ElevatorKeypad3D`, `ElevatorKeypadDisplay`(내 코드), `MonitorDesktopUI`(팀원 코드, 파일 뷰어만 추가).
  - LobbyF 씬(MCP, **저장 안 함**): 단서 자막 6개, 메모장 본문, 휴지통 아이콘 7개와 `FileViewerWindow`, TV에 `BroadcastChannelScreen`.
  - 사직서는 "세 번째 채널… 그 두 배" 버전으로 썼다 (사용자가 고르지 않아 Claude가 정함).
- **검증한 것:**
  - Unity 컴파일 에러 0, 플레이 중 새 에러 0.
  - 플레이 모드 캡처로 확인: TV 화면이 원래 비율로 꽉 차고 반사나 경계선이 없다. CH 05와 CH 06(영상 CAM 06과 일치)이 맞게 표시되고, 채널이 바뀔 때 가운데 배너가 뜬다.
  - 키패드: 칸 이름이 보이고, 두 번 틀리면 "HINT 3/6 CH → …"가 뜬다.
  - 데스크톱: 휴지통 4×2 배치, 사직서 뷰어, 새 메모장을 확인했다.
- **검증 못 한 것:**
  - 실제 플레이어가 E로 단서를 조사하는 흐름 (자막은 데이터만 바꿨다).
  - 2인 네트워크(상대 화면의 TV 채널 표시), 빌드.
  - 실제 플레이어 시점에서 본 TV 밝기. 검증은 TV 프레젠테이션 카메라로만 했다.
- Artlist 0크레딧.

### 2026-10-02~03 · 메인 PC (EUNSEO) · #2 탈출구 열림 시네마틱 완성
- **한 일:** 스틸(v1 반려 → v2 승인) → 테이크 A·B·B-2 영상 → VO → 편집 스크립트 `Tools/Claude/render_exit_cinematic.py` → `B1F Escape Sequence.exitVideo` 연결. 위 "현재 상태"의 #2 탈출구 열림 참고.
  - 사용자 피드백: 실제 게임처럼 어둡게(FullPower, 군데군데 조명), 벽은 게임과 같게, 마지막은 어두운 복도로 도망. 돌아서 뛰기보다 카메라가 옆에서 빠르게 따라가는 트래킹. 캐릭터 등신 비율은 인게임 모델에 맞출 것.
  - Unity: B1F 씬은 FullPower 참고 렌더와 모델 렌더 때 잠깐 바꿨다가 원래대로 돌렸다. `exitVideo` 할당만 남아 있다 (**B1F 씬 저장 필요**).
- **검증한 것:** 편집본 형식(Baseline, 21초, 오디오 트랙 1), Unity 임포트(VideoClip 21초 1920×1080, 오디오 1), 스틸 12장으로 자막·지도·전환 위치, 오디오 레벨.
- **검증 못 한 것:** 귀로 듣기, 실제 Exit 단계 재생(2인), 빌드.

### 2026-10-02 · 메인 PC (EUNSEO) · 영상 모델 비교, #2 탈출구 열림 스토리보드 초안
- **한 일:** Artlist 실제 견적으로 시드댄스 2.5/2.0, 클링 3.0, 베오 3.1 라이트를 비교했다 (위 "기본 영상 모델"). 사용자 결정에 따라 기본 모델을 시드댄스 2.0 또는 클링 3.0으로 정했다. #2 탈출구 열림 롱테이크 스토리보드 초안을 텍스트로 작성했다.
- **검증 못 한 것:** 스틸과 영상은 아직 만들지 않았다 (사용자 허가 대기). 크레딧 0.

### 2026-10-02 · 메인 PC (EUNSEO) · 프롤로그 소리 끊김·영상 안 보임 수정
- **증상 (사용자):** Play In Editor를 켜고 MainLobby에서 실행하면 소리가 계속 끊기고 영상이 보이지 않았다.
- **원인:** Editor.log에 `AudioSampleProvider buffer overflow`가 수십 번 찍혔다. VideoPlayer가 소리를 AudioSource로 내보내는 구조였는데, MainLobby 로드 직후 프레임이 튀면서 오디오 버퍼가 넘쳤다. VideoPlayer는 그 소리를 시계로 쓰기 때문에 그림도 멈췄다.
- **수정 (`GamePrologueCinematic`, `render_prologue.py`):** 사운드트랙을 별도 `Prologue_Audio.wav`로 빼고, VideoPlayer 오디오 출력을 껐다. 씬이 안정된 뒤 시작하고, 그림을 소리에 맞춰 재동기화한다.
- **이 PC `.mcp.json` 수정:** 역슬래시가 이스케이프되지 않아 JSON이 깨져 Unity MCP가 세션에 붙지 않았다. 경로를 `/`로 바꿨다. 새 세션부터 적용된다. 그 사이에는 `Temp/ClaudeMcp/umcp.py`로 relay를 호출했다.
- **검증한 것:** MainLobby 플레이 모드에서 67.7초 끝까지 재생됐다. 그림과 소리 차이는 약 0.1초로 일정했다. buffer overflow 경고가 없었다. 화면 캡처로 영상이 보이는 것을 확인했고, 끝난 뒤 오브젝트가 제거됐다.
- **검증 못 한 것:** 귀로 듣는 확인, 빌드 첫 실행.

### 2026-10-01~02 · 메인 PC (EUNSEO) · 게임 시작 프롤로그 시네마틱
- **한 일:** 위 "게임 시작 프롤로그" 참고.
  - 사용자 시네마틱 규칙을 "절대 규칙"에 기록했다.
  - 스토리보드 v1→v6을 만들었다. 매끈한 그림체는 반려됐고, 무광 원단과 실사 배경으로 바꿨다. 1층은 평범한 회사로 위장하고, 로비 직원은 정장을 입는다.
  - 사용자 승인 뒤 영상 3개, 음악, VO를 만들었다. 편집은 ffmpeg 스크립트로 하고, 게임에는 런타임 부트스트랩으로 연결했다.
  - 새 파일: `Scripts/Lobby/GamePrologueCinematic.cs`, `Editor/GamePrologueMenu.cs`, `Tools/Claude/render_prologue.py`, `Resources/Prologue/Prologue_Cinematic.mp4`, `SoundSources/Prologue/*.wav`(7), `Art/Prologue/Source~/`.
- **검증한 것:**
  - Unity 컴파일 에러 0.
  - B1F 플레이 모드에서 `PlayForTesting`으로 재생했다. 30fps로 끝까지 재생되고, 오디오가 나오고, 오버레이 캡처가 정상이며, 끝난 뒤 `AudioListener.pause`가 복원되고 오브젝트가 제거된다.
  - 렌더 스틸 20여 장으로 자막과 그래픽 위치를 확인했다. 오디오 레벨도 확인했다 (전 구간에 소리가 있다).
- **검증 못 한 것:** 귀로 듣는 믹스 확인, 빌드 첫 실행, 스킵 키 입력.

### 2026-10-01 · 노트북 · LobbyF 오프닝 시네마틱
- **한 일:** 위 "LobbyF 오프닝 시네마틱" 참고.
  - 새 파일: `Scripts/Lobby/LobbyIntroCinematic.cs`, `Shaders/HiddenCinematicGrade.shader`, `Tools/Claude/gen_intro_sfx.js`, `Movies/LobbyF/Intro/`(영상 3), `SoundSources/LobbyF/Intro/`(합성음 + VO), `Art/LobbyF/IntroCinematic/`.
  - 팀원 코드 좁은 수정: `SubtitleTrigger`, `SubtitleIntroPresentation` (시네마틱이 끝날 때까지 대기).
  - LobbyF 씬에 `LobbyF Intro Cinematic` 오브젝트를 추가하고 타임라인을 설정했다 (MCP, **저장 안 함**).
  - 사용자 피드백: 캐릭터 후드는 옆으로 넓게 퍼진 납작한 귀(서 있는 귀·롭이어 아님), 근육질이 아닌 정장, 건물은 버려진 평범한 회사로 위장, LobbyF 시작 지점 배경과 이어질 것, 배경보다 플레이어 묘사 우선, 진중한 목소리.
- **검증한 것:**
  - 오프라인 컴파일 에러 0, Unity 콘솔 에러 0.
  - 플레이 모드 캡처: 시작 캡션 → 외관 → 잠금 → 진입 컷 → 로비 + 자막 2줄 → NEXUS 타이틀 → 게임 화면, 이어서 팀원의 무전기 시작 자막이 뜬다.
  - 처음엔 리로드가 꺼진 플레이 진입 때문에 시네마틱이 시작되지 않았고, 팀원 시작 자막과 겹쳤다. 둘 다 고쳤다.
  - 그레인 해시의 정밀도 문제로 생긴 대각선 줄무늬를 고쳤고, 그림자 톤을 바꿔 보라 기운을 줄였다.
- **검증 못 한 것:** 소리 청취, 2인 네트워크, 빌드.
- **추가 (같은 날):** 사용자 요청으로 VO를 영어로 다시 만들었다 (32크레딧). 대사가 길어져 타임라인을 늘렸다. 플레이 모드 AudioSource 로그로 두 VO가 겹치지 않고 순서대로 재생되는 것을 확인했다.

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
