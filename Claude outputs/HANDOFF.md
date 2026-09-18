# HANDOFF — project-Card (Unity MR 카드 게임) 버그 수정 작업

마지막 갱신: 2026-09-18 (세션 사용량 한도 도달 직전 작성)

## 0. 작업 대상
- 저장소: `project-Card` (사용자 컴퓨터, 경로 `C:\Users\user\Documents\GitHub\project-Card`)
- 엔진: Unity 6000.3.11f1 (Unity 6.3 LTS)
- 씬: `Assets/.../KJH_CardTest.unity`
- 핵심 오케스트레이터: `GameController` 게임오브젝트의 `DemoTurnController` 컴포넌트
- 작업 방식: 원격 데스크톱 브리지(`mcp__remote-devices__*`)로 사용자 PC에 직접 붙어 Unity 에디터를 컴퓨터 사용(computer-use)으로 조작. 별도의 커맨드라인/CI 테스트 파이프라인은 이 프로젝트에 없음 — 검증은 전부 Unity 에디터 Play 모드 수동(자동화 클릭) 테스트로 수행.

## 1. 목표와 완료 조건

사용자가 보고한 버그 3건을 고치는 것이 목표였음 (원문):
> "턴 종료시 적 하수인을 안떄리고 그래서 하수인 체력이 0으로 가면 없어져야 하는데 안그러잖아 그리고 턴이 넘어가면 드로우를 했을때 반대에서 카드가 나와야지 그리고 코스트가 1이 남아 있는데 카드를 못내 지금"

즉:
1. 턴 종료 시 전투가 일어나 상대 하수인 체력이 0이 되면 필드에서 제거돼야 함
2. 턴이 넘어가면 Draw 버튼을 눌렀을 때 그 턴 플레이어 쪽(반대편) 덱에서 카드가 나와야 함
3. 코스트가 남아있는데 카드를 못 내는 현상이 없어야 함

**완료 조건**: 위 3개 증상이 Play 모드에서 여러 턴에 걸쳐 재현되지 않고, 정상 동작(드로우 진영 전환, 코스트 소모 후 배치, 전투로 인한 사망/제거)이 확인되는 것.

## 2. 완료한 항목

### 2-1. (이번 세션 이전에 이미 완료) FieldSlot 콜라이더 겹침 버그
- `CardMove.FindFieldSlot()`이 `Physics.OverlapSphere` 결과 중 "가장 가까운" `FieldSlot`을 고르도록 수정됨 (겹치는 콜라이더 중 첫 번째를 그냥 쓰면 엉뚱한 슬롯이 선택되는 문제였음).
- 이번 세션에서 코드 재확인만 함, 추가 수정 없음.

### 2-2. 버그 1 — 하수인이 죽어도 안 없어짐
- **원인 진단**: 전투 로직 자체(`GameManager.HandleCombat()`, `CardInstance.TakeDamage()`, `PlayerBoardState.RemoveDeadCards()`, `DemoTurnController.SyncDeadMinions()`)는 코드 리뷰 결과 이미 정상이었음. 실제 원인은 2-4번(버그 3)의 손패/코스트 데이터 꼬임 때문에 애초에 하수인을 제대로 필드에 낼 수 없었던 것 — 2-4를 고치고 나서 별도 코드 수정 없이 해결됨.
- **검증**: 같은 라인에 하수인 두 개를 마주 세우고 End Turn을 반복 → 공격력1/체력2 하수인이 2번 피격 후 데이터상 사망 + 필드 비주얼에서도 `Destroy()`로 사라지는 것까지 확인함.

### 2-3. 버그 2 — 턴이 바뀌어도 항상 내 쪽에서만 카드가 나옴
- **원인**: `MyDrawButton`/`EnemyDrawButton`이 각각 자기 쪽 `DrawManager.OnDrawButtonClick()`에 직접 연결되어 있어서, 버튼을 누르면 무조건 그 버튼이 가리키는 고정된 덱에서만 카드가 나왔음. 실제 턴 플레이어가 누구인지와 무관했음.
- **수정**: `DrawManager`에 `PlayerSide side` 필드 추가. `DemoTurnController.OnDrawButtonClicked()`를 새로 만들어 `gameManager.State.CurrentPlayer`를 확인한 뒤 `TryDrawCard(current)`를 호출하고, 성공하면 해당 진영의 `DrawManager`(myDrawManager/enemyDrawManager)로만 비주얼을 스폰하도록 변경.
- **에디터 작업**: `MyDrawButton`과 `EnemyDrawButton` 둘 다 `Button.OnClick()`을 `DrawManager.OnDrawButtonClick` → `DemoTurnController.OnDrawButtonClicked`로 재연결함. `EnemyDeck`의 `Side` 필드가 기본값이 잘못 "Me"로 되어 있어서 "Opponent"로 수동 변경함.
- **검증**: Me 턴에 드로우 → 내 손패 위치에 카드 생성 확인. End Turn 후 Opponent 턴에 드로우 → 반대편(상대) 손패 위치에 카드 생성 확인. 여러 턴 반복해도 진영이 계속 올바르게 전환됨.

