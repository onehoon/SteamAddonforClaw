# Steam Addon for Claw — 한국어 사용 가이드

Steam Addon for Claw는 지원되는 MSI Claw 핸드헬드의 내장 컨트롤러에 Steam Input과 Steam Deck 스타일의 컨트롤러 통합 기능을 제공합니다.

Addon이 컨트롤러 권한을 보유하는 동안(MSI Center M 비활성화 상태) 내장 MSI 컨트롤러를 항상 하나의 가상 컨트롤러로 Windows에 표현합니다. Steam 게임이나 Steam Big Picture 세션이 활성 상태이면 가상 Steam Deck 컨트롤러로, 그렇지 않으면 가상 Xbox 360 컨트롤러로 표현됩니다. 이 동작에는 사용자 스위치가 없으며 Steam/Big Picture 상태에 따라 자동으로 결정됩니다.

Center M 버튼 리매핑, Device 단위 CPU Boost 및 TDP 제어, 게임별 성능 프로파일, Steam Quick Access Menu의 성능 제어 기능은 독립적인 기능입니다.

## 지원 기기

| 기기 | Board ID | 지원 상태 |
| --- | --- | --- |
| MSI Claw 7 AI+ A2VM | `MS-1T42` | 지원 |
| MSI Claw 8 AI+ A2VM | `MS-1T52` | 지원 |
| MSI Claw 8 EX AI+ CG3EM | `MS-1T91` | 지원 |

Addon은 MSI의 정확한 Board ID를 기준으로 지원 모델을 식별합니다. 지원하지 않거나 식별할 수 없는 하드웨어에서는 컨트롤러 Runtime이 시작되기 전에 종료되며, 컨트롤러 또는 성능 관련 변경을 적용하지 않습니다.

### 실제 하드웨어 검증 상태

- **MSI Claw 8 EX AI+ CG3EM (`MS-1T91`)** — 실제 하드웨어에서 테스트 완료.
- **MSI Claw 7 AI+ / 8 AI+ A2VM (`MS-1T42`, `MS-1T52`)** — 소프트웨어상 지원되지만 실제 기기 검증은 아직 진행 전입니다.

## 요구 사항

- Windows 11 x64
- 위 목록에 포함된 지원 MSI Claw 모델
- Steam Deck 컨트롤러 표현, 게임별 Steam 프로파일, Quick Access Menu 통합 기능을 사용하려면 Steam이 설치되어 있고 실행 중이어야 함
- **순정 MSI Center M이 설치되어 있고 정상적으로 사용 가능한 기본 MSI 컨트롤러 환경**

> [!CAUTION]
> **Steam Addon for Claw는 순정 MSI Center M이 설치된 기본 컨트롤러 환경만 지원합니다.**
>
> **Handheld Companion, ClawTweaks 또는 이와 유사한 프로그램과 병행 사용하지 마십시오.** 내장 컨트롤러를 관리하거나, 컨트롤러 모드를 변경하거나, 별도 Routing/가상 컨트롤러 소유권을 제공하는 프로그램과의 동시 사용은 지원하지 않습니다. 이러한 환경에서는 컨트롤러 소유권, Routing, 복구 또는 종료(Teardown) 경로가 서로 충돌할 수 있습니다.

## 주요 기능

- Steam 게임 및 Steam Big Picture Mode에서 자동 Steam Deck 컨트롤러 표현
- Steam 게임 및 Big Picture Mode 외 상태에서 자동 Xbox 360 컨트롤러 표현
- 가상 Steam Deck 컨트롤러 출력 (`VID 28DE`, `PID 1205`)
- 내장 컨트롤러 버튼, 스틱, 트리거, D-pad, 후면 버튼 매핑
- 물리 진동(Rumble) 지원
- Center M의 일반 동작(Normal Action) 사용자 설정 가능
- 독립 기능인 Device 단위 CPU Boost 및 TDP 제어
- 독립 기능인 게임별 CPU Boost 및 TDP 프로파일
- 주기적인 게임/프로세스 Polling 없이 이벤트 기반으로 Steam 게임 감지
- 설치된 Steam 게임 및 Steam에 추가한 Non-Steam 게임별 프로파일
- Steam Quick Access Menu 제어 기능
- 애플리케이션 시작 시 자동 Silent 업데이트 확인
- 백그라운드 트레이 동작 및 Lifecycle 복구

## Steam 컨트롤러 표현

