# DeFrag — TV 몬스터 시네마틱 제작 시안 v1

## 목적과 현재 상태

원본 관제실에서 두 번의 문 충격, 파손, TV 몬스터의 등장까지 연결한다. 이 폴더는 참조 이미지와 영상 프롬프트 패키지이며 완성 영상이 아니다. 이미지는 내장 image_gen으로 생성했다. Unity 씬/게임 코드는 변경하지 않았다.

원본의 왼쪽 콘솔, 중앙 홀로그램, 뒤쪽 붉은 문, 오른쪽 기둥 배치를 기반으로 만든 시안이다. 원본과 픽셀 단위로 일치하지 않는다. 바닥 재질과 캐릭터 세부가 재해석됐고, 03 이미지에서는 문 파손 범위가 넓어지고 문 오른쪽 벽 장치 위치가 달라졌다. 게임 최종 삽입용 공간 검증을 통과한 이미지라고 취급하지 않는다. 정확한 배치가 필수라면 같은 카메라의 Unity 캡처로 기준 프레임을 교체한다.

## 입력 프레임

- `01-room-idle.png`: 충격 전 기본 조명과 공간. 모든 숏의 환경 기준.
- `02-second-impact.png`: 같은 구도, 두 번째 충격 직후 닫힌 문과 긴장한 인물. 문 찌그러짐 형태는 시안.
- `03-monster-reveal.png`: 같은 구도에서 파괴된 문과 TV 몬스터. 마지막 분위기와 몬스터 위치 기준. 앞 숏에는 입력하지 않는다.

이미지를 이야기 속 순서대로 단순히 여러 장 업로드하는 것과 시작/끝 프레임 슬롯에 지정하는 것은 다르다. 아래 표대로 해당 슬롯에 직접 설정한다. 각 서비스의 실제 UI가 부여한 이미지/Element 태그를 사용한다. 지원하지 않는 기능을 프롬프트만으로 켰다고 간주하지 않는다.

## 샷 구성 — 총 편집 길이 약 16~20초

| 숏 | 목표 길이 | 시작 | 끝 | 역할 |
|---|---|---|---|---|
| A | 6~8초 | 01 | 02 | 첫 충격, 정적, 두 번째 충격과 작은 반응 |
| B | 5~6초 | A의 승인된 마지막 프레임, 시험 시 02 | 03 | 문 파손, 한 걸음 진입 |
| C | 4~6초 | B의 승인된 마지막 프레임, 시험 시 03 | 미지정 | 움직임을 줄이고 몬스터를 오래 보여 줌 |

서비스 지원 길이에 맞춰 생성하고 편집에서 조정한다. 우선 A 한 개로 공간과 캐릭터를 검증한 뒤 B/C를 생성한다. A가 어긋나면 다음 샷을 계속 생성하지 않는다. 생성 결과에서 승인된 실제 마지막 프레임을 다음 숏 시작으로 쓰면 정지 참조만 이어 쓰는 것보다 접점이 명확해진다.

카메라는 세 숏 모두 같은 위치. 마지막 확대는 원본 해상도가 허용하는 만큼 편집에서 수행한다. 이 확대는 AI가 새 각도의 방을 생성하게 하는 지시가 아니다. 이번 시안에서는 로우 앵글로 떨어지는 연출을 생략한다.

## 공통 프롬프트 — 각 숏 앞에 붙이기

```text
Animate the supplied start frame as an existing stylized 3D game set, not a redesigned location. Preserve the exact camera, room layout, curved red doorway, globe pedestal, consoles, chairs and right foreground pillar. Exactly two short, big-headed cat-hooded players wearing goggles, respirators and dark suits. Keep their original proportions and identities. Acting should show believable weight and small stimulus-driven reactions, not realistic human body proportions. The camera is locked. One continuous shot with no internal cuts. No dialogue, music, captions, logos drawn into the scene, flames, slow motion or full-screen glitch filter. Preserve readable shadows. Only perform the actions described for this shot.
```

## 숏 A — 두 번의 충격

시작 슬롯 01 / 끝 슬롯 02. 몬스터 이미지는 이 숏에 첨부하지 않는다.

```text
The two players work quietly at the existing left console. A single heavy impact strikes the closed red-outlined door from outside. The door jolts inward; a little dust falls. One player's working hand stops. The other turns toward the sound slightly earlier. Hold a short uneasy silence. A second, stronger impact bends the same door panel inward and briefly flickers the light. One player braces a hand against the console; the other shifts weight backward. Small, different, physically grounded reactions, feet planted. No walking sequence or exaggerated arm waving. End with the door still closed and both players wary, matching the supplied end frame. Exactly two separated impacts, no door breach and no monster in this shot. Sound: ventilation, two synchronized heavy knocks and falling dust.
```

