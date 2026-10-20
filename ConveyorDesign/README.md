# 컨베어 구동부 선정 프로그램 (ConveyorDesign)

벨트 / 롤러 / 체인 컨베어의 **모터 · 감속기 · 스프라켓 · 전동체인 · 풀리**를 선정하는 WPF(.NET 8) 프로그램.
입력 체계는 닛세이(NISSEI) 기어모터 계산선정 툴, 벨트 장력 계산은 FMS(Siegling Prolink) 자료를 따른다.

## 빌드

- Visual Studio 2022 (17.8 이상) + ".NET 데스크톱 개발" 워크로드, 또는 .NET 8 SDK
- `ConveyorDesign.sln` 열고 빌드 (외부 NuGet 패키지 없음)
- 명령줄: `dotnet build -c Release` → `ConveyorDesign\bin\Release\net8.0-windows\ConveyorDesign.exe`

## 계산 흐름

| 단계 | 내용 | 식 |
|---|---|---|
| 1 부하 | 유효장력 FU | 벨트/체인: `μ·g·(m+mB)·cosθ + g·m·sinθ (+ μST·g·m 축적)`<br>롤러: `μ1·g·m·cosθ + μ2·g·(m·cosθ+mR)·(d축/D1) + 체인저항 + g·m·sinθ` |
| 2 구동축 | 회전수·토크·축동력 | `n1 = V·1000/(π·D1)` (체인: `V·1000/(p·z)`), `T1 = FU·D1/2`, `PA = FU·V/60000` |
| 3 모터 (유도) | 표준 kW | `P = PA/(η감속기·η전동) × 여유율` → KS 표준 용량 올림 |
| 3 모터 (서보) | 용량·감속비 | 운전 패턴(가속·정속·감속·정지)으로 `TL`, `TA = (JM+JL)·ω/ta + TL`, `TD`, `Trms = √(ΣT²t/Σt)` 계산. 관성비 `JL/JM ≤ 허용`, 최고 회전수 ≤ 정격×사용률, `Trms ≤ 정격×사용률`, `max(TA,TD) ≤ 최대토크` 를 만족하는 최소 용량 + 감속비 조합 |
| 4 감속기·스프라켓 | 감속비, z3/z2 | 표준 감속비 × 스프라켓 잇수 조합 중 속도 오차 최소 (자동) |
| 5 전동 체인 | 체인 호수·링크수 | 설계동력 `P×K1` ≥ 정격(17T 기준, 잇수 보정) / `L = (z2+z3)/2 + 2C/p + ((z2−z3)/2π)²·p/C` |
| 6 검토 | 토크·OHL·관성·기동 | 출력토크 부하율(Sf), `OHL = F·K1·K2`, 모터축 환산 J×C, 기동토크 = T부하 + J·ω/t |

벨트 장력
- 고무/PVC: 오일러식 `T1 = FU·e^μθ/(e^μθ−1)`, 폭당 장력 ≤ 허용값
- 모듈벨트(FMS): `FB = FU·C1/C2`, `C3 = FB/b0 ≤ C3max`, `FW ≈ FU·C1 + mW·g`

## 데이터 (Data/StandardData.cs)

- 모터 표준 출력 (KS C 4202), 극수별 정격 회전수
- 감속비 표준열 (1/3 ~ 1/1200)
- 롤러체인 RS25~RS200: 피치, 인장강도, 최대허용하중, 단위질량, 정격 전달동력표(근사)
- 스프라켓 잇수, 풀리/롤러 표준 외경
- 서비스팩터 Sf, 관성보정계수 C, OHL 계수 K1/K2 (닛세이 기준)

> 모든 수치는 **표준 규격 기반 근사치**다. 최종 선정은 제조사 카탈로그(허용토크, 허용 OHL, 허용 부하관성, 체인 정격표)로 확인해야 한다.
> 제조사 카탈로그 데이터를 `StandardData.cs`에 추가하면 특정 제품 모델까지 선정하도록 확장할 수 있다.

## 파일

- `*.cvd.json` — 입력 조건 저장 파일 (열기/저장)
- 보고서 내보내기 — 선정 사양표 + 계산 근거 (.txt) 또는 사양표만 (.csv)

## 구조

```
ConveyorDesign/
  Models/      ConveyorInput (입력), SelectionResult (결과)
  Data/        StandardData (표준 규격 테이블)
  Engine/      ConveyorCalculator (계산·선정 엔진)
  ViewModels/  MainViewModel, ObservableObject, RelayCommand
  MainWindow.xaml  입력(좌) / 선정 요약·사양표·계산 근거(우)
```