MSI Center M이 비활성화되어 있는 동안 Addon이 컨트롤러 권한을 보유하며 항상 하나의 가상 컨트롤러를 표현합니다. 어떤 컨트롤러를 표현할지는 자동으로 결정됩니다.

```text
MSI Center M 비활성화  →  Steam Addon for Claw 컨트롤러 권한
        │
        ├── Steam 게임 실행 중 또는 Steam Big Picture 활성  →  가상 Steam Deck 컨트롤러
        │
        └── 그 외                                          →  가상 Xbox 360 컨트롤러
```

이 동작에는 사용자 설정 스위치가 없으며 실시간 Steam / Big Picture 상태를 따릅니다.

순정 MSI 컨트롤러 환경으로 되돌리려면 **Controller** 탭에서 MSI Center M을 다시 활성화합니다.

## 컨트롤러 매핑

MSI Claw 내장 컨트롤러의 입력은 다음과 같이 가상 Steam Deck 컨트롤러에 매핑됩니다.

| MSI Claw 입력 | Steam Deck 출력 |
| --- | --- |
| A / B / X / Y | A / B / X / Y |
| D-pad | D-pad |
| LB / RB | L1 / R1 |
| LT / RT 아날로그 입력 | L2 / R2 아날로그 트리거 |
| LT / RT 완전 입력(Full Pull) | L2 / R2 디지털 Full Pull |
| 왼쪽 스틱 | 왼쪽 스틱 |
| 오른쪽 스틱 | 오른쪽 스틱 |
| L3 / R3 | L3 / R3 |
| Menu / Start | Menu |
| View / Back | Options |
| 오른쪽 후면 M1 버튼 | R4 |
| 왼쪽 후면 M2 버튼 | L4 |

Motion / Gyro 출력은 현재 지원되는 컨트롤러 매핑에 포함되지 않습니다.

## Center M 버튼 리매핑

**Controller** 탭에는 Center M 버튼 설정이 있습니다.

Center M 리매핑은 Addon이 관리하며 UI에는 **Always enabled**로 표시됩니다. 사용자가 수정할 수 있는 **Normal Action**은 Center M 버튼을 눌렀을 때 수행할 동작을 결정합니다.

사용 가능한 Normal Action은 다음과 같습니다.

- **None**
- **Steam Big Picture** — 기본 동작
- **Keyboard / Hotkey** — Ctrl, Shift, Alt, Win 조합 키와 하나의 키를 조합할 수 있음
- **Launch Application** — 선택한 `.exe`를 실행하며 필요하면 실행 인수(arguments)도 지정 가능

### MSI Center M과 컨트롤러 소유권

MSI Center M 애플리케이션은 물리 컨트롤러 모드를 변경할 수 있습니다. Addon이 컨트롤러 권한을 보유하는 동안(MSI Center M 비활성화) Addon은 Center M이 예기치 않게 컨트롤러를 다시 가져가지 못하도록 컨트롤러 소유권을 보호합니다.

순정 환경으로 컨트롤러 권한을 되돌리려면 **Controller** 탭에서 MSI Center M을 다시 활성화합니다. 이 변경을 적용하기 위해 Windows가 재시작됩니다.

## Device 탭

**Device** 탭에는 핸드헬드 전체에 적용되는 기본 성능 설정이 있습니다.

Device CPU Boost와 TDP Control은 독립적인 기능이며 컨트롤러 표현의 영향을 받지 않습니다.

이 설정은 우선 적용되는 활성 게임 프로파일이 없을 때 사용하는 일반적인 Device 단위 설정입니다.

### CPU Boost

CPU Boost는 독립적으로 활성화할 수 있으며 다음 두 전원 상태를 각각 설정할 수 있습니다.

- **Plugged in** — 전원 연결 상태
- **On battery** — 배터리 사용 상태

Windows에서 제공하는 프로세서 Boost 모드를 UI에서 직접 선택할 수 있습니다.

### TDP Control

TDP Control에서는 AC와 배터리 상태에 대해 다음 값을 각각 설정할 수 있습니다.

- **PL1**
- **PL2**

선택 가능한 범위는 감지된 Claw 모델에서 지원하는 범위로 제한됩니다.

처음 사용할 때 기존 MSI Center M의 Manual TDP 값이 확인되면 Addon은 그 값을 초기 TDP 설정값으로 사용할 수 있습니다.

### Device 기능을 끈다는 의미

