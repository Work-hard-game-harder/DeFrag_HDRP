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

## 현재 상태 (최신화할 것)

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

- **누적 사용:** 583 (플랜 16,500 중).
- **락커·TV 몬스터 (2026-09-28, 별도 예산 500 중 266):**
  - 몬스터 음성 묶음(MONSTER) 53, 라디오 속삭임(VINTAGE_RADIO, 한국어) 24, 플레이어 헐떡임 39, Lyria 3 긴장 BGM 150.
  - 음성은 ElevenLabs v3를 썼다 (몬스터는 voice "Shadow", 플레이어는 "Mild").
- **추가 예산 사용:** 1000 중 17. 발전기 B BGM `GeneratorColdStart_Loop`와 PA 음성 3종에 썼다.
- **문 파손 2:** 0크레딧 (효과음은 코드로 합성).

## 작업 로그 (최신이 위)

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
