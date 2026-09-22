# B1F 영상 이벤트 설치

Play Mode를 종료하고 메인 에디터에서 설정한다. B1F 씬에 사용자가 편집 중인 변경이 있어 이 기능은 씬을 자동 수정하지 않는다.

1. 항상 활성 상태인 빈 루트 `B1F Escape Sequence`를 만들고 NetworkObject, B1FEscapeSequence, B1FEscapePresentation을 추가한다. 전력별로 꺼지는 오브젝트 밑에 두지 않는다. 씬 NetworkObject이므로 런타임 AddComponent로 추가하지 않는다.
2. Sequence의 Connection에 기존 ConnectServerCoordinator, Power에 기존 B1FPowerController, Presentation에 방금 추가한 컴포넌트를 연결한다.
3. 영상 슬롯: Warning Video=혜준 경고 영상, Approach Video=영주 접근 영상, Impact Video=서연 첫 충돌 영상, Breach Video=은서 파손 영상, Exit Video=비상구 개방 영상. 빈 슬롯은 기본 4초짜리 검은 안내 화면으로 동작하여 자산 없이 흐름을 테스트할 수 있다. 각 플레이어의 종료 응답을 기다리며, 영상 실패/응답 누락 시 제한 시간 뒤 계속한다.
4. Intact Door/Broken Door는 정상 문과 파손 문(콜라이더 포함)의 시각 루트다. 하위에 NetworkObject를 넣지 않는다. 서버와 클라이언트가 같은 상태로 교체한다. Dust/Audio/Clip은 선택 항목이다. 영상 속 소리와 중복되면 Breach Sound는 비워 둔다. 작은 파편은 Broken Door의 자식으로 배치하거나 Dust의 sub-emitter로 제작한다.
5. On Approach Server / On Breach Server / On Escape Ready Server는 서버에서만 실행되는 UnityEvent다. 실제 몬스터 이동, 기존 문 서버 개방 함수를 연결한다. 실제 몬스터의 공격 일시정지는 기존 AI의 서버 제어 API에 연결해야 한다. 본 뼈대는 AI 이동/피격 정책을 변경하지 않는다.
6. QuestManager의 순서를 아래처럼 구성한다. 모든 단계 Shared Party, Immediate, Target Count=1. 기존 Connect Server와 Generator B 완료 신호는 유지한다.

| 순서 | Quest Id | Required Signal | 제목 예시 |
|---|---|---|---|
| 기존 | b1f_connect_server | B1F_CONNECT_SERVER_COMPLETED | 관제실 서버에 연결하라 |
| 추가 | b1f_download_initial | B1F_DOWNLOAD_INTERRUPTED | 서버 데이터를 다운로드하라 |
| 기존 | b1f_full_power | B1F_GENERATOR_B_COMPLETED | 발전기를 찾아 시설 전력을 전부 복구하라 |
| 추가 | b1f_download_resumed | B1F_DOWNLOAD_COMPLETED | 서버 다운로드가 완료될 때까지 기다려라 |
| 기존/추가 | b1f_escape | B1F_EXIT_REACHED | 비상구로 탈출하라 |

7. B1FPowerController의 PowerOff/EmergencyPower/FullPower 루트가 실제 조명 루트에 연결되어야 한다. 이야기 정전은 PowerOff로 전환하되 발전기 복구를 허용한다. Sequence와 Generator는 이 루트들의 자식이면 안 된다.
8. 기존 CinematicSceneTrigger의 Escape Sequence에 이 Sequence를 연결한다. 연결하면 EscapeReady 전에는 B2F 이동을 거절한다. Destination Scene과 기존 전환 영상도 연결한다. 비상구 영상만으로 씬 이동하지 않으며 플레이어가 탈출 트리거에 닿으면 기존 전환을 사용한다.

기본 진행: 경고 → 다운로드 35% → 접근/충돌/파손 영상 → 중단 안내 2초 → 전체 정전/발전기 퀘스트 → FullPower 확인 → 35%부터 재개 → 100% → 비상구 영상 → 탈출 퀘스트/서버 문 개방 이벤트.

## 검증

- 호스트와 Player2를 재시작하고 디버그 체크포인트를 끈 상태에서 Connect Server 완료부터 확인한다. 기존 after-connect 디버그는 퀘스트 단계를 건너뛰므로 본 스토리 테스트에는 사용하지 않는다.
- 두 화면의 다운로드율과 영상 순서, 영상 중 이동 잠금 및 종료 후 조작 복구를 확인한다.
- 모든 영상을 비워도 발전기 단계로 진행되어야 한다.
- 파손 뒤 두 화면의 문 콜라이더와 시각 모델이 일치해야 한다.
- 정전 상태에서 연료통이 생성되고 발전기 두 통 완료 후 FullPower 및 다운로드 재개가 되어야 한다.
- 탈출 준비 전에는 트리거가 작동하지 않고 마지막 영상 후에만 작동해야 한다.

현재 뼈대는 영상의 프레임 단위 동기화 대신 단계와 종료 대기를 동기화한다. 영상 준비 속도에 따라 시작에 약간 차이가 날 수 있다. 추가 CCTV 모니터 출력, 몬스터 무적/피격 정지, 파편 물리 연출은 별도 연출 연결 항목이다.