## 숏 B — 파손과 진입

시작 슬롯 A 마지막 승인 프레임(시험은 02) / 끝 슬롯 03. 몬스터 참조는 이 숏부터 사용한다.

```text
Continue from the damaged but closed door. After a brief held breath, its already stressed panel tears inward with a sharp metal failure. This is structural collapse after the earlier impacts, not a third knock. Heavy pieces drop promptly to the floor; fine dust hangs briefly near the doorway. The players reflexively lower their bodies beside the console, with minimal movement. Red corridor light enters through the original doorway. Through the clearing dust, the slender black humanoid with a rectangular CRT head steps over the threshold once, moving into the clear floor area to the right of the globe pedestal, and stops in the supplied end-frame position. Two arms, two legs, red-white television static, no human face. Do not let it intersect the pedestal. The globe becomes dimmer but stays fixed. End on a readable stationary monster. Sound: tearing metal, falling fragments, then CRT hiss. No fireball, no chase, no additional impact.
```

## 숏 C — 몬스터를 부각

시작 슬롯 B 마지막 승인 프레임(시험은 03). 끝 프레임은 강제로 동일 이미지를 넣지 않는다.

```text
Hold this exact composition after the breach. The TV-headed creature remains almost completely still, with only a barely perceptible settling of its shoulders. Fine dust settles and the final small fragment lands. Its CRT screen flickers with red-white static. The two players remain tense at the left edge of the scene, with no new gestures. Preserve every object and character position. The red backlight separates the creature's thin silhouette from the doorway. Let the stillness after violence become threatening. Keep the monster clearly visible for the entire shot. Sound gradually resolves to a quiet electrical hum and CRT hiss. No new attack, no camera orbit, no cutaway, no sudden ending title.
```

## Kling과 Seedance 입력 차이

- Kling 3.0 Omni: 실제 시작/끝 프레임 기능과 Elements 기능 중 해당 UI에서 함께 지원하는 조합을 확인한다. 인물/몬스터 Elements를 쓰면 각각 외형만 참조하도록 명시한다. 여러 기능이 모두 동시에 작동한다고 가정하지 않는다.
- Seedance: 이미지/영상/오디오 참조가 가능하지만 서비스별 기능이 다르다. 태그를 쓸 경우 실제 업로드 태그에 'room composition only', 'monster appearance only', 'motion only'처럼 역할을 붙인다. 시작/끝 슬롯이 없으면 01을 장면 기준으로 지정하되, 확정된 끝 프레임 제어와 동등하다고 보지 않는다.
- Artlist: 영상 모델 자체가 아니라 여러 모델을 제공하는 플랫폼. 선택한 모델의 참조 방식과 시작/끝 지원 여부를 확인한다. 새 구독 전에 기존 계정의 기능과 크레딧을 먼저 확인한다.

## 검수 기준

1. 원본 공간: 홀로그램·문·콘솔·벽 장치 위치가 움직이지 않는가?
2. 동작: 손발 접지가 유지되고 두 사람이 서로 다른 작은 반응을 하는가?
3. 사건: 두 번의 충격 뒤 파손인가? 문이 자동문처럼 그냥 열리지 않는가?
4. 괴물: 두 팔·두 다리와 CRT 머리가 유지되고, 문을 통과하며 가구와 충돌하지 않는가?
5. 마지막: 몬스터가 최소 3초 읽히며 먼지/글리치에 가려지지 않는가?

네이티브 음향이 타격 수나 시점을 틀리면 영상까지 다시 생성하지 말고 효과음을 편집에서 맞춘다. 공간과 캐릭터가 틀린 결과는 음향으로 해결하지 않는다.

## 기능 확인 출처 (2026-09-26)

- https://kling.ai/quickstart/klingai-video-3-omni-model-user-guide
- https://seed.bytedance.com/en/seedance2_0
- https://help.artlist.io/hc/en-us/articles/33161438828957-AI-Toolkit-Generating-AI-Videos

## 이미지 생성 지시 요약

01: 원본 게임 와이드 이미지의 카메라와 모든 물체 위치를 유지하고 과노출을 낮춘 청록색 호러 조명으로 편집.
02: 01의 같은 구도에서 닫힌 문만 충격으로 손상시키고, 두 인물의 작은 경계 반응과 조명 감쇠를 추가.
03: 02의 같은 구도에 문 파손과 붉은 역광을 추가하고, 별도 몬스터 참조에서 외형만 가져와 문을 넘어선 위치에 배치. 지구본은 위치를 유지하고 발광만 낮춤.