**CPU Boost** 또는 **TDP Control**을 끈다는 것은 해당 기능을 Device 단위에서 더 이상 Addon이 관리하지 않는다는 의미입니다.

이는 "Addon이 시작되기 전의 값으로 복원"하는 명령이 아닙니다. 저장된 설정값은 그대로 유지되며, 해당 기능을 다시 활성화하면 다시 사용할 수 있습니다.

## Profile 탭

**Profile** 탭에서는 게임별 CPU Boost 및 TDP 설정을 관리할 수 있습니다.

게임 목록은 다음 항목으로 구성됩니다.

- 설치된 Steam 게임
- Steam에 등록된 Non-Steam 바로가기(shortcut)

Addon이 이미 실행 중인 상태에서 새 게임을 설치하거나 새로운 Non-Steam 게임을 추가했다면 **Refresh**를 사용하여 목록을 다시 읽을 수 있습니다.

### 게임 프로파일 만들기

1. **Profile**을 엽니다.
2. 검색하거나 목록에서 게임을 선택합니다.
3. 게임 선택 영역 옆의 토글을 사용해 해당 프로파일을 활성화합니다.
4. 전원 연결 상태와 배터리 상태 각각에 대해 CPU Boost를 설정합니다.
5. 전원 연결 상태와 배터리 상태 각각에 대해 TDP PL1 / PL2를 설정합니다.

프로파일은 일부 항목만 Device 설정을 상속받는 부분 override 방식이 아니라, 게임별로 완전한 성능 설정 세트를 가지는 구조입니다.

게임 프로파일을 처음 활성화하면 가능한 경우 저장되어 있는 Device 설정값이 초기값으로 복사됩니다. 이후부터는 해당 게임이 자신의 별도 저장값을 유지합니다.

프로파일을 비활성화해도 해당 게임의 저장된 설정은 삭제되지 않습니다.

### Device와 Profile 우선순위

활성화된 프로파일이 있는 게임이 실행 중이면 다음과 같이 동작합니다.

```text
활성화된 게임 Profile
        ↓ 우선 적용
활성화된 Device 설정
```

게임이 종료되면 다음 규칙을 사용합니다.

- 해당 Device 기능이 활성화되어 있으면 저장된 Device 값이 다시 유효한 설정이 됩니다.
- 해당 Device 기능이 비활성화되어 있으면 Addon은 예전의 게임 실행 전 값으로 복원하려 하지 않고 해당 기능에 대한 관리를 중단합니다.

성능 프로파일은 실제 Steam AppID를 사용하며 컨트롤러 표현과 독립적으로 동작합니다.

## 게임 감지 및 Non-Steam 게임

Steam Addon for Claw는 게임을 감지하기 위해 실행 중인 프로세스를 지속적으로 Polling하지 않습니다.

Steam 게임 감지는 이벤트 기반으로 동작합니다. Addon은 Steam의 `RunningAppID` 상태 변경을 감시하고, Steam이 게임의 시작, 변경 또는 종료를 보고할 때 그 변경에 반응합니다.

따라서 실행 중인 `.exe` 파일을 반복적으로 스캔하거나, Steam 게임이 실행 중인지 일정 주기로 계속 확인하지 않습니다.

감지된 Steam AppID는 서로 독립적인 두 가지 용도로 사용됩니다.

- **컨트롤러 표현** — 감지된 Steam 세션(또는 Big Picture)이 있으면 Xbox 360 컨트롤러 대신 가상 Steam Deck 컨트롤러를 표현합니다.
- **Performance Profiles** — CPU Boost와 TDP 프로파일은 실제 실행 중인 Steam AppID를 사용합니다.

```text
Steam RunningAppID
        │
        ├── Steam 게임 또는 Big Picture 활성? ──→ 가상 Steam Deck 표현
        │
        └── 활성화된 Profile 일치? ────────────→ CPU Boost / TDP Profile
```

### Non-Steam 게임

Non-Steam 게임도 Steam 라이브러리에 **Non-Steam Game**으로 추가하면 동일한 컨트롤러 표현 및 게임별 성능 기능을 사용할 수 있습니다.

Profile 탭은 Steam에 등록된 Non-Steam shortcut을 읽기 때문에 일반 Steam 게임과 마찬가지로 각 shortcut마다 별도의 CPU Boost 및 TDP 프로파일을 만들 수 있습니다.

