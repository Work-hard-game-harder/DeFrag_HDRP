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
- **디버그:** MainLobby를 거치거나, `B1FStoryDebugCheckpoint`의 *Start After Connect Server*를 쓴다.
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

## 현재 상태 (최신화할 것)

- **완료 (커밋 1caa575까지):**
  - 발전기 B 개편.
  - 모든 미니게임과 터미널 UI 개편, 1회 튜토리얼, 효과음.
  - Connect Server와 배전함 A 협동 방식 개편.
  - 문 파손 2 실시간 컷씬 (약 22.6초).
- **미검증:** 실제 2인 네트워크 플레이.
  - 컷씬: 양쪽 화면에 재생되는지, 끝나고 조작이 복구되는지, 파손된 문을 통과할 수 있는지.
  - 미니게임: 튜토리얼이 1회만 뜨는지, 모니터와 조작자 동기화가 되는지.
- **주의 사항:**
  - 파편은 각 클라이언트에만 있는 장식이라 두 화면에서 위치가 다를 수 있다.
  - `LocalCutsceneStage`의 HUD 숨김 로직은 팀원 코드 `B1FMonsterSpawnTimeline`과 비슷하다. 공용화는 팀원과 상의해야 한다.
- **다음 후보:**
  - 2인 테스트 결과에 따라 연출 타이밍이나 난이도 조정.
  - 사용자가 지시하면 다른 탈출 단계도 실시간 컷씬 슬롯으로 연결 가능.

## Artlist 크레딧 장부

- **누적 사용:** 317 (플랜 16,500 중).
- **추가 예산 사용:** 1000 중 17. 발전기 B BGM `GeneratorColdStart_Loop`와 PA 음성 3종에 썼다.
- **문 파손 2:** 0크레딧 (효과음은 코드로 합성).

## 작업 로그 (최신이 위)

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
