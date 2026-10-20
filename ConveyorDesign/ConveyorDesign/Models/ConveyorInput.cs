using System.Text.Json.Serialization;
using ConveyorDesign.ViewModels;

namespace ConveyorDesign.Models;

public enum ConveyorType
{
    Belt,    // 벨트 컨베어 (고무/PVC 벨트, 모듈 플라스틱 벨트)
    Roller,  // 롤러 컨베어 (체인 구동 롤러)
    Chain    // 체인 컨베어 (슬랫, 톱체인 등)
}

public enum BeltKind
{
    Rubber,   // 고무·PVC 벨트 (오일러 장력식)
    Modular   // 모듈 플라스틱 벨트 (FMS/Siegling 계산법: C1, C2, C3)
}

public enum Transmission
{
    Direct,     // 감속기 출력축 ↔ 구동축 직결 (커플링, 중공축)
    ChainDrive  // 감속기 출력축 → 스프라켓/체인 → 구동축
}

public enum MotorKind
{
    Induction,  // 3상 유도전동기 (+인버터) — kW 기준 선정
    Servo       // AC 서보모터 — 토크·관성비·회전수 기준 선정
}

public enum LoadClass
{
    Uniform,      // 균일 부하
    LightShock,   // 가벼운 충격 부하
    HeavyShock    // 무거운 충격 부하
}

public enum DailyHours
{
    Under3,   // 3시간 이하
    H3to10,   // 3~10시간
    Over10    // 10시간 이상
}

public enum OhlPosition
{
    ShaftRoot,   // 축의 밑부분 (K2 = 0.75)
    ShaftCenter, // 축의 중앙 (K2 = 1.00)
    ShaftEnd     // 축단 (K2 = 1.50)
}