Non-Steam 게임을 Steam을 통해 실행하고 Steam이 해당 shortcut을 현재 실행 중인 AppID로 보고하면 다음과 같이 동작합니다.

- 활성화된 CPU Boost / TDP 프로파일을 적용할 수 있습니다.
- 컨트롤러 표현이 가상 Steam Deck 컨트롤러로 전환됩니다.

### 런처를 사용하는 게임

일부 Non-Steam 게임은 먼저 별도의 런처를 실행한 뒤 실제 게임을 실행합니다.

이러한 게임은 실제 게임이 실행된 뒤에도 Steam이 해당 Non-Steam shortcut을 계속 실행 중인 것으로 인식하는 동안에만 지원됩니다.

런처가 종료될 때 Steam이 해당 shortcut도 종료된 것으로 판단하면 실제 게임 프로세스가 계속 실행 중이더라도 Steam은 더 이상 그 shortcut을 활성 `RunningAppID`로 제공하지 않습니다. 이 경우 Addon은 해당 게임의 Steam Deck 표현이나 게임 프로파일을 계속 유지할 수 없습니다.

이 동작은 의도적으로 Steam 자체의 실행 게임 상태를 기준으로 합니다. Addon은 별도로 하위 게임 프로세스를 찾아 추적하거나 executable polling을 fallback으로 사용하지 않습니다.

## Steam Quick Access Menu 지원

Steam Addon for Claw는 성능 제어 기능을 Steam의 GamepadUI / Quick Access Menu에 통합합니다.

Addon 탭에서는 데스크톱 UI와 동일한 성능 설정에 빠르게 접근할 수 있습니다.

- CPU Boost
- TDP PL1 / PL2
- 현재 실행 중인 게임의 Profile 제어

현재 활성 게임이 없으면 QAM에서는 Device 단위 제어가 표시됩니다. 지원되는 활성 게임이 감지되면 해당 게임의 Profile 제어가 대신 표시됩니다.

QAM 통합은 Steam의 기본 GamepadUI 컴포넌트를 사용합니다. Steam Client 업데이트로 내부 컴포넌트가 호환되지 않게 변경되면 Addon은 지원하지 않는 대체 UI를 강제로 주입하지 않고, 영향을 받은 QAM 통합 기능을 비활성화합니다.

## 자동 업데이트

Steam Addon for Claw는 애플리케이션이 시작될 때 자동으로 업데이트를 확인합니다.

새로운 Stable Release가 있으면 다음 순서로 처리됩니다.

1. 업데이트를 자동으로 다운로드합니다.
2. 사용자 입력 없이 설치가 Silent 방식으로 예약됩니다.
3. 현재 프로세스가 종료되고 업데이트가 적용됩니다.
4. 새 버전의 Steam Addon for Claw가 자동으로 다시 시작됩니다.

일반 Release에서는 별도의 수동 업데이트 작업이 필요하지 않습니다.

업데이트 서비스에 일시적으로 접근할 수 없거나, 업데이트 확인이 Timeout되거나, 업데이트 작업에 실패해도 Addon은 실행 자체를 차단하지 않고 정상적인 시작 절차를 계속합니다.

## 빠른 시작

1. 공식 Release 패키지를 설치합니다.
2. **Steam Addon for Claw**를 실행합니다.
3. Steam 관련 기능을 사용하려면 Steam을 실행 상태로 유지합니다.
4. 필요하면 **Device**에서 기본 CPU Boost / TDP 설정을 구성합니다.
5. 필요하면 **Profile**에서 게임별 성능 설정을 구성합니다.
6. 필요하면 **Controller**에서 Center M의 **Normal Action**을 설정합니다.
7. Steam 게임, Steam에 추가한 Non-Steam 게임을 실행하거나 Steam Big Picture Mode에 진입하면 내장 컨트롤러가 자동으로 가상 Steam Deck 컨트롤러로 Steam에 표현됩니다.

## 백그라운드 동작

컨트롤러 Runtime은 설정 창과 분리되어 실행되며 시스템 트레이에서 계속 사용할 수 있습니다.

설정 창을 닫아도 컨트롤러 표현이나 Profile 처리가 중지되지 않습니다. UI를 다시 열거나 Addon을 완전히 종료하고 싶을 때 시스템 트레이의 기능을 사용하면 됩니다.

## 안전 및 복구

Addon은 실제 핸드헬드 사용 Lifecycle을 기준으로 설계되어 있습니다.