### 2-4. 버그 3 — 코스트는 남아있는데 카드를 못 냄
두 가지 원인이 겹쳐 있었음:
- **원인 A**: `GameManager.HandleTurnStart()`가 턴마다 몰래 자동으로 드로우를 하고 있었는데, 이게 화면에 보이는 `DrawManager`쪽 비주얼과 전혀 연결이 안 되어 있어서 "실제 손패는 비어있는데 화면엔 카드가 있어 보이는" 식으로 어긋났음.
  - **수정**: `HandleTurnStart()`에서 자동 드로우 제거. `GameManager.TryDrawCard(PlayerSide side)` 퍼블릭 메서드를 추가해서, Draw 버튼을 눌렀을 때만 `DemoTurnController`가 명시적으로 호출하도록 변경 (2-3 항목과 연동됨).
- **원인 B**: `OnCardChecker.manuallyOccupied`가 한 번 `true`로 설정된 뒤 리셋되는 코드가 없어서, 손패 자리 하나를 한 번이라도 쓰면 카드가 떠난 뒤에도 영원히 "차있음" 상태로 남았음. 자리 8개를 다 소진하면 그 이후로는 Draw를 눌러도 카드가 아예 안 나옴(또는 겹쳐서 나옴).
  - **수정**: `OnCardChecker.OnTriggerExit()`에서 `manuallyOccupied = false`로 리셋하도록 추가.
- **검증**: 여러 턴에 걸쳐 "천천히, 신중하게"(카드 선택 클릭 → 약 1초 대기 → 목표 슬롯 클릭) 방식으로 드로우+배치를 반복 → 매번 코스트가 정확히 1씩 소모되고 카드가 손패에서 필드로 이동함. 막히는 현상 없음.

### 2-5. 씬 저장
- 위 버튼 재연결 및 참조 할당을 마친 뒤 `Ctrl+S`로 `KJH_CardTest.unity` 저장 완료 (Hierarchy 탭의 `*` 표시 사라진 것으로 확인).
- 컴파일 에러/경고 0건 확인 (Unity 콘솔 아이콘 카운트: 메시지 다수 / 경고 0 / 에러 0).

## 3. 남은 항목 (우선순위 순)

1. **[미확인 / 재현 조건 불확실] 매우 빠른 연속 클릭 시 카드가 손패에 걸려 배치가 안 되는 것처럼 보이는 현상**
   - 이번 세션 후반부에, 자동화 클릭을 빠르게 연속으로(특히 "카드 선택 → 빈 땅 클릭으로 취소"라는 탐색성 클릭을 낀 뒤) 했을 때, 카드 2장이 손패에 남아 어느 것도 필드에 배치되지 않고 코스트도 소모되지 않는 상태가 한 번 관찰됨.
   - 이후 "선택 클릭 → 약 1초 대기 → 배치 클릭"으로 천천히, 여러 턴(랜덤 선공 포함) 반복했을 때는 단 한 번도 재현되지 않고 매번 정상 동작함.
   - **결론(미확정)**: 자동화 클릭이 사람보다 훨씬 빠르게 연속으로 들어가면서 `CardMove.Update()`의 `wasPressedThisFrame` 판정이나 `selectedCard` 상태 머신이 프레임 타이밍상 꼬였을 가능성이 높다고 추정하나, 실제 사람이 마우스로 빠르게 두 번 클릭했을 때도 동일 현상이 나는지는 **확인하지 못함**. 다음 세션에서 가장 먼저 재확인이 필요한 항목.
   - 만약 실제 사람 조작에서도 재현된다면: `board.hand.Count == 0`인데도 화면에 카드가 남아있는 상태가 어떻게 생기는지 (DrawManager가 TryDrawCard 성공 여부와 무관하게 비주얼을 중복 스폰하는 경로가 있는지, 혹은 CardMove의 `selectedCard`가 두 번째 클릭에서 갱신되지 않고 유지되는 레이스가 있는지) 디버그 로그를 임시로 추가해서 정밀 진단 필요.