/// <summary>
/// 컨베어 선정 입력값. 기호는 닛세이(NISSEI) 계산선정 툴과 FMS(Siegling) 자료의 표기를 따른다.
/// </summary>
public class ConveyorInput : ObservableObject
{
    // ───── 기본 ─────
    private string _projectName = "컨베어-01";
    public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }

    private ConveyorType _type = ConveyorType.Belt;
    public ConveyorType Type { get => _type; set => Set(ref _type, value); }

    private BeltKind _beltKind = BeltKind.Rubber;
    public BeltKind BeltKind { get => _beltKind; set => Set(ref _beltKind, value); }

    // ───── 반송 조건 ─────
    private double _speed = 20;          // V [m/min]
    public double Speed { get => _speed; set => Set(ref _speed, value); }

    private double _length = 10;         // lT [m] 컨베어 길이 (축간 거리)
    public double Length { get => _length; set => Set(ref _length, value); }

    private double _angle = 0;           // θ [°] 경사각 (+상승, -하강)
    public double Angle { get => _angle; set => Set(ref _angle, value); }

    private double _workMass = 20;       // M1 [kg/개]
    public double WorkMass { get => _workMass; set => Set(ref _workMass, value); }

    private int _workCount = 10;         // n1 [개] 컨베어 위에 동시에 올라가는 수
    public int WorkCount { get => _workCount; set => Set(ref _workCount, value); }

    private bool _accumulation = false;  // 축적(저류) 운전 여부
    public bool Accumulation { get => _accumulation; set => Set(ref _accumulation, value); }

    private double _frictionProduct = 0.3;  // μST 제품-벨트(롤러) 마찰계수 (축적 시)
    public double FrictionProduct { get => _frictionProduct; set => Set(ref _frictionProduct, value); }

    // ───── 벨트 컨베어 ─────
    private double _beltWidth = 500;     // b0 [mm]
    public double BeltWidth { get => _beltWidth; set => Set(ref _beltWidth, value); }

    private double _beltMassPerM2 = 6;   // 벨트 단위질량 [kg/m²] (PVC 2ply ≈ 3~5, 고무 ≈ 8~12, 모듈벨트 ≈ 6~10)
    public double BeltMassPerM2 { get => _beltMassPerM2; set => Set(ref _beltMassPerM2, value); }

    private double _frictionBelt = 0.3;  // μT 벨트-지지면 마찰계수 (슬라이더베드 0.3, 롤러지지 0.03~0.05)
    public double FrictionBelt { get => _frictionBelt; set => Set(ref _frictionBelt, value); }

    private double _pulleyDiameter = 150; // D1 [mm] 구동 풀리 외경
    public double PulleyDiameter { get => _pulleyDiameter; set => Set(ref _pulleyDiameter, value); }

    private double _pulleyMass = 15;     // mW [kg] 구동 풀리 질량
    public double PulleyMass { get => _pulleyMass; set => Set(ref _pulleyMass, value); }

    private double _wrapAngle = 180;     // [°] 벨트 감김각
    public double WrapAngle { get => _wrapAngle; set => Set(ref _wrapAngle, value); }

    private double _frictionPulley = 0.3; // 벨트-풀리 마찰계수 (맨 강철 0.25~0.3, 고무 라깅 0.35~0.4)
    public double FrictionPulley { get => _frictionPulley; set => Set(ref _frictionPulley, value); }

    private double _beltAllowableTension = 8; // [N/mm] 벨트 허용 장력 (폭당) — 고무/PVC 벨트
    public double BeltAllowableTension { get => _beltAllowableTension; set => Set(ref _beltAllowableTension, value); }

    // 모듈 벨트 (FMS) 계수
    private bool _c1StartStop = false;     // 시작-멈춤 운전 +0.2
    public bool C1StartStop { get => _c1StartStop; set => Set(ref _c1StartStop, value); }
    private bool _c1Pushing = false;       // 종동(미는) 구동 +0.2
    public bool C1Pushing { get => _c1Pushing; set => Set(ref _c1Pushing, value); }
    private double _c2Temperature = 1.0;   // 온도계수 C2
    public double C2Temperature { get => _c2Temperature; set => Set(ref _c2Temperature, value); }
    private double _c3Max = 30;            // 허용 벨트장력 C3max [N/mm]
    public double C3Max { get => _c3Max; set => Set(ref _c3Max, value); }

    // ───── 롤러 컨베어 ─────
    private double _rollerDiameter = 60.5;  // D1 [mm] 롤러 외경
    public double RollerDiameter { get => _rollerDiameter; set => Set(ref _rollerDiameter, value); }

    private double _rollerPitch = 100;      // [mm] 롤러 피치
    public double RollerPitch { get => _rollerPitch; set => Set(ref _rollerPitch, value); }

    private double _rollerMass = 2.5;       // M2 [kg/개] 롤러 질량 (스프라켓 포함)
    public double RollerMass { get => _rollerMass; set => Set(ref _rollerMass, value); }

    private double _rollerShaftDiameter = 15;   // D3 [mm] 롤러 축경
    public double RollerShaftDiameter { get => _rollerShaftDiameter; set => Set(ref _rollerShaftDiameter, value); }

    private double _frictionRolling = 0.05;     // μ1 워크-롤러 굴림마찰 (닛세이 참고: 0.2~0.3은 축적 포함 시, 통상 0.03~0.1)
    public double FrictionRolling { get => _frictionRolling; set => Set(ref _frictionRolling, value); }

    private double _frictionBearing = 0.05;     // μ2 롤러 베어링 마찰계수 (0.05~0.1)
    public double FrictionBearing { get => _frictionBearing; set => Set(ref _frictionBearing, value); }

    private double _rollerChainMassPerM = 1.0;  // 롤러 구동 체인 질량 [kg/m] (RS40 ≈ 0.64, RS50 ≈ 1.04)
    public double RollerChainMassPerM { get => _rollerChainMassPerM; set => Set(ref _rollerChainMassPerM, value); }

    // ───── 체인 컨베어 ─────
    private double _chainPitch = 38.1;      // [mm] 컨베어 체인 피치
    public double ChainPitch { get => _chainPitch; set => Set(ref _chainPitch, value); }

    private int _chainStrands = 2;          // 체인 열 수
    public int ChainStrands { get => _chainStrands; set => Set(ref _chainStrands, value); }

    private int _conveyorSprocketTeeth = 15; // z 구동 스프라켓 잇수 (컨베어 체인)
    public int ConveyorSprocketTeeth { get => _conveyorSprocketTeeth; set => Set(ref _conveyorSprocketTeeth, value); }

    private double _chainMassPerM = 4.0;    // [kg/m·열] 체인+슬랫 등 부속 포함 단위질량
    public double ChainMassPerM { get => _chainMassPerM; set => Set(ref _chainMassPerM, value); }

    private double _frictionChain = 0.2;    // μ 체인-가이드 마찰계수 (닛세이 참고 0.2~0.3, 롤러체인+강 레일 0.12~0.15, 윤활 0.1)
    public double FrictionChain { get => _frictionChain; set => Set(ref _frictionChain, value); }

    private double _conveyorSprocketMass = 5.0; // M2 [kg/개] 스프라켓 질량
    public double ConveyorSprocketMass { get => _conveyorSprocketMass; set => Set(ref _conveyorSprocketMass, value); }

    private double _chainAllowableLoad = 0;   // [kN] 컨베어 체인 최대허용하중 (0이면 표준 RS 체인 표에서 자동)
    public double ChainAllowableLoad { get => _chainAllowableLoad; set => Set(ref _chainAllowableLoad, value); }

    // ───── 구동계 ─────
    private MotorKind _motorKind = MotorKind.Induction;
    public MotorKind MotorKind { get => _motorKind; set => Set(ref _motorKind, value); }

    // 서보모터 조건
    private double _servoRatedRpm = 3000;        // 서보 정격 회전수 [rpm]
    public double ServoRatedRpm { get => _servoRatedRpm; set => Set(ref _servoRatedRpm, value); }

    private double _servoMaxTorqueRatio = 3.0;   // 순시 최대토크 / 정격토크
    public double ServoMaxTorqueRatio { get => _servoMaxTorqueRatio; set => Set(ref _servoMaxTorqueRatio, value); }

    private double _servoMaxInertiaRatio = 10;   // 허용 관성비 (J부하환산 / J모터)
    public double ServoMaxInertiaRatio { get => _servoMaxInertiaRatio; set => Set(ref _servoMaxInertiaRatio, value); }

    private bool _servoInertiaAsWarning = false;  // true: 관성비 초과를 선정 조건에서 빼고 경고만 (저속·저응답 컨베어)
    public bool ServoInertiaAsWarning { get => _servoInertiaAsWarning; set => Set(ref _servoInertiaAsWarning, value); }

    private double _servoTorqueUtilization = 0.8; // 실효토크 ≤ 정격 × 이 값
    public double ServoTorqueUtilization { get => _servoTorqueUtilization; set => Set(ref _servoTorqueUtilization, value); }

    private double _servoSpeedUtilization = 0.9;  // 최고 회전수 ≤ 정격 × 이 값
    public double ServoSpeedUtilization { get => _servoSpeedUtilization; set => Set(ref _servoSpeedUtilization, value); }

    private double _moveDistance = 0;             // [mm] 1사이클 이동거리 (0 = 연속 운전)
    public double MoveDistance { get => _moveDistance; set => Set(ref _moveDistance, value); }

    private double _decelTime = 0.2;              // [s] 감속 시간
    public double DecelTime { get => _decelTime; set => Set(ref _decelTime, value); }

    private double _dwellTime = 0.5;              // [s] 정지 시간
    public double DwellTime { get => _dwellTime; set => Set(ref _dwellTime, value); }

    private int _motorPoles = 4;
    public int MotorPoles { get => _motorPoles; set => Set(ref _motorPoles, value); }

    private double _frequency = 60;
    public double Frequency { get => _frequency; set => Set(ref _frequency, value); }

    private Transmission _transmission = Transmission.ChainDrive;
    public Transmission Transmission { get => _transmission; set => Set(ref _transmission, value); }

    private bool _autoSelectSprockets = true;    // 감속비 + 스프라켓 잇수 자동 선정
    public bool AutoSelectSprockets { get => _autoSelectSprockets; set => Set(ref _autoSelectSprockets, value); }

    private int _driveSprocketTeeth = 15;        // z3 감속기측 스프라켓 잇수 (수동)
    public int DriveSprocketTeeth { get => _driveSprocketTeeth; set => Set(ref _driveSprocketTeeth, value); }

    private int _drivenSprocketTeeth = 30;       // z2 구동축측 스프라켓 잇수 (수동)
    public int DrivenSprocketTeeth { get => _drivenSprocketTeeth; set => Set(ref _drivenSprocketTeeth, value); }

    private double _manualReducerRatio = 0;      // 0이면 자동
    public double ManualReducerRatio { get => _manualReducerRatio; set => Set(ref _manualReducerRatio, value); }

    private double _transmissionCenterDistance = 500; // [mm] 전동 체인 축간거리
    public double TransmissionCenterDistance { get => _transmissionCenterDistance; set => Set(ref _transmissionCenterDistance, value); }

    private double _reducerEfficiency = 0.9;   // 감속기 효율 (평행축 0.9~0.95, 웜 0.5~0.8)
    public double ReducerEfficiency { get => _reducerEfficiency; set => Set(ref _reducerEfficiency, value); }

    private double _transmissionEfficiency = 0.95; // 전동 체인 효율
    public double TransmissionEfficiency { get => _transmissionEfficiency; set => Set(ref _transmissionEfficiency, value); }

    private LoadClass _loadClass = LoadClass.LightShock;
    public LoadClass LoadClass { get => _loadClass; set => Set(ref _loadClass, value); }

    private DailyHours _dailyHours = DailyHours.Over10;
    public DailyHours DailyHours { get => _dailyHours; set => Set(ref _dailyHours, value); }

    private int _startsPerDay = 50;            // 기동 횟수/일 (관성 보정계수 C)
    public int StartsPerDay { get => _startsPerDay; set => Set(ref _startsPerDay, value); }

    private OhlPosition _ohlPosition = OhlPosition.ShaftCenter;
    public OhlPosition OhlPosition { get => _ohlPosition; set => Set(ref _ohlPosition, value); }

    private double _motorMargin = 1.2;         // 모터 용량 여유율
    public double MotorMargin { get => _motorMargin; set => Set(ref _motorMargin, value); }

    private double _accelTime = 1.0;           // [s] 기동 가속 시간 (기동 토크 검토)
    public double AccelTime { get => _accelTime; set => Set(ref _accelTime, value); }

    // ───── 파생 값 ─────
    [JsonIgnore] public double TotalWorkMass => WorkMass * WorkCount;

    [JsonIgnore] public double AngleRad => Angle * Math.PI / 180.0;

    public ConveyorInput Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<ConveyorInput>(json) ?? new ConveyorInput();
    }
}
