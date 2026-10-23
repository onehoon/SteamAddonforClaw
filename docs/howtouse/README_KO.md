# Steam Addon for Claw

Steam Addon for Claw는 지원되는 MSI Claw 핸드헬드의 내장 컨트롤러에 Steam 연동과 게임별 성능 설정을 제공합니다. Steam 게임 또는 Steam Big Picture가 활성화되면 내장 컨트롤러를 Steam Deck 또는 DualSense Edge로, 그 외에는 Xbox 360 컨트롤러로 표현할 수 있습니다. 전역 Steam 컨트롤러와 선택적인 Steam 게임별 컨트롤러도 지정할 수 있으며, 전면·후면 버튼, 성능 HUD, 바로가기, 별도의 Quick Settings Overlay, Windows 게임 홈 동작도 설정할 수 있습니다.

이 안내서는 현재 앱에서 실제로 제공하는 기능을 설명합니다. 메인 앱에는 **Device**, **Controller**, **Steam**, **XBOX**, **ClawHUD**, **Shortcut**, **Tweaks**, **How to Use**, **Settings**가 있습니다. **Quick Settings Overlay**는 컨트롤러로 조작하는 별도 창이며, Steam Quick Access나 ClawHUD와는 다릅니다.

## 목차

- [지원 기기](#지원-기기)
- [요구 사항 및 호환성](#요구-사항-및-호환성)
- [빠른 시작](#빠른-시작)
- [컨트롤러 표현 방식](#컨트롤러-표현-방식)
- [자이로스코프 / IMU — Steam Input 설정](#자이로스코프--imu--steam-input-설정)
- [MSI Center M과 내장 컨트롤러](#msi-center-m과-내장-컨트롤러)
- [메인 앱 안내](#메인-앱-안내)
  - [Device](#device)
  - [Controller](#controller)
  - [Steam](#steam)
  - [XBOX](#xbox)
  - [ClawHUD](#clawhud)
  - [Shortcut](#shortcut)
  - [Tweaks](#tweaks)
  - [Settings](#settings)
- [게임 프로필과 설정 우선순위](#게임-프로필과-설정-우선순위)
- [Non-Steam 게임](#non-steam-게임)
- [Quick Settings Overlay](#quick-settings-overlay)
- [Steam Quick Access와 Quick Settings Overlay 비교](#steam-quick-access와-quick-settings-overlay-비교)
- [컨트롤러 매핑 안내](#컨트롤러-매핑-안내)
- [백그라운드 실행 및 업데이트](#백그라운드-실행-및-업데이트)
- [절전·복귀·재시작](#절전복귀재시작)
- [복구 및 문제 해결](#복구-및-문제-해결)
- [알려진 제한 사항](#알려진-제한-사항)
- [기본 MSI 컨트롤러로 복귀 및 제거](#기본-msi-컨트롤러로-복귀-및-제거)
- [라이선스](#라이선스)

## 지원 기기

Device 페이지에는 제조사와 모델, 호환 상태, 보드 식별 정보, 감지된 GPU 이름이 표시됩니다. 현재 앱은 다음 MSI Claw 모델을 인식합니다.

| 지원 기기 |
| --- |
| MSI Claw 7 AI+ A2VM |
| MSI Claw 8 AI+ A2VM |
| MSI Claw 8 EX AI+ CG3EM |

컨트롤러 관리를 시작하기 전에 지원 모델인지 확인합니다. 모델을 식별할 수 없거나 지원되지 않으면 컨트롤러 기능이 시작되지 않습니다.

**MSI Claw 7 AI+ A2VM**, **MSI Claw 8 AI+ A2VM**, **MSI Claw 8 EX AI+ CG3EM** 세 모델 모두 실제 기기에서 검증했습니다. A2VM 7·8의 지원 기능도 실기 검증을 완료했습니다. LED·진동·모션 등 기기별 설정은 감지된 모델과 펌웨어에 따라 사용 가능 여부가 달라질 수 있으며, 게임별 호환성은 별개입니다.

## 요구 사항 및 호환성

- Windows 11 x64, 빌드 26100 이상
- 위에 기재된 지원 MSI Claw 모델
- 현재 로그인한 Windows 사용자가 관리자 계정이어야 합니다. 승인이 필요한 작업에서는 Windows 사용자 계정 컨트롤(UAC) 창이 표시될 수 있습니다. 직접 시작한 작업에 대해서만 승인하세요.
- 기본 MSI 컨트롤러 환경을 위한 순정 MSI Center M
- Steam Deck 컨트롤러 표현, Steam 게임 프로필, Steam Big Picture 및 Steam Quick Access 기능을 사용할 경우 Steam 설치 및 실행

다른 컨트롤러 관리 프로그램이나 가상 컨트롤러 라우팅 프로그램을 동시에 실행하지 마세요. 내장 컨트롤러를 두고 Addon과 MSI Center M 또는 다른 프로그램이 서로 경쟁할 수 있습니다. 이 앱은 한 명의 Windows 사용자와 하나의 대화형 세션을 기준으로 합니다. 원격 데스크톱 및 다중 사용자 세션은 지원하지 않습니다.

## 빠른 시작

1. [Releases](https://github.com/onehoon/SteamAddonforClaw/releases/latest)에서 최신 Windows 설치 파일을 내려받아 설치합니다.
2. 설치 또는 최초 구성 중 Windows 승인 창이 표시되면 직접 시작한 작업에 대해 승인합니다. 구성요소 설정 안내가 표시되면 앱의 지시를 따르세요. Windows 재시작을 요청하면 작업을 저장한 뒤 재시작하세요.
3. **Steam Addon for Claw**를 열고 **Device**에서 기기 상태를 확인합니다.
4. Addon이 내장 컨트롤러를 관리하도록 하려면 **MSI Center M**의 **Disable**을 선택한 후 **Disable and Restart**를 확인합니다. 컨트롤러 관리 권한을 바꾸려면 Windows 재시작이 필요합니다.
5. Windows가 다시 시작되면 Addon은 백그라운드에서 실행됩니다. 메인 앱을 열어 버튼, 성능 설정, 프로필 또는 ClawHUD를 설정하세요.

진동이 나오지 않는다면 게임을 저장하고 종료한 뒤 **Controller → Vibration Strength → Vibration Recovery → Restore**를 사용하세요. 자동 진동 복구는 기본적으로 실행되지 않습니다. 복구 후 좌우 모터의 **Test** 버튼으로 실제 진동을 확인할 수 있습니다.

MSI 기본 컨트롤러 동작을 계속 사용하려면 MSI Center M을 활성화 상태로 두세요. 나중에 Device에서 **Enable and Restart**를 선택해도 됩니다.

## 컨트롤러 표현 방식

MSI Center M의 컨트롤러 관리가 비활성화되어 Addon이 컨트롤러를 관리하면 실행 상태에 따라 다음 **가상 컨트롤러** 중 하나를 Windows에 표시합니다.

| 지원 가상 컨트롤러 | 사용되는 상태 |
| --- | --- |
| **Xbox 360** | Steam 게임과 Big Picture가 활성 상태가 아닌 일반 Windows 환경 |
| **Steam Deck** | 전역 선택 또는 활성화된 게임 프로필에서 Steam 게임/Big Picture용으로 선택한 경우 |
| **DualSense Edge** | **Controller → Controller for Steam Game** 또는 활성화된 Steam 게임 프로필에서 선택한 경우 |

전역 **Steam Deck** 또는 **DualSense Edge**는 **Controller → Controller for Steam Game**에서 선택합니다. 기본 선택은 Steam Deck입니다. Steam 게임 프로필에서 별도 컨트롤러를 지정할 수도 있고, **Use default**는 전역 선택을 따릅니다. Steam 게임 또는 Big Picture가 활성화되면 컨트롤러 선택만 잠기며, 게임별 재정의가 있으면 실제 적용된 컨트롤러를 표시합니다. Steam과 Big Picture가 모두 비활성인 일반 Windows나 Xbox 게임 중에는 컨트롤러 선택을 변경할 수 있습니다. **Gyroscope** 토글은 모든 상태에서 변경할 수 있습니다. Steam 게임 시작/종료 시 활성 게임의 선택과 전역 선택 사이를 자동 전환하고, Steam 게임과 Big Picture가 모두 비활성인 일반 Windows에서는 Xbox 360을 사용합니다. 한 번에 하나의 가상 컨트롤러만 연결됩니다. **Normal** 또는 **Steam Game / Big Picture**에서 설정한 버튼 동작은 실제 활성화된 컨트롤러 표현에 맞춰 사용됩니다.

**Steam Input 필수 사용 설정:** Addon의 **Steam Deck** 또는 **DualSense Edge** 가상 컨트롤러로 Steam 게임을 플레이할 때는 게임별 **Steam Input을 켜세요**. Steam 라이브러리에서 게임을 우클릭하고 **속성 → 컨트롤러 → 해당 게임에 대한 재정의 → Steam Input 사용(Enable Steam Input)**을 선택합니다. 게임이 DualSense를 자체 지원하더라도 Addon의 공식 지원 설정은 **Steam Input ON**입니다. Steam Input OFF에서 일부 게임이 작동할 수는 있지만, 동일한 입력·진동 동작을 보장하는 지원 설정은 아닙니다.

**DualSense Edge 진동 지원 범위:** Addon은 일반 좌우 모터 **Rumble**만 MSI Claw의 물리 진동 모터로 전달합니다. Sony의 **L2/R2 어댑티브 트리거 저항·진동**과 **USB Audio 기반 고급 햅틱**은 물리 Claw 모터로 변환하지 않습니다. 게임에서 R2를 누를 때 느껴지는 진동은 별도로 전송된 일반 Rumble일 수 있으며, 고급 햅틱까지 재현한다는 의미는 아닙니다.

## 자이로스코프 / IMU — Steam Input 설정

**Controller → Controller for Steam Game**의 전역 **Gyroscope** 토글은 기본적으로 켜져 있습니다. 켜져 있고 Steam용 가상 컨트롤러가 적용되면 지원되는 물리 자이로스코프·가속도 값을 Steam Input에 전달합니다. 끄면 센서 수집과 모션 입력을 중단하지만 일반 컨트롤러 입력과 가상 장치 연결은 유지됩니다. Addon 자체에서는 자이로를 마우스나 조이스틱으로 변환하지 않습니다. **Xbox 360 모드에서는 자이로 입력을 제공하지 않습니다.**

**자이로를 주로 사용한다면 DualSense Edge 모드를 고려해 보세요.** 테스트한 MSI Claw 8 AI+ A2VM에서는 DualSense Edge 자이로 움직임이 더 부드러웠고, Steam Deck 모드의 **Gyro To Mouse**에는 끊김이 보고됐습니다. 기기와 게임에 따라 결과가 다를 수 있습니다. Gyroscope 토글은 모션 입력을 켜고 끄는 기능이며 활성화 조건과 감도는 Steam Input에서 설정합니다.

같은 기기에서 Steam 자이로 보정 화면에 움직임이 표시되고, Steam Input 활성화 조건을 **Always On**으로 설정했을 때 게임의 자이로 → 조이스틱 카메라 조작이 작동한 사례가 있습니다.

**자이로 동작과 활성화 조건, 감도는 Steam Input에서 설정하세요.** Steam에서 게임별 **컨트롤러 레이아웃 → 자이로 동작(Gyro Behavior)**을 열고 **Gyro To Mouse** 또는 자이로 → 조이스틱 동작을 선택합니다. 보정 화면에서 센서가 움직여도 활성화 버튼·조건에 따라 게임에서 동작하지 않을 수 있으므로, 처음에는 **Always On**으로 확인한 뒤 필요한 조건과 감도를 조정하세요.

**A2VM 7인치와 A2VM 8인치**의 자이로 입력은 모두 실기 검증을 완료했습니다. EX/CG3EM의 자이로 동작은 아직 충분히 실기 검증되지 않았으며, DualSense Edge의 게임별 버튼·PS/Quick Access·진동·모션 호환성은 게임에 따라 차이가 있을 수 있습니다.

## MSI Center M과 내장 컨트롤러

MSI Center M 설정은 **Device** 페이지에 있습니다. MSI Center M을 비활성화하면 Windows 재시작 후 Addon이 내장 컨트롤러를 관리합니다. 다시 활성화하면 Windows 재시작 후 MSI가 컨트롤러를 관리합니다.

현재 세션에서 즉시 바꾸는 스위치가 아닙니다. 확인 창에는 **Disable and Restart** 또는 **Enable and Restart**가 표시됩니다. 전환을 확인한 뒤에는 앱을 강제 종료하거나 기기를 끄지 말고 Windows 재시작이 완료되도록 하세요.

## 메인 앱 안내

### Device

Device 페이지에서 감지된 기기와 지원 상태를 확인할 수 있습니다. 컨트롤러 관리 방식을 정하는 **MSI Center M**도 여기서 변경합니다. 자세한 내용은 [MSI Center M과 내장 컨트롤러](#msi-center-m과-내장-컨트롤러)를 참고하세요.

기기 전체에 적용되는 성능 및 배터리 설정은 다음과 같습니다.

- **TDP Control** — 프로세서 전력 한도 관리를 켭니다. **PL1**은 지속 전력 한도이고 **PL2**는 짧은 시간 적용되는 더 높은 전력 한도입니다. **Plugged in**과 **On battery** 값은 따로 설정합니다. 설정 가능한 범위는 Claw 모델에 따라 다릅니다. 지원되는 경우 첫 설정에서 기존 MSI Center M Manual TDP 값을 불러올 수 있습니다.
- **CPU Boost** — Windows 프로세서 부스트 모드를 전원 상태별로 관리합니다. **Disabled**, **Enabled**, **Aggressive**, **Efficient Enabled**, **Efficient Aggressive**, **Aggressive At Guaranteed**, **Efficient Aggressive At Guaranteed** 중 선택합니다.
- **Maximum Processor State** — Windows의 최대 프로세서 성능 상태를 **Plugged in**과 **On battery** 각각 **99%** 또는 **100%**로 설정합니다. CPU 사용률 상한을 정하는 기능은 아니며 CPU Boost와 별도로 동작합니다. 처음 설치하면 꺼져 있습니다. 끄면 관리가 중단되지만 이전 Windows 값으로 자동 복원되지는 않습니다.
- **Windows Power Mode** — 전원 상태별 Windows 전원 모드를 관리합니다. **Best power efficiency**, **Balanced**, **Best performance** 중 선택합니다.
- **Battery charge limit** — 기기가 지원하는 경우 충전 한도를 켜고 60%부터 100%까지 5% 단위로 설정합니다.

**TDP Control**, **CPU Boost**, **Maximum Processor State**, **Windows Power Mode**를 끄면 Addon이 해당 설정의 관리를 중단합니다. Addon이 건드리기 전의 Windows/MSI 값을 언제나 그대로 복원한다는 의미는 아닙니다. 저장한 값은 유지되므로 나중에 다시 켤 수 있습니다.

**Battery charge limit**은 동작이 다릅니다. 이 토글은 MSI의 충전 제한 기능 자체를 직접 켜거나 끕니다. 선택한 충전 한도 퍼센트는 나중에 다시 사용할 수 있도록 저장됩니다.

### Controller

페이지 맨 위의 **Controller for Steam Game** 영역에서 기본값인 **Steam Deck** 또는 **DualSense Edge**를 선택하고 **Gyroscope**를 켜거나 끕니다. Steam 게임/Big Picture 중에는 컨트롤러 선택만 잠기고 실제 적용된 컨트롤러가 표시됩니다. 자이로 토글은 Steam 게임, Big Picture, Xbox 게임, 일반 Windows 상태에서 변경할 수 있습니다. 앱에서 설정 상태를 확인할 수 없으면 컨트롤러 선택이 잠길 수 있습니다.

**Controller**에서 **XBOX Gamebar Button**(WING)과 **Center M Button**의 동작을 설정합니다. 각 버튼은 **Normal**과 **Steam Game / Big Picture**에서 서로 다른 동작을 가질 수 있습니다. 현재 컨트롤러 표현에 따라 사용할 설정이 자동으로 선택됩니다.

설정 그룹에 따라 사용할 수 있는 동작이 다릅니다.

| 동작 | 기능 |
| --- | --- |
| **Quick Settings Overlay** | Addon의 별도 Quick Settings Overlay를 엽니다. |
| **Steam Quick Access** | Steam Deck의 Quick Access 버튼 입력을 Steam에 전달합니다. Steam 자체 Quick Access Menu를 열며, Steam 안에 Addon 페이지를 여는 기능은 아닙니다. Steam Game / Big Picture 그룹에서 사용할 수 있습니다. |
| **Steam Button** | Steam 시스템 버튼 입력을 전달합니다. Steam Quick Access와 다른 동작입니다. Steam Game / Big Picture 그룹에서 사용할 수 있습니다. |
| **Steam Big Picture** | Steam Big Picture를 엽니다. Normal 그룹에서 사용할 수 있습니다. |
| **Xbox** | Xbox 앱을 엽니다. Normal 그룹에서 사용할 수 있습니다. |
| **Keyboard/Hotkey** | Ctrl, Shift, Alt, Windows 보조키와 설정한 키 하나를 입력합니다. |
| **Launch Application** | 실행 파일을 선택해 실행합니다. 필요하면 인수를 지정할 수 있습니다. |

최초 설치 기본값은 **Gamebar Button: Normal에서는 Steam Big Picture, Steam Game / Big Picture에서는 Steam Button**입니다. **Center M Button은 두 그룹 모두 Quick Settings Overlay**입니다. 각 모드에서 **Gamebar Button**과 **Center M Button**에는 서로 다른 동작을 지정해야 합니다.

**M1 / M2**에서는 Xbox 360 컨트롤러로 표시되는 동안의 후면 버튼 동작을 설정합니다. 각각 **Disabled**, A/B/X/Y, D-Pad 방향, LB/RB, LT/RT, L3/R3, View, Menu, Xbox Guide 중 선택할 수 있습니다. 두 버튼을 같은 대상으로 지정해도 됩니다. Steam Game / Big Picture에서는 Steam Deck과 DualSense Edge 모두 M1은 R4, M2는 L4로 유지되며 전역 Xbox 360 설정은 이 동작을 바꾸지 않습니다. XBOX 게임에서 게임별 M1/M2 설정을 켜면 전역 매핑 대신 사용할 수 있습니다.

기기에서 지원하는 경우 **Joystick LED**를 켜거나 끄고 하나의 고정 색상과 0%~100% 밝기를 설정할 수 있습니다. **Vibration Strength**에서는 좌우 모터를 각각 0%~100%로 조절하고 각 **Test** 버튼으로 실제 진동을 확인합니다. 모델·펌웨어에 따라 일부 설정은 사용할 수 없으며, A2VM 7·8의 지원 기능은 실제 기기에서 검증했습니다.

**Vibration Strength** 안의 **Vibration Recovery → Restore**는 세 지원 모델에서 사용할 수 있는 수동 진동 복구 기능입니다. 실행 중 가상 컨트롤러가 잠시 끊기므로 게임을 저장하고 종료한 뒤 사용하세요. 성공 메시지 자체가 실제 모터 동작을 보증하지는 않으므로 좌우 **Test** 버튼으로 확인하세요. 자동 진동 복구는 기본적으로 꺼져 있습니다.

### Steam

**Steam** 페이지에는 설치된 Steam 게임과 Steam이 인식하는 Non-Steam 바로가기가 표시됩니다. **Refresh**, 검색, 즐겨찾기를 사용해 게임을 찾고 선택하면 프로필을 편집할 수 있습니다. 게임을 실행하기 전에 미리 프로필을 설정하고, 실제 사용할 프로필의 활성화 스위치를 켜세요.

게임 상세 설정 맨 위의 **Virtual Controller**에서 **Use default**, **Steam Deck**, **DualSense Edge**를 선택합니다. 이 선택은 프로필이 켜져 있고 Steam / Big Picture가 비활성 상태일 때만 편집할 수 있습니다. 프로필을 꺼도 선택한 값은 보존됩니다. **Use default** 또는 프로필이 없거나 꺼진 경우에는 **Controller → Controller for Steam Game**의 전역 선택을 따릅니다. Big Picture에서 게임이 활성화되면 켜진 프로필의 명시적 선택이 자동 적용되고, 게임이 끝나 Big Picture가 계속 열려 있으면 전역 선택으로 돌아갑니다.

게임별 설정은 다음과 같습니다.

- **TDP Control** — **Plugged in** 및 **On battery** 각각의 PL1/PL2 값
- **CPU Boost** — 전원 상태별 Windows 프로세서 부스트 모드
- **Windows Power Mode** — 전원 상태별 Windows 전원 모드
- **Minimum GPU Clock** — 지원되는 경우 전원 상태별 Intel GPU 최소 클럭 값. 전력 또는 온도 한도 때문에 실제 클럭이 설정값보다 낮아질 수 있습니다.
- **Intel FPS Limit** — 전원 상태별 40~120 FPS 목표값을 1 FPS 단위로 설정합니다. Intel의 FPS 제한 기능을 사용하며 일부 게임은 지원하지 않을 수 있습니다.
- **Resolution** — **Do not change**, 1920 × 1200, 1920 × 1080, 1680 × 1050, 1440 × 900

**TDP Control**, **CPU Boost**, **Windows Power Mode**는 프로필에서 해당 기능을 켜면 대응하는 Device 설정보다 우선합니다. 프로필에서 이 기능을 끄면 활성화된 Device 설정을 계속 사용할 수 있습니다. **Minimum GPU Clock**, **Intel FPS Limit**, **Resolution**은 게임 프로필 전용 기능이므로 대응하는 Device 설정이 없습니다. 게임이 비활성화되면 게임 프로필 전용 설정의 적용이 끝나고, TDP/CPU Boost/Windows Power Mode는 활성화된 Device 설정이 다시 적용됩니다. 별도 런처가 종료될 때 Steam이 바로가기를 더 이상 활성 게임으로 인식하지 않으면, 자식 게임 프로세스가 실행 중이어도 프로필 적용이 끝날 수 있습니다. 자세한 내용은 [Non-Steam 게임](#non-steam-게임)을 참고하세요.

### XBOX

**XBOX** 페이지에는 현재 Windows 사용자에게 설치되어 있고 앱이 인식한 Xbox/Microsoft Store 게임이 표시됩니다. **Refresh**, 검색, 즐겨찾기를 사용하고 게임을 선택해 프로필을 편집하세요. 해당 게임이 활성화됐을 때 설정을 적용하려면 프로필을 켭니다. 모든 Store 앱이나 Game Pass 타이틀이 목록에 표시되는 것은 아닙니다.

성능 설정은 Steam 프로필과 같은 TDP Control, CPU Boost, Windows Power Mode, Minimum GPU Clock, Intel FPS Limit, Resolution입니다. XBOX 프로필에는 **Use global M1 / M2 mapping** 설정도 있습니다. 이를 끄면 해당 게임에서 사용할 Xbox 360 M1/M2 동작을 따로 지정할 수 있습니다. Steam Deck 표현에서는 M1은 R4, M2는 L4로 유지됩니다.

게임을 실행하기 전에 미리 프로필을 설정할 수 있습니다. Addon이 해당 XBOX 게임을 활성 상태로 인식하면 활성화한 프로필 설정을 사용합니다. TDP Control, CPU Boost, Windows Power Mode는 대응하는 프로필 기능이 꺼져 있으면 활성화된 Device 설정을 계속 사용할 수 있습니다. Minimum GPU Clock, Intel FPS Limit, Resolution은 게임 프로필 전용이므로 Device 대체 설정이 없습니다.

### ClawHUD

**ClawHUD**는 Addon에 포함되어 함께 관리되는 성능 HUD입니다. 별도 ClawHUD 앱을 설치할 필요가 없습니다. **Enable HUD**를 켠 다음 표시 모드를 **In game only** 또는 **Always**로 선택합니다.

기기와 데이터 제공 여부에 따라 **FPS**, **CPU 사용률 및 온도**, **GPU 사용률 및 클럭**, **CPU 패키지 전력(TDP)**, **RAM 사용량**, **VRAM 사용량**, **팬 속도**, **배터리 정보**를 표시할 수 있습니다. 배터리 사용 중에는 잔량과 예상 사용 가능 시간이 표시될 수 있습니다. 제공되지 않는 측정값은 실제 0으로 처리하지 않고 표시에서 제외됩니다.

Intel XeFG를 사용하는 경우 PresentMon이 드라이버에서 생성된 모든 출력 프레임을 관찰하지 못할 수 있으므로 ClawHUD의 FPS 값에 모든 생성 프레임이 포함되지 않을 수 있습니다.

**Size**(**-2**, **-1**, **Default**, **+1**, **+2**), **Font**(**Unispace**, **Segoe UI Variable**), **Alignment**(**Left**, **Center**, **Right**), **Background width**(**Full width**, **Content width**)를 설정할 수 있습니다. **Opacity**는 50%~100% 범위에서 5% 단위로 조절합니다. **Intel VRR Range Fix**는 해당 Intel 디스플레이의 가변 주사율 범위를 보정하기 위한 기기별 옵션이며 모든 디스플레이에 필요한 것은 아닙니다. 문제가 표시되면 사용할 수 있는 경우 페이지의 **Retry**를 선택하세요.

### Shortcut

**Shortcut**에서 Quick Settings Overlay에 표시할 항목을 추가·편집·삭제하고 드래그하여 순서를 바꿀 수 있습니다. 지원하는 동작은 다음과 같습니다.

- 실행 파일 실행(선택적 인수 포함)
- PowerShell 스크립트 실행
- 웹사이트 URL 열기
- 기본 디스플레이를 JPEG 스크린샷으로 캡처
- Steam Big Picture, Steam 클라이언트 또는 Xbox 앱 열기

**실행 파일(.exe)**과 **PowerShell** 바로가기에는 **Run as administrator** 옵션이 있으며 기본값은 꺼짐입니다. 관리자 권한이 꼭 필요한 동작에만 켜세요. 다른 바로가기 유형에는 이 옵션이 없습니다.

스크린샷은 기본 디스플레이를 JPEG 파일로 저장합니다. **Browse**로 사용자 지정 폴더를 선택하거나 **Use default**로 Windows `Pictures\Screenshots` 폴더를 사용할 수 있습니다. **Open folder**는 선택된 폴더를 열며, **Save**를 눌러야 변경 사항이 저장됩니다. 스크린샷 바로가기는 하나만 추가할 수 있습니다.

바로가기의 아이콘은 동작에 맞춰 자동 설정되지만, 추가·편집 창에서 다른 아이콘을 선택하거나 기본 아이콘으로 되돌릴 수 있습니다. 스크린샷을 제외한 바로가기에서는 **Close Overlay after launch**로 동작 실행 후 Overlay를 닫을지도 설정할 수 있습니다. Overlay에는 설정한 바로가기와 아이콘이 표시됩니다. 세부 설정은 메인 앱에서 편집하세요.

### Tweaks

**Tweaks**에서는 지원되는 Windows 기능 및 레지스트리 설정을 변경할 수 있습니다. 처음 열거나 필요한 경우 안전 안내문을 확인해야 하며, 이 안내문은 하루에 한 번 표시될 수 있습니다. 일부 변경은 Windows 또는 파일 탐색기 재시작 후 적용됩니다. Addon이 자동으로 재시작하지는 않습니다. Windows 정책이나 기기 상태에 따라 사용할 수 없는 옵션도 있으며, 적용 확인이 필요한 상태는 화면에 따로 표시됩니다.

**Gaming Performance** 항목:
- **Memory Integrity** — Windows 커널 메모리 보호 기능을 켜거나 끕니다. **끄면 Windows 보안이 약화됩니다.** 켤 때는 호환되지 않는 드라이버나 프로그램이 영향을 받을 수 있습니다.
- **Virtual Machine Platform** — Windows 가상화 플랫폼 기능을 켜거나 끕니다. 끄면 가상화 기능에 의존하는 프로그램이 동작하지 않을 수 있습니다.

**Registry Tweaks** 항목:
- **Faster Menus** — 메뉴 애니메이션을 끄고 하위 메뉴가 열리기까지의 지연을 줄입니다.
- **Classic Context Menu** — Windows 11의 오른쪽 클릭 메뉴를 클래식 방식으로 표시합니다.
- **Disable Password on Wake** — 절전에서 복귀할 때 로그인을 요구하지 않도록 합니다. **다른 사람이 기기에 직접 접근할 경우 바탕화면을 열 수 있으므로 주의하세요.**
- **Disable Edge Background** — Edge의 시작 부스트 및 백그라운드 실행을 비활성화합니다. 이미 실행 중인 Edge는 직접 종료해야 할 수 있습니다.
- **Disable Startup App Delay** — 시작 앱의 실행 지연 설정을 제거합니다. 실제 시작 속도 개선은 환경에 따라 달라집니다.

**Disable ...**로 시작하는 항목은 토글을 **켜면 이름에 표시된 비활성화 동작이 적용**됩니다. 토글을 끄면 Windows가 허용하는 범위에서 그 옵션을 해제합니다.

### Settings

#### 앱 업데이트 (Application updates)

**Check**를 누르면 업데이트를 다시 확인하고 있으면 다운로드합니다. 다운로드가 끝나 설치할 수 있으면 버튼이 **Install update**로 바뀝니다. 선택하면 Addon이 재시작되고 다음 안전한 시작 과정에서 업데이트가 적용됩니다. 업데이트가 없으면 최신 상태라고 표시됩니다.

#### XBOX Mode (Full Screen Experience)

Windows 게임 전체 화면 환경에서 사용할 홈 앱을 **Off**, **XBOX**, **Steam Big Picture** 중 선택합니다. **Off**는 Addon이 선택한 게임 홈을 사용하지 않도록 합니다. **Start on Windows startup**은 다음 Windows 시작 시 선택한 홈 앱으로 바로 진입할지 정합니다. Windows가 이미 다른 앱을 사용 중이면 **Other Windows app**에 현재 선택이 표시됩니다. 새 옵션으로 선택하거나 해당 앱의 시작 동작을 Addon에서 관리할 수는 없습니다.

#### 앱 테마 (App theme)

메인 앱의 표시 방식을 **System**, **Light**, **Dark** 중 선택합니다. **System**은 Windows의 테마 설정을 따릅니다.

#### 현재 전원 소스만 표시 (Show only current power source)

켜면 Quick Settings Overlay에서 현재 연결된 전원에 해당하는 컨트롤만 표시합니다. 메인 앱에서는 **Plugged in**과 **On battery** 설정을 계속 모두 볼 수 있습니다.

#### 로깅 (Logging)

기록할 로그의 상세 수준을 **Off**, **Info**, **Debug** 중 선택합니다. **Open folder**는 로그가 저장되는 폴더를 엽니다. 문제가 발생했을 때 기록을 확인하거나 오류를 제보할 때 사용할 수 있습니다.

#### 필수 구성 요소 (Required Components)

컨트롤러 기능에 필요한 구성 요소의 준비 상태를 확인합니다. 직접 드라이버를 설치하는 화면은 아닙니다. 앱에 설정 또는 재시작 안내가 표시되면 안내를 따르세요.

## 게임 프로필과 설정 우선순위

Steam 및 XBOX 게임 프로필은 게임을 실행하기 전에 미리 만들고 편집할 수 있습니다. 프로필을 꺼도 저장된 값은 유지됩니다.

Steam 게임의 **Virtual Controller**는 마스터 프로필이 켜져 있을 때만 편집할 수 있습니다. **Use default** 또는 프로필이 없거나 꺼진 경우 **Controller → Controller for Steam Game**의 전역 선택을 사용합니다. Big Picture에서 게임이 시작되면 켜진 프로필의 명시적 선택으로 자동 전환하고, 게임이 끝나 Big Picture가 계속 열려 있으면 전역 선택으로 돌아옵니다. Steam 게임 또는 Big Picture가 활성화된 동안 전역·게임별 컨트롤러 수동 선택은 잠깁니다. 프로필을 껐다 다시 켜도 게임별 선택은 보존됩니다.

**TDP Control**, **CPU Boost**, **Windows Power Mode**는 Device와 게임 프로필 양쪽에 있는 기능입니다. 실행 중인 게임 프로필에서 해당 기능을 켜면 프로필 값이 우선합니다. 프로필에서 해당 기능을 끄면 활성화된 Device 설정을 계속 사용할 수 있습니다.

**Minimum GPU Clock**, **Intel FPS Limit**, **Resolution**은 게임 프로필 전용 기능입니다. 이 기능을 끄거나 일치하는 활성 게임 프로필이 없을 때 대신 사용할 Device 설정은 없습니다. **Battery charge limit**과 **Maximum Processor State**는 Device 전용이며 게임 프로필에는 포함되지 않습니다.

```text
인식된 게임 실행 중 + 활성화된 프로필
    -> 켜진 TDP / CPU Boost / Power Mode 프로필 설정은 Device보다 우선
    -> 켜진 GPU Clock / FPS Limit / Resolution은 해당 게임에 적용
    -> 꺼진 TDP / CPU Boost / Power Mode 프로필 설정은 활성화된 Device 설정 사용 가능

일치하는 활성 프로필 없음
    -> Device 단위 기능은 활성화된 Device 설정 사용
    -> 게임 프로필 전용 설정은 적용되지 않음
```

게임이 비활성화되면 게임 프로필 전용 설정의 적용이 끝나고, Device에 대응 설정이 있는 기능은 활성화된 Device 설정이 다시 적용됩니다. Device의 TDP Control, CPU Boost, Windows Power Mode를 끄면 Addon이 해당 Device 설정의 관리를 중단하며 이전 Windows/MSI 값 복원을 보장하지는 않습니다. Battery charge limit은 Device 항목에서 설명한 별도의 직접 켜기/끄기 동작을 사용합니다.

## Non-Steam 게임

일반 Windows 게임에 Steam 프로필을 사용하려면 먼저 Steam에 해당 게임을 **Non-Steam Game**으로 추가하세요. **Steam** 페이지에 Steam이 인식하는 바로가기가 표시되므로 프로필을 미리 설정하고 활성화할 수 있습니다. Steam 바로가기로 게임을 실행하고 Steam이 계속 활성 게임으로 표시하는지 확인하세요. 일부 런처는 별도 게임 프로세스를 실행한 뒤 자신의 바로가기를 종료합니다. 이때 Steam이 바로가기를 더 이상 활성 상태로 보지 않으면 Addon의 프로필 적용도 끝날 수 있습니다.

## Quick Settings Overlay

Quick Settings Overlay는 컨트롤러로 조작하는 Addon의 별도 설정 창입니다. 메인 앱과 같은 저장 설정을 사용합니다. 기본 탭 순서는 **Shortcut**, **Device**, **Controller**, **Setting**, **Profile**이며 **Setting → Tab Order**에서 순서를 바꿀 수 있습니다.

- **Device** — 지원되는 기기 전체 TDP Control, CPU Boost, Windows Power Mode, 배터리 충전 한도에 빠르게 접근합니다.
- **Profile** — 현재 활성화되어 앱이 인식한 게임의 설정을 표시합니다. 게임이 없으면 “No game is currently running. Start a game to configure its profile.”라고 안내합니다. 게임과 기기에 따라 표시되는 항목이 다르며, XBOX 프로필에는 M1/M2 설정도 표시될 수 있습니다.
- **Controller** — 메인 앱과 공유되는 **Controller for Steam Game** 선택 및 **Gyroscope** 토글, Xbox 360 M1/M2 매핑, 지원되는 경우 Joystick LED 및 진동 설정을 제공합니다.
- **Shortcut** — 설정한 바로가기를 실행합니다. 세부 설정은 메인 앱의 Shortcut에서 편집합니다.
- **Setting** — Overlay 탭 순서와 현재 전원 소스만 표시하는 옵션을 설정합니다.

컨트롤러 조작은 **LB / RB**로 탭을 이동합니다. 마지막 탭에서 다음 이동을 하면 첫 탭으로, 첫 탭에서 이전 이동을 하면 마지막 탭으로 순환합니다. **위/아래**로 페이지 항목을 이동하고, **좌/우**로 선택값을 바꾸거나 Shortcut 격자 안에서 이동합니다. **A**는 항목 실행, **B**는 이전 단계로 돌아가거나 Overlay를 닫습니다.

## Steam Quick Access와 Quick Settings Overlay 비교

두 기능은 서로 다른 화면입니다.

| 이름 | 소유자 | 열리는 화면 |
| --- | --- | --- |
| **Steam Quick Access Menu** (Steam Quick Access) | Steam | Steam 자체 Quick Access 화면. Controller에서 이 동작을 선택하면 Steam에 Quick Access 버튼을 전달합니다. |
| **Quick Settings Overlay** | Steam Addon for Claw | Addon의 Device, Profile, Controller, Shortcut, Setting 탭 |
| **ClawHUD** | Steam Addon for Claw | 화면 위의 성능 정보 표시. 설정 메뉴가 아닙니다. |
| **Steam Button** | Steam | Steam 시스템 버튼 입력. Quick Access 명령과 다르며 Addon Overlay를 열지 않습니다. |

Addon 설정 탭을 Steam Quick Access Menu 안에 삽입하지 않습니다.

## 컨트롤러 매핑 안내

내장 컨트롤러의 기본 버튼, D-Pad, 범퍼, 트리거, 스틱, 스틱 클릭은 대응하는 표준 게임패드 입력으로 전달됩니다. 컨트롤러 표현이 바뀌면 Windows에서 보이는 컨트롤러 종류가 달라지지만 기본 입력의 의미는 같습니다.

| 내장 컨트롤 | Steam Deck | DualSense Edge | Xbox 360 |
| --- | --- | --- | --- |
| A / B / X / Y | 각각 대응하는 버튼 | Cross / Circle / Square / Triangle | A / B / X / Y |
| D-Pad | 각각 대응하는 방향 | 각각 대응하는 방향 | 각각 대응하는 방향 |
| LB / RB | L1 / R1 | L1 / R1 | LB / RB |
| LT / RT 아날로그 입력 | L2 / R2 아날로그 트리거 | L2 / R2 아날로그 트리거 | LT / RT 아날로그 트리거 |
| LT / RT 끝까지 누름 | L2 / R2 디지털 Full Pull | L2 / R2 디지털 입력 | 별도 디지털 Full Pull 출력 없음 |
| 좌/우 스틱, L3 / R3 | 각각 대응하는 스틱 및 클릭 | 각각 대응하는 스틱 및 클릭 | 각각 대응하는 스틱 및 클릭 |
| View / Back | View / Options 동작 | Create | View |
| Menu / Start | Menu 동작 | Options | Menu / Start |
| M1 / M2 | R4 / L4 | R4 / L4 | Controller에서 설정, 기본값은 Disabled |

WING과 Center M은 별도로 설정하는 전면 버튼이며 일반 게임패드 버튼 매핑 표에 포함되지 않습니다. **Steam Deck과 DualSense Edge 모드에서는 자이로/모션 출력을 지원합니다.** 자이로 활성화 조건, Gyro To Mouse·조이스틱 동작 및 감도는 Addon이 아닌 Steam Input에서 설정하세요.

## 백그라운드 실행 및 업데이트

메인 앱 창을 닫아도 백그라운드 Addon과 컨트롤러 표현은 종료되지 않습니다. 작업 표시줄 알림 영역 아이콘에서 **Open** 또는 **Restart Addon**을 선택할 수 있습니다. **Open**은 메인 앱을 다시 열고, **Restart Addon**은 실행 중인 Addon을 재시작합니다. 트레이 메뉴에는 일반적인 **Quit** 명령이 없습니다. 컨트롤러 관리를 MSI Center M에 돌리려면 **Device → MSI Center M → Enable and Restart**를 사용하세요. 앱 전체 제거에는 Windows의 일반 제거 절차를 사용하세요.

Addon은 시작 후 백그라운드에서 업데이트를 확인하고 다운로드합니다. 이 과정은 현재 세션을 중단하지 않습니다. 다운로드된 업데이트는 나중에 안전한 Addon 시작 과정에서 적용됩니다. 직접 확인하려면 **Settings → Application updates → Check**를 사용하고, 업데이트가 준비되면 **Install update**를 선택하세요. 확인이나 다운로드에 실패해도 Addon은 계속 실행되며, 나중에 **Check**를 다시 선택할 수 있습니다.

## 절전·복귀·재시작

Windows 절전, 최대 절전, 복귀, 재시작 뒤 컨트롤러 연결과 설정을 복구하도록 설계되어 있습니다. 절전에서 깬 뒤 Windows가 내장 컨트롤러를 다시 연결하는 데 시간이 걸릴 수 있습니다. 컨트롤러 관리 설정을 바꾸거나 다른 컨트롤러 프로그램을 실행하기 전에 잠시 기다리세요.

잠시 기다려도 컨트롤러가 돌아오지 않으면 알림 영역에서 **Restart Addon**을 사용해 보세요. Addon의 컨트롤러 관리를 중단하려면 **Device → MSI Center M → Enable and Restart**를 선택하고 Windows 재시작을 완료하세요.

## 복구 및 문제 해결

### Steam 게임이 Xbox 컨트롤러로 표시됩니다

Steam이 실행 중이고 게임을 활성 상태로 인식하는지 확인하세요. Addon이 컨트롤러를 관리하도록 MSI Center M이 비활성화되어 있는지도 확인하세요. 게임을 시작하기 전에 전역 컨트롤러는 **Controller → Controller for Steam Game**에서 선택하고, 필요하면 Steam 프로필을 켠 뒤 **Virtual Controller** 게임별 선택을 지정하세요. Steam 게임이나 Big Picture가 활성화되면 수동 선택은 잠깁니다. 일반 Windows 환경에서는 Xbox 360을 사용합니다.

### 절전 복귀 후 컨트롤러가 응답하지 않습니다

Windows가 컨트롤러를 다시 연결할 때까지 잠시 기다린 후 필요하면 알림 영역에서 **Restart Addon**을 선택하세요. 그래도 관리할 수 없다면 **Enable and Restart**로 MSI Center M에 컨트롤러 관리를 돌려주세요. 첫 단계로 장치 드라이버를 직접 제거하거나 변경하지 마세요.

### 모터가 진동하지 않습니다

Addon이 컨트롤러를 관리하는 상태에서 **Controller → Vibration Strength**를 열고 **Left Test**와 **Right Test**를 실행하세요. 계속 무음이면 게임을 저장하고 종료한 뒤 **Vibration Recovery → Restore**를 선택하고 두 테스트를 다시 실행하세요. 실행 중 가상 컨트롤러 연결이 잠시 끊깁니다. 성공 메시지가 실제 모터 동작을 확인한 것은 아니므로 두 테스트로 확인하세요. 복구에 실패하면 필요할 때 **Device → MSI Center M → Enable and Restart**를 사용해 MSI에 컨트롤러 관리를 돌려주세요.

### WING 또는 Center M이 다른 동작을 합니다

**Controller**에서 해당 버튼의 **Normal**과 **Steam Game / Big Picture** 설정을 모두 확인하세요. 현재 컨트롤러 표현에 맞는 그룹이 사용됩니다. 다른 컨트롤러 유틸리티가 버튼 입력을 가로채고 있지 않은지도 확인하세요.

### Quick Settings Overlay가 열리지 않습니다

현재 표현 그룹에서 누르는 버튼이 **Quick Settings Overlay**로 설정되어 있는지 확인하세요. Addon이 실행 중인지(알림 영역 아이콘 확인), 컨트롤러 관리 권한을 가지고 있는지 확인합니다. 필요하면 트레이 메뉴에서 Addon을 재시작하세요.

### 게임 프로필이 적용되지 않습니다

**Steam** 또는 **XBOX**에서 올바른 게임을 선택하고 프로필과 적용할 기능을 켰는지 확인하세요. Steam 게임은 Steam이 계속 활성 상태로 인식해야 합니다. **Plugged in**과 **On battery** 중 현재 전원에 맞는 값을 확인하세요. TDP Control, CPU Boost, Windows Power Mode는 활성 게임 프로필에서 켠 경우에만 대응 Device 설정보다 우선합니다. Minimum GPU Clock, Intel FPS Limit, Resolution은 프로필 전용 설정이며 해당 프로필 기능을 켠 경우에만 적용됩니다.

### 런처를 닫은 뒤 Non-Steam 프로필 적용이 끝납니다

별도 런처가 종료되면 실제 게임이 계속 실행 중이어도 Steam이 바로가기를 활성 상태로 보지 않을 수 있습니다. Addon 프로필은 Steam이 보고하는 게임 활성 상태를 따릅니다. Steam 바로가기가 활성 상태로 유지되는 방식으로 게임을 추가하거나 실행하세요.

### ClawHUD가 보이지 않습니다

**ClawHUD**에서 **Enable HUD**를 켜고 **Display mode**를 확인하세요. 상태에 문제가 표시되면 사용할 수 있는 경우 **Retry**를 선택합니다. 일부 측정값이나 디스플레이 옵션은 기기에 따라 제공되지 않을 수 있습니다.

### XBOX 게임이 목록에 없습니다

**Refresh**를 선택한 후 다시 검색하세요. 목록은 현재 Windows 사용자의 설치 앱 중 Addon이 인식하는 Xbox/Microsoft Store 게임을 표시하며 모든 Store 앱이나 구독 게임이 포함되는 것은 아닙니다.

### Required Components가 준비되지 않았다고 표시됩니다

**Settings → Required Components**에서 상태를 확인하세요. 앱의 설정 안내를 따르고 재시작을 요청하면 Windows를 다시 시작합니다. 상태가 계속 준비되지 않으면 **Restart Addon** 후 다시 확인하세요. 상태 문제를 우회하려고 컨트롤러 드라이버를 직접 설치하거나 제거하지 마세요.

## 알려진 제한 사항

- [지원 기기](#지원-기기)에 나열된 MSI Claw 모델만 인식합니다.
- 일부 설정은 하드웨어, Windows, Intel 그래픽 지원 또는 게임 지원에 따라 특정 기기나 게임에서 사용할 수 없을 수 있습니다.
- Intel FPS 제한은 모든 게임에서 작동하지 않습니다. Minimum GPU Clock은 그래픽 드라이버가 선택 가능한 값을 제공할 때만 사용할 수 있으며 실제 클럭은 전력·온도 한도의 영향을 받습니다.
- XBOX 카탈로그는 인식된 설치 게임을 표시하며 Microsoft Store 또는 Game Pass의 모든 앱을 포함하지 않습니다.
- Steam 프로필은 Steam이 게임 또는 바로가기를 활성 상태로 보고하는지에 따라 달라집니다. 일부 런처 실행 방식에서는 Steam이 연결을 잃어 프로필 적용이 중단될 수 있습니다.
- Quick Settings Overlay와 Steam Quick Access는 별개입니다. Steam 메뉴에 Addon 탭을 넣지 않습니다.
- **Steam Deck과 DualSense Edge는 자이로/모션 입력을 지원합니다.** A2VM 7·8의 자이로 입력은 실기 검증을 완료했습니다. A2VM 8에서 Steam Deck의 Gyro To Mouse는 끊김이 있었고 DualSense Edge가 상대적으로 부드러웠습니다. 게임에 따라 차이가 있을 수 있으며, EX/CG3EM의 자이로 실기 검증은 아직 완료되지 않았습니다.
- 일반 Device 페이지의 Fan Control은 현재 사용자 기능으로 제공되지 않습니다.
- Joystick LED 및 진동 관련 기능은 모델과 펌웨어에 따라 이용 가능 여부가 다릅니다. A2VM 7·8의 지원 기능은 모두 실기 검증을 완료했습니다. 자동 진동 복구는 기본적으로 꺼져 있으며, 수동 **Restore** 이후에는 좌우 모터 **Test** 버튼으로 실제 진동을 확인하세요.
- 한 명의 대화형 Windows 사용자와 세션을 지원합니다. 빠른 사용자 전환, 원격 데스크톱 세션 및 다중 사용자 사용은 지원하지 않습니다.

## 기본 MSI 컨트롤러로 복귀 및 제거

MSI Center M으로 컨트롤러 관리를 돌리려면 **Device**에서 **Enable**을 선택하고 **Enable and Restart**를 확인하세요. Windows가 다시 시작될 때까지 기다린 후 MSI 기본 컨트롤러 환경을 사용하세요.

앱을 제거하려면 **Windows 설정 → 앱 → 설치된 앱**에서 일반적인 제거 항목을 사용해 **Uninstall**을 선택합니다. 제거 전에 안전한 컨트롤러 전환을 준비합니다. 준비가 완료되지 않았다는 메시지가 나오면 컨트롤러 구성 요소를 강제로 제거하지 말고 안내된 문제를 해결한 뒤 다시 시도하세요. 재설치를 위해 Addon 설정, 게임 프로필, 바로가기, 로그는 제거 후에도 보존됩니다. Addon에 포함된 ClawHUD는 앱 제거 시 함께 제거됩니다.

## 라이선스

Steam Addon for Claw는 [`LICENSE`](../../LICENSE)에 명시된 라이선스로 배포됩니다. 포함된 타사 구성 요소에는 설치 파일에 동봉된 각각의 라이선스와 고지가 적용됩니다.