2. **미구현 상태로 알려져 있는 것 (원래부터 스코프 밖, 코드 주석에도 명시됨)**
   - Battlecry, Cleave 등 카드 능력치 시스템 전혀 없음 — 데모에서 의도적으로 제외된 것으로 보임(주석: "필요한 능력(Battlecry, Cleave 등)은 이번 데모에서 전부 뺐음 — 나중에 따로 붙일 예정").
   - 필드에 나오는 모든 하수인이 원본 카드 스탯과 무관하게 공격력1/체력2로 고정되는 데모 규칙 (의도된 동작, `DemoTurnController.TryPlaceMinion` 참고).
   - `turnText` 등 UI 텍스트가 한글 미지원 TMP 폰트(Liberation Sans SDF)라서 영어로만 표시됨 — 코드 주석에 명시된 기존 알려진 제약.

3. **[미확인]** 사용자가 `testDeckMe` / `testDeckOpponent`에 실제로 어떤 `CardData` 에셋을 넣어뒀는지 내용은 확인하지 않음 — 비어있으면 드로우가 계속 실패하고 게임이 바로 무승부로 끝나므로, 다음 세션에서 Play 테스트 전에 Inspector에서 두 리스트가 비어있지 않은지 확인 권장.

## 4. 변경한 파일과 변경 이유

| 파일 | 변경 내용 | 이유 |
|---|---|---|
| `Assets/1. Script/GameLogic/Turn/GameManager.cs` | `HandleTurnStart()`에서 자동 드로우 제거, `public bool TryDrawCard(PlayerSide side)` 추가 | 자동 드로우가 화면 비주얼과 무관하게 일어나서 손패 데이터-비주얼 불일치(버그3 원인A) 유발 → 명시적 드로우로 전환 |
| `Assets/1. Script/DemoScene/DrawManager.cs` | `[SerializeField] private PlayerSide side; public PlayerSide Side => side;` 필드 추가 | `DemoTurnController`가 어느 `DrawManager`가 내 덱/상대 덱인지 구분할 수 있도록 |
| `Assets/1. Script/DemoScene/DemoTurnController.cs` | `myDrawManager`/`enemyDrawManager` 필드 추가, `OnDrawButtonClicked()` 메서드 신규 작성 | 실제 `CurrentPlayer` 기준으로 올바른 진영의 덱에서 드로우 + 올바른 진영의 비주얼만 스폰 (버그2 수정 핵심) |
| `Assets/1. Script/DemoScene/OnCardChecker.cs` | `OnTriggerExit()`에서 `manuallyOccupied = false` 추가 | 손패 자리 점유 표시가 카드가 떠난 뒤에도 영구히 풀리지 않던 버그(버그3 원인B) 수정 |
| `Assets/1. Script/DemoScene/CardMove.cs` | (이전 세션에 완료, 이번 세션은 재확인만) `FindFieldSlot()`이 최근접 슬롯 선택 | FieldSlot 콜라이더가 슬롯 간격보다 훨씬 커서 겹치는 문제 해결 |
| `Assets/.../KJH_CardTest.unity` (씬) | `MyDrawButton`/`EnemyDrawButton`의 `OnClick()` 재연결(→`DemoTurnController.OnDrawButtonClicked`), `EnemyDeck.Side`를 "Opponent"로 수정, `DemoTurnController`의 `myDrawManager`/`enemyDrawManager` 참조 연결 | 위 코드 변경을 실제로 씬에서 동작하게 하기 위한 에디터 배선 작업 |

## 5. 실행한 테스트 명령어와 마지막 결과

- 이 프로젝트에는 커맨드라인 유닛 테스트/CI 파이프라인이 **없음** (미확인 — Unity Test Runner 관련 파일이 보였으나(`[TestRunnerTools] Callbacks registered after domain reload` 콘솔 로그) 실제 테스트 스위트를 실행하지는 않았음).
- 대신 다음 방식으로 검증함: Unity 에디터에서 `Ctrl+R`(재컴파일) 후 콘솔 에러/경고 카운트 확인 → **마지막 결과: 에러 0, 경고 0**.
- 기능 검증은 전부 Play 모드 진입 후 컴퓨터 사용(마우스 클릭)으로 Draw/카드배치/End Turn을 수동 반복하는 방식. 자동화된 스크립트 테스트는 아님.

## 6. 현재 실패 중인 것/알려진 문제

- 위 "3-1" 항목 참고: 매우 빠른 연속 클릭 시 손패에 카드가 걸려 배치가 안 되는 현상이 한 번 관찰됨. **정상 속도(사람 조작에 가까운 속도) 클릭에서는 재현되지 않음.** 실제 버그인지 자동화 클릭 아티팩트인지 미확정.
- 그 외에 알려진 실패 상태 없음 (컴파일 에러 없음, Play 모드 정상 진입/종료 확인됨).