다음과 같은 상황에서 컨트롤러 소유권과 복구 경로를 처리합니다.

- Steam 게임 및 Big Picture Mode 진입 및 종료
- 물리 컨트롤러 재열거(re-enumeration)
- Sleep / Hibernate / Resume
- 애플리케이션 종료 또는 재시작
- 표현 전환 실패 및 Rollback

Addon은 시작 시 이전의 오래된 세션을 의도적으로 다시 재생하지 않습니다. 컨트롤러 소유권은 현재의 실제 기기 상태를 기준으로 다시 구성됩니다.

## 알려진 제한 사항

- 현재 실제 하드웨어 검증은 MSI Claw 8 EX AI+ CG3EM (`MS-1T91`)에서 완료되어 있습니다. A2VM 모델은 지원되지만 아직 실제 기기 검증이 남아 있습니다.
- **순정 MSI Center M 컨트롤러 환경만 지원합니다. Handheld Companion, ClawTweaks 및 유사 컨트롤러 관리 환경과의 병행 사용은 지원하지 않습니다.**
- Motion / Gyro 출력은 현재 Steam Deck 가상 컨트롤러 매핑에서 지원되지 않습니다.
- 런처 기반 Non-Steam 게임은 실제 게임 시작 후에도 Steam이 해당 shortcut을 활성 `RunningAppID`로 계속 보고하는 경우에만 정상 동작합니다.
- QAM 통합은 Steam GamepadUI 내부 구조에 의존하므로 Steam Client의 큰 UI 변경 이후 Addon 업데이트가 필요할 수 있습니다.
- 다른 애플리케이션이 같은 물리 컨트롤러의 소유권을 독립적으로 가져가고 있으면 Addon의 컨트롤러 표현과 충돌할 수 있습니다.

## 문제 해결

Steam 게임 중 가상 Steam Deck 컨트롤러가 표현되지 않는 경우 다음 항목을 확인합니다.

1. 기기가 위 지원 목록의 Board ID 중 하나인지 확인합니다.
2. **순정 MSI Center M 환경인지 확인하고 Handheld Companion, ClawTweaks 또는 다른 컨트롤러 관리 프로그램이 실행 중이지 않은지 확인합니다.**
3. Addon이 컨트롤러 권한을 보유하도록 **Controller** 탭에서 MSI Center M이 비활성화되어 있는지 확인합니다.
4. Steam이 실행 중이며 해당 Steam 게임 또는 Non-Steam shortcut을 현재 실행 중인 것으로 인식하고 있는지 확인합니다.
5. 내장 컨트롤러를 관리하고 있을 수 있는 다른 컨트롤러 관리 또는 가상 컨트롤러 도구를 종료합니다.
6. MSI Center M을 직접 열었거나 컨트롤러가 재열거된 경우 Addon이 안정적인 상태로 돌아갈 수 있도록 한 뒤 Steam 세션을 다시 시작합니다.

런처가 종료된 뒤 Non-Steam 게임 프로파일 적용이 중단된다면 Steam이 여전히 해당 Non-Steam shortcut을 실행 중으로 표시하는지 확인합니다. Addon은 하위 게임 실행 파일을 별도로 스캔하지 않고 Steam이 제공하는 활성 AppID를 의도적으로 기준으로 사용합니다.

Steam Client 업데이트 후 Steam Quick Access의 Addon 탭이 보이지 않는다면 Steam GamepadUI 통합에 호환성 업데이트가 필요할 수 있습니다. 핵심 설정 기능은 계속 데스크톱 UI에서 사용할 수 있습니다.

## 개발 문서

기술 구현 메모, 프로토콜 조사, Lifecycle 설계, 공개 전 개발용 README는 GitHub 저장소의 [`docs/`](https://github.com/onehoon/SteamAddonforClaw/tree/main/docs)에서 확인할 수 있습니다.

이전에 사용하던 개발 중심 README는 [`docs/PRE_RELEASE_DEVELOPMENT_STATUS.md`](https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/PRE_RELEASE_DEVELOPMENT_STATUS.md)에 보관되어 있습니다.

현재 영문 사용자 문서는 저장소 루트의 [`README.md`](https://github.com/onehoon/SteamAddonforClaw/blob/main/README.md)에서 확인할 수 있습니다.

## 라이선스

이 프로젝트는 `AGPL-3.0-only` 라이선스를 사용합니다.