## 7. 다음 세션이 가장 먼저 할 일

1. (커맨드 없음 — GUI 프로젝트) Unity Hub에서 `project-Card`를 **Unity 6000.3.11f1**로 연다 (다른 버전으로 잘못 열리지 않도록 주의 — 이 머신에 6000.6.0f1도 설치되어 있어서 헷갈리기 쉬움).
2. `KJH_CardTest.unity` 씬이 열려 있는지 확인 (이미 저장된 상태이므로 `*` 표시 없어야 정상).
3. Play 모드 진입 후, **일부러 빠르게 연속 클릭**해서 (카드 선택 직후 바로 빈 땅 클릭 "취소" → 바로 다시 카드 선택 → 배치, 텀 없이) 섹션 3-1의 손패 걸림 현상이 재현되는지 다시 시도. 재현되면 `CardMove.cs`/`DemoTurnController.TryPlaceMinion`/`DrawManager.OnDrawButtonClick`에 임시 `Debug.Log`를 넣어 `board.hand.Count`와 `selectedCard` 상태를 프레임 단위로 추적.
4. 문제 없으면 사용자에게 최종 확인 요청하고 마무리.

## 8. 절대 되돌리면 안 되는 사용자(또는 이번 세션) 변경사항

- `KJH_CardTest.unity` 씬의 `MyDrawButton` / `EnemyDrawButton` `OnClick()` 바인딩 — 반드시 `DemoTurnController.OnDrawButtonClicked`를 가리켜야 함. (예전 값인 `DrawManager.OnDrawButtonClick`으로 되돌리면 버그2가 재발함)
- `EnemyDeck` 게임오브젝트의 `DrawManager.Side` 값 = **Opponent** (기본값 "Me"로 되돌리면 안 됨)
- `DemoTurnController`의 `myDrawManager` → `MyDeck`, `enemyDrawManager` → `Enemy/EnemyDeck` 참조 연결
- `GameManager.HandleTurnStart()`에서 자동 드로우 코드를 다시 추가하면 안 됨 (버그3 원인A 재발)
- `OnCardChecker.OnTriggerExit()`의 `manuallyOccupied = false` 리셋 라인 제거 금지 (버그3 원인B 재발)
- 사용자가 `testDeckMe`/`testDeckOpponent` Inspector 리스트에 직접 넣어둔 `CardData` 에셋 항목들 (내용 미확인이지만 임의로 비우거나 순서를 바꾸지 말 것)

---

## 다음 세션 시작 프롬프트 (그대로 붙여넣기용)

```
project-Card (Unity MR 카드 게임, C:\Users\user\Documents\GitHub\project-Card, Unity 6000.3.11f1,
씬 KJH_CardTest.unity)의 버그 수정 작업을 이어서 진행해줘.

먼저 /home/claude/handoff/HANDOFF.md (또는 전달받은 동일 파일)를 읽고 지금까지 상황을 파악해줘.
이전 세션에서 사용자가 보고한 3가지 버그(턴 종료 시 하수인 안 죽음 / 드로우가 항상 내 쪽에서만
나옴 / 코스트 남았는데 카드 못 냄)는 모두 코드 수정 + 씬 재배선을 마치고 "천천히 클릭하는" 방식의
Play 모드 테스트로는 정상 동작을 확인했어.

다만 한 가지 미해결 항목이 있어: 세션 후반부에 매우 빠르게 연속 클릭했을 때 카드가 손패에 걸려
배치가 안 되는 현상이 한 번 관찰됐는데, 정상 속도로는 재현이 안 됐어. HANDOFF.md 7번 항목대로
이 현상이 실제 사람 조작(빠른 연속 클릭)에서도 재현되는지 먼저 확인해줘. 재현되면 관련 스크립트
(CardMove.cs, DemoTurnController.cs, DrawManager.cs)에 임시 디버그 로그를 넣어서 정밀 진단하고
고쳐줘. 재현 안 되면 사용자에게 그렇게 보고하고 마무리해줘.

HANDOFF.md 8번(절대 되돌리면 안 되는 변경사항) 꼭 지켜줘 — 특히 두 Draw 버튼의 OnClick 바인딩,
EnemyDeck.Side="Opponent", GameManager의 자동 드로우 제거 상태, OnCardChecker의 manuallyOccupied
리셋 코드는 실수로라도 원복되면 안 돼.
```
