using System.Text;
using ConveyorDesign.Data;
using ConveyorDesign.Models;

namespace ConveyorDesign.Engine;

/// <summary>
/// 컨베어 구동부(모터·감속기·스프라켓·풀리·체인) 선정 계산 엔진.
///
/// 계산 순서
///  1. 부하 계산      : 유효장력 FU [N]  (FMS/Siegling 계산법 + 닛세이 입력 체계)
///  2. 구동축         : 회전수 n1, 토크 T1, 축동력 PA
///  3. 모터 선정
///     - 유도전동기   : PA / (η감속기 · η전동) × 여유율 → 표준 kW, 감속비·스프라켓은 속도 오차 최소 조합
///     - 서보모터     : 운전 패턴(가속·정속·감속·정지)으로 부하/가감속/실효 토크와 관성비 계산,
///                      관성비·최고회전수·토크 조건을 만족하는 최소 용량 + 감속비 조합
///  4. 전동 체인      : 사용계수 K1 적용 설계동력 ≥ 체인 정격 → 체인 번호, 링크수
///  5. 검토          : 감속기 출력토크(Sf), OHL(K1·K2), 관성(C), 기동토크, 벨트/체인 장력
/// </summary>
public static class ConveyorCalculator
{
    private sealed class LoadStage
    {
        public double Fu;          // 유효장력 [N]
        public double D1;          // 구동 요소(풀리/롤러/스프라켓 PCD) 지름 [mm]
        public double J;           // 구동축 환산 부하 관성 [kg·m²]
        public double MovingMass;  // 이동부 질량(벨트/체인/롤러) [kg]
        public double Mw;          // 구동 풀리/스프라켓 질량 [kg]
        public string SpeedBasis = "";
        public Func<double, double> RpmToSpeed = rpm => 0; // 구동축 rpm → m/min
    }

    /// <summary>모터·감속기·전동비 선정 결과 (유도/서보 공통)</summary>
    private sealed class DriveSel
    {
        public double MotorKw;        // 선정 모터 용량
        public double MotorRpm;       // 운전 시 모터 회전수 (유도: 정격, 서보: 실제 필요 회전수)
        public double MotorRatedTorque;
        public double Ratio;          // 감속비 i1
        public int Z3, Z2;            // 전동 스프라켓 (감속기측, 구동축측)
        public double ITr;            // 전동비 z2/z3
        public double NOut;           // 감속기 출력 회전수
        public double TOutReq;        // 감속기 출력축 필요 토크
        public double VAct;           // 실제 반송속도
        public double SpeedErr;       // 속도 오차 %
    }

    private const string G_LOAD = "1. 부하";
    private const string G_SHAFT = "2. 구동축";
    private const string G_MOTOR = "3. 모터";
    private const string G_REDUCER = "4. 감속기";
    private const string G_SPROCKET = "5. 스프라켓/전동체인";
    private const string G_CHECK = "6. 검토";

    public static SelectionResult Calculate(ConveyorInput i)
    {
        var r = new SelectionResult();
        var log = new StringBuilder();
        try
        {
            Validate(i);
            log.AppendLine($"■ {i.ProjectName}  —  {TypeName(i)}  /  {(i.MotorKind == MotorKind.Servo ? "서보모터" : "유도전동기")}  계산 근거");
            log.AppendLine($"   반송속도 V = {i.Speed} m/min, 길이 L = {i.Length} m, 경사각 θ = {i.Angle}°");
            log.AppendLine($"   워크 M1 = {i.WorkMass} kg/개 × n1 = {i.WorkCount} 개 → m = {i.TotalWorkMass:0.##} kg");
            log.AppendLine();

            LoadStage ls = i.Type switch
            {
                ConveyorType.Belt => BeltLoad(i, r, log),
                ConveyorType.Roller => RollerLoad(i, r, log),
                _ => ChainLoad(i, r, log)
            };

            // ───── 2. 구동축 ─────
            double n1 = i.Type == ConveyorType.Chain
                ? i.Speed * 1000.0 / (i.ChainPitch * i.ConveyorSprocketTeeth)
                : i.Speed * 1000.0 / (Math.PI * ls.D1);
            double T1 = ls.Fu * (ls.D1 / 2000.0);
            double PA = ls.Fu * i.Speed / 60000.0;

            log.AppendLine("[2] 구동축");
            if (i.Type == ConveyorType.Chain)
                log.AppendLine($"   n1 = V·1000 / (p·z) = {i.Speed}·1000 / ({i.ChainPitch}·{i.ConveyorSprocketTeeth}) = {n1:0.##} rpm");
            else
                log.AppendLine($"   n1 = V·1000 / (π·D1) = {i.Speed}·1000 / (π·{ls.D1:0.##}) = {n1:0.##} rpm");
            log.AppendLine($"   T1 = FU · D1/2 = {ls.Fu:0.#} N × {ls.D1 / 2000.0:0.####} m = {T1:0.##} N·m");
            log.AppendLine($"   PA = FU·V / 60000 = {ls.Fu:0.#} × {i.Speed} / 60000 = {PA:0.###} kW");
            log.AppendLine($"   구동축 환산 부하 관성 J = {ls.J:0.####} kg·m²  (GD² = {4 * ls.J:0.###} kg·m²)");
            log.AppendLine();

            r.EffectiveTension = ls.Fu;
            r.DriveShaftRpm = n1;
            r.DriveShaftTorque = T1;
            r.ShaftPowerKw = PA;
            r.Add(G_SHAFT, "구동축 회전수 n1", n1, "rpm", "0.##", ls.SpeedBasis);
            r.Add(G_SHAFT, "구동축 토크 T1", T1, "N·m");
            r.Add(G_SHAFT, "축동력 PA", PA, "kW", "0.###");
            r.Add(G_SHAFT, "구동축 환산 관성 GD²", 4 * ls.J, "kg·m²", "0.###", $"J = {ls.J:0.####} kg·m²");

            // ───── 3·4. 모터 / 감속기 / 전동비 ─────
            DriveSel d = i.MotorKind == MotorKind.Servo
                ? SelectServo(i, ls, n1, T1, r, log)
                : SelectInduction(i, ls, n1, T1, PA, r, log);

            r.RequiredMotorKw = PA;
            r.SelectedMotorKw = d.MotorKw;
            r.MotorRpm = d.MotorRpm;
            r.ReducerRatio = d.Ratio;
            r.ReducerOutputRpm = d.NOut;
            r.ReducerOutputTorque = d.TOutReq;
            r.DriveSprocketTeeth = d.Z3;
            r.DrivenSprocketTeeth = d.Z2;
            r.ActualSpeed = d.VAct;
            r.SpeedErrorPercent = d.SpeedErr;

            // ───── 5. 전동 체인 ─────
            bool chainDrive = i.Transmission == Transmission.ChainDrive;
            r.Add(G_SPROCKET, "전동 방식", chainDrive ? "체인 전동" : "직결");
            if (chainDrive)
            {
                r.Add(G_SPROCKET, "감속기측 스프라켓 z3", d.Z3, "T");
                r.Add(G_SPROCKET, "구동축측 스프라켓 z2", d.Z2, "T");
                r.Add(G_SPROCKET, "전동비 i2 = z2/z3", d.ITr, "", "0.###");
            }
            r.Add(G_SPROCKET, "실제 반송속도", d.VAct, "m/min", "0.##",
                i.MotorKind == MotorKind.Servo ? "서보 속도지령으로 맞춤" : $"오차 {d.SpeedErr:+0.0;-0.0} %");
            if (i.MotorKind == MotorKind.Induction && Math.Abs(d.SpeedErr) > 5)
                r.Warnings.Add($"실제 속도 오차가 {d.SpeedErr:+0.0;-0.0}% 입니다. 감속비 또는 스프라켓 잇수를 조정하십시오.");

            if (chainDrive) TransmissionChainStage(i, d, r, log);

            // ───── 6. 검토 (유도전동기 기동) ─────
            if (i.MotorKind == MotorKind.Induction) InductionStartupCheck(i, ls, d, r, log);

            r.Add(G_CHECK, "참고", "표준 규격 데이터 기반", "", "최종 선정은 제조사 카탈로그(허용토크·OHL·관성)로 확인");
        }
        catch (Exception ex)
        {
            r.Ok = false;
            r.Warnings.Add("계산 오류: " + ex.Message);
            log.AppendLine("!! " + ex);
        }
        r.CalculationLog = log.ToString();
        return r;
    }

    // ══════════════════ 유도전동기 선정 ══════════════════

    private static DriveSel SelectInduction(ConveyorInput i, LoadStage ls, double n1, double T1, double PA, SelectionResult r, StringBuilder log)
    {
        bool chainDrive = i.Transmission == Transmission.ChainDrive;
        double etaTr = chainDrive ? i.TransmissionEfficiency : 1.0;
        double etaTotal = i.ReducerEfficiency * etaTr;
        double pReq = PA / etaTotal * i.MotorMargin;
        double pMotor = StandardData.CeilToStandard(pReq, StandardData.MotorKw);
        double nMotor = StandardData.MotorRatedRpm(i.MotorPoles, i.Frequency);
        double sf = StandardData.ServiceFactor(i.LoadClass, i.DailyHours);

        log.AppendLine("[3] 모터 (유도전동기)");
        log.AppendLine($"   η = η감속기 × η전동 = {i.ReducerEfficiency} × {etaTr} = {etaTotal:0.###}");
        log.AppendLine($"   P필요 = PA / η × 여유율 = {PA:0.###} / {etaTotal:0.###} × {i.MotorMargin} = {pReq:0.###} kW");
        log.AppendLine($"   → 표준 모터 {pMotor} kW, {i.MotorPoles}P, {i.Frequency} Hz, 정격 약 {nMotor} rpm");
        log.AppendLine($"   서비스팩터 Sf = {sf}  ({LoadName(i.LoadClass)}, {HoursName(i.DailyHours)})");

        double iTr = 1.0; int z3 = 0, z2 = 0; double ratio;
        if (chainDrive && !i.AutoSelectSprockets)
        {
            z3 = i.DriveSprocketTeeth; z2 = i.DrivenSprocketTeeth; iTr = (double)z2 / z3;
        }
        if (i.ManualReducerRatio > 0)
        {
            ratio = i.ManualReducerRatio;
            if (chainDrive && i.AutoSelectSprockets)
            {
                (z3, z2) = BestTeethPair(nMotor / ratio / n1); iTr = (double)z2 / z3;
            }
        }
        else if (chainDrive && i.AutoSelectSprockets)
        {
            (ratio, z3, z2) = BestRatioAndTeeth(nMotor, n1); iTr = (double)z2 / z3;
        }
        else
        {
            ratio = StandardData.NearestStandard(nMotor / (n1 * iTr), StandardData.ReducerRatios);
        }

        double nOut = nMotor / ratio;
        double n1Act = nOut / iTr;
        double vAct = ls.RpmToSpeed(n1Act);
        double speedErr = (vAct - i.Speed) / i.Speed * 100.0;
        double tOutReq = T1 / (iTr * etaTr);

        log.AppendLine();
        log.AppendLine("[4] 감속비 / 스프라켓");
        if (chainDrive) log.AppendLine($"   전동 스프라켓 z3(감속기측) = {z3} T, z2(구동축측) = {z2} T → 전동비 i2 = z2/z3 = {iTr:0.###}");
        else log.AppendLine("   직결 (전동비 1.0)");
        log.AppendLine($"   필요 총감속비 = n모터 / n1 = {nMotor} / {n1:0.##} = {nMotor / n1:0.##}");
        log.AppendLine($"   감속기 감속비 i1 = {ratio}  (표준값),  출력 회전수 = {nMotor} / {ratio} = {nOut:0.##} rpm");
        log.AppendLine($"   실제 구동축 회전수 = {nOut:0.##} / {iTr:0.###} = {n1Act:0.##} rpm → 실제 속도 {vAct:0.##} m/min (오차 {speedErr:+0.0;-0.0} %)");
        log.AppendLine($"   감속기 출력축 필요 토크 = T1 / (i2·η전동) = {T1:0.##} / ({iTr:0.###}·{etaTr}) = {tOutReq:0.##} N·m");

        double tAvail = 9550.0 * pMotor * i.ReducerEfficiency / nOut;
        int bumps = 0;
        while (tOutReq * sf > tAvail && bumps < 5)
        {
            double next = StandardData.CeilToStandard(pMotor * 1.01, StandardData.MotorKw);
            if (next <= pMotor) break;
            pMotor = next; bumps++;
            tAvail = 9550.0 * pMotor * i.ReducerEfficiency / nOut;
        }
        if (bumps > 0)
        {
            log.AppendLine($"   ※ 출력토크 부족으로 모터를 {pMotor} kW 로 상향 (Sf {sf} 적용)");
            r.Warnings.Add($"서비스팩터 적용 시 출력토크가 부족하여 모터를 {pMotor} kW로 상향했습니다.");
        }
        double loadRate = tOutReq * sf / tAvail * 100.0;
        double tMotorRated = 9550.0 * pMotor / nMotor;
        log.AppendLine($"   기어모터 정격 출력토크 = 9550·P·η감속기 / n출력 = 9550·{pMotor}·{i.ReducerEfficiency} / {nOut:0.##} = {tAvail:0.##} N·m");
        log.AppendLine($"   토크 부하율 = T필요·Sf / T정격 = {tOutReq:0.##}·{sf} / {tAvail:0.##} = {loadRate:0.#} %");

        r.Add(G_MOTOR, "모터 종류", "3상 유도전동기");
        r.Add(G_MOTOR, "필요 동력 (여유율 포함)", pReq, "kW", "0.###", $"PA/η×{i.MotorMargin}");
        r.Add(G_MOTOR, "선정 모터", $"{pMotor} kW  {i.MotorPoles}P", "", $"{i.Frequency} Hz, 약 {nMotor} rpm");
        r.Add(G_MOTOR, "모터 정격 토크", tMotorRated, "N·m");
        r.Add(G_MOTOR, "서비스팩터 Sf", sf, "", "0.##", $"{LoadName(i.LoadClass)} / {HoursName(i.DailyHours)}");

        r.Add(G_REDUCER, "감속비 i1", $"1/{ratio:0.#}", "", $"필요 총감속비 {nMotor / n1:0.##}");
        r.Add(G_REDUCER, "출력 회전수", nOut, "rpm");
        r.Add(G_REDUCER, "출력축 필요 토크", tOutReq, "N·m", "0.##", "구동축 토크를 전동비·효율로 환산");
        r.Add(G_REDUCER, "출력축 정격 토크 (모터 기준)", tAvail, "N·m", "0.##", "9550·P·η/n");
        r.Add(G_REDUCER, "토크 부하율 (Sf 적용)", loadRate, "%", "0.#", loadRate <= 100 ? "OK" : "NG — 용량 상향 필요");
        if (loadRate > 100) r.Warnings.Add("감속기 출력토크 부하율이 100%를 초과합니다. 모터/감속기 용량을 올리십시오.");

        return new DriveSel
        {
            MotorKw = pMotor, MotorRpm = nMotor, MotorRatedTorque = tMotorRated, Ratio = ratio, Z3 = z3, Z2 = z2, ITr = iTr,
            NOut = nOut, TOutReq = tOutReq, VAct = vAct, SpeedErr = speedErr
        };
    }

    // ══════════════════ 서보모터 선정 ══════════════════

    private static DriveSel SelectServo(ConveyorInput i, LoadStage ls, double n1, double T1, SelectionResult r, StringBuilder log)
    {
        bool chainDrive = i.Transmission == Transmission.ChainDrive;
        double etaTr = chainDrive ? i.TransmissionEfficiency : 1.0;
        double etaTotal = i.ReducerEfficiency * etaTr;
        double nMax = i.ServoRatedRpm * i.ServoSpeedUtilization;

        // 운전 패턴
        double v = i.Speed / 60.0;                       // m/s
        double ta = i.AccelTime, td = i.DecelTime, tdw = i.DwellTime;
        double tc; bool triangular = false; double vPeak = v;
        if (i.MoveDistance > 0)
        {
            double s = i.MoveDistance / 1000.0;
            tc = (s - v * (ta + td) / 2.0) / v;
            if (tc < 0)
            {
                // 삼각 프로파일: 지정 거리로는 최고속도 도달 불가 → 도달 가능한 최고속도로 환산
                triangular = true; tc = 0;
                vPeak = 2.0 * s / (ta + td);
            }
        }
        else { tc = 0; tdw = 0; }  // 연속 운전: 가속 후 정속 (실효토크는 부하토크 기준)
        bool continuous = i.MoveDistance <= 0;
        double tCycle = ta + tc + td + tdw;

        log.AppendLine("[3] 모터 (서보모터)");
        log.AppendLine($"   정격 회전수 {i.ServoRatedRpm} rpm, 최고 사용 회전수 = 정격 × {i.ServoSpeedUtilization} = {nMax:0} rpm");
        log.AppendLine($"   최대토크 = 정격 × {i.ServoMaxTorqueRatio}, 실효토크 ≤ 정격 × {i.ServoTorqueUtilization}, 허용 관성비 ≤ {i.ServoMaxInertiaRatio}");
        if (continuous)
            log.AppendLine($"   운전 패턴: 연속 운전 (가속 {ta} s 후 정속)");
        else
            log.AppendLine($"   운전 패턴: 이동거리 {i.MoveDistance} mm — 가속 {ta} s, 정속 {tc:0.###} s, 감속 {td} s, 정지 {tdw} s → 사이클 {tCycle:0.###} s" +
                           (triangular ? $"  ※ 삼각 프로파일 (최고속도 {vPeak * 60:0.##} m/min 로 제한)" : ""));

        // 후보 탐색: 감속비 × (스프라켓 조합) × 서보 용량
        var ratios = i.ManualReducerRatio > 0 ? new[] { i.ManualReducerRatio } : StandardData.ServoReducerRatios;
        List<(int z3, int z2)> pairs = new();
        if (!chainDrive) pairs.Add((0, 0));
        else if (!i.AutoSelectSprockets) pairs.Add((i.DriveSprocketTeeth, i.DrivenSprocketTeeth));
        else
            foreach (int z3 in StandardData.SprocketTeeth)
            {
                if (z3 < 13 || z3 > 25) continue;
                foreach (int z2 in StandardData.SprocketTeeth)
                    if ((double)z2 / z3 >= 1.0 && (double)z2 / z3 <= 4.0) pairs.Add((z3, z2));
            }

        double speedScale = vPeak / v;      // 삼각 프로파일이면 < 1
        double n1Peak = n1 * speedScale;

        (ServoSpec spec, double ratio, int z3, int z2, double jr, double tl, double tacc, double tdec, double trms, double nm)? best = null;
        int bestIdx = int.MaxValue; double bestScore = double.MaxValue;
        bool anySpeedOk = false;

        foreach (double ratio in ratios)
        {
            foreach (var (z3, z2) in pairs)
            {
                double iTr = z3 == 0 ? 1.0 : (double)z2 / z3;
                double nm = n1Peak * iTr * ratio;                  // 모터 최고 회전수
                if (nm > nMax) continue;
                anySpeedOk = true;
                double jl = ls.J / (iTr * iTr * ratio * ratio);   // 모터축 환산 부하 관성
                double tl = T1 / (iTr * ratio * etaTotal);        // 모터축 부하 토크
                double omega = 2 * Math.PI * nm / 60.0;
                for (int k = 0; k < StandardData.Servos.Length; k++)
                {
                    var s = StandardData.Servos[k];
                    double jr = jl / s.RotorInertia;
                    if (jr > i.ServoMaxInertiaRatio && !i.ServoInertiaAsWarning) continue;
                    double tRated = s.RatedTorque(i.ServoRatedRpm);
                    double jt = s.RotorInertia + jl;
                    double tacc = jt * omega / ta + tl;
                    double tdec = Math.Abs(jt * omega / td - tl);
                    double trms = continuous
                        ? tl
                        : Math.Sqrt((tacc * tacc * ta + tl * tl * tc + tdec * tdec * td) / tCycle);
                    if (Math.Max(tacc, tdec) > tRated * i.ServoMaxTorqueRatio) continue;
                    if (trms > tRated * i.ServoTorqueUtilization) continue;
                    // 점수: 작은 용량 우선, 같은 용량이면 관성비 작은 쪽, 그 다음 감속비 작은 쪽
                    double score = k * 1000 + jr * 10 + ratio * 0.01;
                    if (k < bestIdx || (k == bestIdx && score < bestScore))
                    {
                        bestIdx = k; bestScore = score;
                        best = (s, ratio, z3, z2, jr, tl, tacc, tdec, trms, nm);
                    }
                    break; // 이 조합에서는 가장 작은 서보 하나만
                }
            }
        }

        if (best == null)
        {
            if (!anySpeedOk)
                throw new InvalidOperationException($"모든 감속비에서 모터 회전수가 최고 사용 회전수({nMax:0} rpm)를 초과합니다. 구동 풀리/스프라켓 지름을 키우거나 정격 회전수를 올리십시오.");
            throw new InvalidOperationException("조건(관성비·토크)을 만족하는 서보 용량이 없습니다. 감속비 범위 확대, 가감속 시간 연장, 허용 관성비 상향을 검토하십시오.");
        }

        var b = best.Value;
        double iTrSel = b.z3 == 0 ? 1.0 : (double)b.z2 / b.z3;
        double tRatedSel = b.spec.RatedTorque(i.ServoRatedRpm);
        double nOut = b.nm / b.ratio;
        double tOutReq = T1 / (iTrSel * etaTr);
        double jlSel = ls.J / (iTrSel * iTrSel * b.ratio * b.ratio);
        double vAct = vPeak * 60.0;
        double omegaSel = 2 * Math.PI * b.nm / 60.0;

        log.AppendLine();
        log.AppendLine("[4] 감속비 / 스프라켓 (서보: 관성비·회전수 조건으로 선정)");
        if (chainDrive) log.AppendLine($"   전동 스프라켓 z3 = {b.z3} T, z2 = {b.z2} T → 전동비 i2 = {iTrSel:0.###}");
        else log.AppendLine("   직결 (전동비 1.0)");
        log.AppendLine($"   감속비 i1 = {b.ratio}{(b.ratio == 1 ? " (감속기 없음)" : "")}, 모터 회전수 = n1·i2·i1 = {n1Peak:0.##}·{iTrSel:0.###}·{b.ratio} = {b.nm:0.#} rpm ≤ {nMax:0} rpm");
        log.AppendLine($"   모터축 환산 부하 관성 JL = J / (i2²·i1²) = {ls.J:0.####} / ({iTrSel:0.###}²·{b.ratio}²) = {jlSel:0.######} kg·m²");
        log.AppendLine($"   관성비 = JL / JM = {jlSel:0.######} / {b.spec.RotorInertia:0.######} = {b.jr:0.##}  (허용 {i.ServoMaxInertiaRatio}) → {(b.jr <= i.ServoMaxInertiaRatio ? "OK" : "초과 — 경고만 (선정 조건에서 제외됨)")}");
        log.AppendLine($"   모터축 부하 토크 TL = T1 / (i2·i1·η) = {T1:0.##} / ({iTrSel:0.###}·{b.ratio}·{etaTotal:0.###}) = {b.tl:0.###} N·m");
        log.AppendLine($"   가속 토크 TA = (JM+JL)·ω/ta + TL = ({b.spec.RotorInertia:0.######}+{jlSel:0.######})·{omegaSel:0.#}/{ta} + {b.tl:0.###} = {b.tacc:0.###} N·m");
        log.AppendLine($"   감속 토크 TD = |(JM+JL)·ω/td − TL| = {b.tdec:0.###} N·m");
        if (continuous) log.AppendLine($"   실효 토크 (연속 운전) = TL = {b.trms:0.###} N·m");
        else log.AppendLine($"   실효 토크 Trms = √[(TA²·ta + TL²·tc + TD²·td) / t사이클] = {b.trms:0.###} N·m");
        log.AppendLine($"   → 서보 {b.spec.Name}: 정격토크 {tRatedSel:0.###} N·m, 최대토크 {tRatedSel * i.ServoMaxTorqueRatio:0.##} N·m");
        log.AppendLine($"      실효/정격 = {b.trms / tRatedSel * 100:0.#} % (≤ {i.ServoTorqueUtilization * 100:0} %),  최대/최대허용 = {Math.Max(b.tacc, b.tdec) / (tRatedSel * i.ServoMaxTorqueRatio) * 100:0.#} %");
        log.AppendLine($"   감속기 출력축 필요 토크 (정상) = T1 / (i2·η전동) = {tOutReq:0.##} N·m,  가속 시 ≈ {b.tacc * b.ratio * i.ReducerEfficiency:0.##} N·m");

        r.Add(G_MOTOR, "모터 종류", "AC 서보모터");
        r.Add(G_MOTOR, "선정 서보 용량", b.spec.Name, "", $"정격 {i.ServoRatedRpm} rpm, 로터 관성 {b.spec.RotorInertia * 1e4:0.###}×10⁻⁴ kg·m² (대표값)");
        r.Add(G_MOTOR, "정격 토크 / 최대 토크", $"{tRatedSel:0.##} / {tRatedSel * i.ServoMaxTorqueRatio:0.##}", "N·m");
        r.Add(G_MOTOR, "운전 시 모터 회전수", b.nm, "rpm", "0", $"≤ {nMax:0} rpm (정격×{i.ServoSpeedUtilization})");
        r.Add(G_MOTOR, "부하 토크 TL (모터축)", b.tl, "N·m", "0.###", $"정격 대비 {b.tl / tRatedSel * 100:0.#} %");
        r.Add(G_MOTOR, "가속 토크 TA", b.tacc, "N·m", "0.###", $"최대토크 대비 {b.tacc / (tRatedSel * i.ServoMaxTorqueRatio) * 100:0.#} %");
        r.Add(G_MOTOR, "감속 토크 TD", b.tdec, "N·m", "0.###");
        r.Add(G_MOTOR, "실효 토크 Trms", b.trms, "N·m", "0.###", $"정격 대비 {b.trms / tRatedSel * 100:0.#} % (허용 {i.ServoTorqueUtilization * 100:0} %)");
        r.Add(G_MOTOR, "관성비 JL/JM", b.jr, "", "0.##", b.jr <= i.ServoMaxInertiaRatio ? $"허용 ≤ {i.ServoMaxInertiaRatio} → OK" : $"허용 {i.ServoMaxInertiaRatio} 초과 — 게인 낮게, 가감속 완만하게 운전");
        if (b.jr > i.ServoMaxInertiaRatio)
            r.Warnings.Add($"관성비 {b.jr:0.#}가 허용값 {i.ServoMaxInertiaRatio}를 초과합니다(경고만 설정). 저속·완만한 가감속 조건에서만 사용하고, 응답성이 필요하면 감속비를 올리거나 중관성 모터를 검토하십시오.");
        if (!continuous)
            r.Add(G_MOTOR, "운전 사이클", $"{ta}+{tc:0.##}+{td}+{tdw}", "s", triangular ? $"삼각 프로파일 — 최고속도 {vAct:0.##} m/min 로 제한" : $"이동거리 {i.MoveDistance} mm");
        if (triangular) r.Warnings.Add($"이동거리가 짧아 지정 속도({i.Speed} m/min)에 도달하지 못합니다. 실제 최고속도 {vAct:0.##} m/min 로 계산했습니다.");
        if (b.jr > 5 && b.jr <= i.ServoMaxInertiaRatio) r.Warnings.Add($"관성비 {b.jr:0.#} — 5를 넘으면 게인 조정 여유가 줄어듭니다. 감속비 상향 또는 중관성 모터를 검토하십시오.");

        r.Add(G_REDUCER, "감속비 i1", b.ratio == 1 ? "직결 (감속기 없음)" : $"1/{b.ratio:0.#}", "", "서보용 유성감속기 등");
        r.Add(G_REDUCER, "출력 회전수", nOut, "rpm");
        r.Add(G_REDUCER, "출력축 필요 토크 (정상)", tOutReq, "N·m", "0.##");
        r.Add(G_REDUCER, "출력축 토크 (가속 시)", b.tacc * b.ratio * i.ReducerEfficiency, "N·m", "0.##", "감속기 순시 허용토크와 비교");

        return new DriveSel
        {
            MotorKw = b.spec.Kw, MotorRpm = b.nm, MotorRatedTorque = tRatedSel, Ratio = b.ratio, Z3 = b.z3, Z2 = b.z2, ITr = iTrSel,
            NOut = nOut, TOutReq = tOutReq, VAct = vAct, SpeedErr = (vAct - i.Speed) / i.Speed * 100.0
        };
    }

    // ══════════════════ 전동 체인 ══════════════════

    private static void TransmissionChainStage(ConveyorInput i, DriveSel d, SelectionResult r, StringBuilder log)
    {
        double k1 = StandardData.ChainUsageFactor(i.LoadClass);
        // 설계동력: 모터 용량 기준 (서보는 가속 시 토크 기준 동력과 비교하여 큰 쪽)
        double pDesign = d.MotorKw * k1;
        if (i.MotorKind == MotorKind.Servo)
            pDesign = Math.Max(pDesign, d.TOutReq * d.NOut / 9550.0 * k1);

        ChainSpec? sel = null; double rating = 0;
        foreach (var c in StandardData.Chains)
        {
            rating = StandardData.ChainRatingKw(c, d.NOut, d.Z3);
            if (rating >= pDesign) { sel = c; break; }
        }
        sel ??= StandardData.Chains[^1];

        double dp3 = sel.PitchDiameter(d.Z3);
        double dp2 = sel.PitchDiameter(d.Z2);
        double chainForce = 2000.0 * d.TOutReq / dp3;
        double C = i.TransmissionCenterDistance;
        double links = (d.Z2 + d.Z3) / 2.0 + 2.0 * C / sel.Pitch + Math.Pow((d.Z2 - d.Z3) / (2 * Math.PI), 2) * sel.Pitch / C;
        int linksEven = (int)Math.Ceiling(links);
        if (linksEven % 2 == 1) linksEven++;
        double cActual = ActualCenterDistance(linksEven, d.Z2, d.Z3, sel.Pitch);
        double chainSf = sel.TensileKN * 1000.0 / chainForce;
        double k2 = StandardData.OhlK2(i.OhlPosition);
        double ohl = chainForce * StandardData.OhlK1(i.Transmission) * k2;

        log.AppendLine();
        log.AppendLine("[5] 전동 체인 / 스프라켓");
        log.AppendLine($"   설계동력 = P × K1(사용계수 {k1}) = {pDesign:0.###} kW");
        log.AppendLine($"   소스프라켓 {d.Z3}T @ {d.NOut:0.##} rpm → {sel.No} 정격 {rating:0.###} kW ≥ {pDesign:0.###} kW");
        log.AppendLine($"   피치원경 Dp = p / sin(180°/z):  z3 → {dp3:0.##} mm,  z2 → {dp2:0.##} mm");
        log.AppendLine($"   체인 장력 F = 2000·T출력 / Dp3 = 2000·{d.TOutReq:0.##} / {dp3:0.##} = {chainForce:0.#} N  (인장강도 대비 안전율 {chainSf:0.#})");
        log.AppendLine($"   링크수 L = (z2+z3)/2 + 2C/p + ((z2−z3)/2π)²·p/C = {links:0.##} → {linksEven} 링크 (짝수),  실제 축간거리 {cActual:0.#} mm");
        log.AppendLine($"   OHL = F × K1 × K2 = {chainForce:0.#} × 1.0 × {k2} = {ohl:0.#} N  ({OhlName(i.OhlPosition)})");

        r.TransmissionChain = sel.No;
        r.TransmissionChainLinks = linksEven;
        r.Add(G_SPROCKET, "전동 체인", sel.No, "", $"피치 {sel.Pitch} mm, 정격 {rating:0.##} kW (설계동력 {pDesign:0.##} kW)");
        r.Add(G_SPROCKET, "스프라켓 피치원경 Dp3 / Dp2", $"{dp3:0.#} / {dp2:0.#}", "mm");
        r.Add(G_SPROCKET, "체인 링크수", linksEven, "링크", "0", $"축간거리 {cActual:0.#} mm (입력 {C} mm)");
        r.Add(G_SPROCKET, "체인 장력", chainForce, "N", "0.#", $"안전율 {chainSf:0.#} (권장 ≥ 7)");
        if (chainSf < 7) r.Warnings.Add($"전동 체인 안전율 {chainSf:0.#} < 7. 체인 호수 상향 또는 스프라켓 대경화 검토.");
        if (d.Z3 < 15) r.Warnings.Add("감속기측 스프라켓 잇수가 15T 미만입니다. 현운동(속도 변동)과 마모 증가에 주의.");
        r.Add(G_CHECK, "감속기 출력축 OHL", ohl, "N", "0.#", $"K2={k2}, 카탈로그 허용 OHL 과 비교");
    }

    // ══════════════════ 유도전동기 관성·기동 검토 ══════════════════

    private static void InductionStartupCheck(ConveyorInput i, LoadStage ls, DriveSel d, SelectionResult r, StringBuilder log)
    {
        bool chainDrive = i.Transmission == Transmission.ChainDrive;
        double cFactor = StandardData.InertiaFactor(i.Transmission, i.StartsPerDay);
        double jOut = ls.J / (d.ITr * d.ITr);
        double jMotor = jOut / (d.Ratio * d.Ratio);
        double jMotorCorr = jMotor * cFactor;
        double omegaMotor = 2 * Math.PI * d.MotorRpm / 60.0;
        double tLoadAtMotor = d.TOutReq / (d.Ratio * i.ReducerEfficiency);
        double tAccel = jMotor * omegaMotor / i.AccelTime;
        double tStartReq = tLoadAtMotor + tAccel;
        double startRatio = tStartReq / d.MotorRatedTorque;

        log.AppendLine();
        log.AppendLine("[6] 관성 / 기동 검토");
        log.AppendLine($"   모터축 환산 J = J / (i2²·i1²) = {ls.J:0.####} / ({d.ITr:0.###}²·{d.Ratio}²) = {jMotor:0.######} kg·m²");
        log.AppendLine($"   관성 보정계수 C = {cFactor} (기동 {i.StartsPerDay} 회/일, {(chainDrive ? "유격 있는 체인 연결" : "직결")}) → 보정 J = {jMotorCorr:0.######} kg·m²");
        log.AppendLine($"   기동 시 필요 토크(모터축) = T부하 + J·ω/t가속 = {tLoadAtMotor:0.###} + {jMotor:0.######}·{omegaMotor:0.#}/{i.AccelTime} = {tStartReq:0.###} N·m");
        log.AppendLine($"   모터 정격토크 {d.MotorRatedTorque:0.###} N·m 대비 {startRatio * 100:0.#} %  (유도전동기 기동토크 ≈ 150~200 %)");

        r.Add(G_CHECK, "모터축 환산 관성 (C 보정)", jMotorCorr, "kg·m²", "0.######", $"C = {cFactor}; 모터 허용 부하관성과 비교");
        r.Add(G_CHECK, "기동 토크 비율", startRatio * 100, "%", "0.#", startRatio <= 1.5 ? "OK (≤150%)" : "확인 필요 — 가속시간 연장 또는 용량 상향");
        if (startRatio > 1.5) r.Warnings.Add("기동 시 필요 토크가 모터 정격의 150%를 초과합니다. 인버터 가속시간 연장 또는 모터 용량 상향을 검토하십시오.");
    }

    // ══════════════════ 부하 계산 ══════════════════

    private static LoadStage BeltLoad(ConveyorInput i, SelectionResult r, StringBuilder log)
    {
        double m = i.TotalWorkMass;
        double mB = i.BeltWidth / 1000.0 * (2 * i.Length) * i.BeltMassPerM2;
        double cos = Math.Cos(i.AngleRad), sin = Math.Sin(i.AngleRad);
        double fFric = i.FrictionBelt * StandardData.G * (m + mB) * cos;
        double fLift = StandardData.G * m * sin;
        double fAcc = i.Accumulation ? i.FrictionProduct * StandardData.G * m : 0;
        double fu = fFric + fLift + fAcc;
        double D1 = i.PulleyDiameter;

        log.AppendLine("[1] 부하 — 벨트 컨베어 (" + (i.BeltKind == BeltKind.Modular ? "모듈 플라스틱 벨트, FMS 계산법" : "고무/PVC 벨트") + ")");
        log.AppendLine($"   벨트 질량 mB = b0 × 2L × 단위질량 = {i.BeltWidth / 1000.0:0.###} m × {2 * i.Length} m × {i.BeltMassPerM2} kg/m² = {mB:0.##} kg");
        log.AppendLine($"   마찰 저항 = μT·g·(m+mB)·cosθ = {i.FrictionBelt}·9.81·({m:0.##}+{mB:0.##})·{cos:0.###} = {fFric:0.#} N");
        log.AppendLine($"   경사 저항 = g·m·sinθ = 9.81·{m:0.##}·{sin:0.###} = {fLift:0.#} N");
        if (i.Accumulation) log.AppendLine($"   축적 저항 = μST·g·m = {i.FrictionProduct}·9.81·{m:0.##} = {fAcc:0.#} N");
        log.AppendLine($"   유효장력 FU = {fu:0.#} N");

        r.Add(G_LOAD, "총 적재하중 m", m, "kg");
        r.Add(G_LOAD, "벨트 질량 mB", mB, "kg", "0.##", "상·하 양측");
        r.Add(G_LOAD, "유효장력 FU", fu, "N", "0.#", $"마찰 {fFric:0.#} + 경사 {fLift:0.#}" + (i.Accumulation ? $" + 축적 {fAcc:0.#}" : ""));

        double fw;
        if (i.BeltKind == BeltKind.Modular)
        {
            double c1 = 1.0 + (i.C1StartStop ? 0.2 : 0) + (i.C1Pushing ? 0.2 : 0) + (i.Speed > 30 ? 0.2 : 0) + (i.Angle > 0 ? 0.4 : 0);
            double fb = fu * c1 / i.C2Temperature;
            double c3 = fb / i.BeltWidth;
            fw = fu * c1 + i.PulleyMass * StandardData.G;
            log.AppendLine($"   C1 = 1.0{(i.C1StartStop ? " +0.2(시작-멈춤)" : "")}{(i.C1Pushing ? " +0.2(종동)" : "")}{(i.Speed > 30 ? " +0.2(V>30)" : "")}{(i.Angle > 0 ? " +0.4(상승)" : "")} = {c1:0.#},  C2 = {i.C2Temperature}");
            log.AppendLine($"   벨트 선택장력 FB = FU·C1/C2 = {fu:0.#}·{c1:0.#}/{i.C2Temperature} = {fb:0.#} N");
            log.AppendLine($"   C3 = FB / b0 = {fb:0.#} / {i.BeltWidth} = {c3:0.##} N/mm  ≤ C3max {i.C3Max} N/mm → {(c3 <= i.C3Max ? "OK" : "NG")}");
            log.AppendLine($"   샤프트 로드 FW ≈ FU·C1 + mW·g = {fu:0.#}·{c1:0.#} + {i.PulleyMass}·9.81 = {fw:0.#} N");
            r.Add(G_LOAD, "벨트 선택장력 FB", fb, "N", "0.#", $"C1={c1:0.#}, C2={i.C2Temperature}");
            r.Add(G_LOAD, "폭당 장력 C3", c3, "N/mm", "0.##", $"허용 {i.C3Max} N/mm → {(c3 <= i.C3Max ? "OK" : "NG")}  (C3/C3max = {c3 / i.C3Max * 100:0}%)");
            if (c3 > i.C3Max) r.Warnings.Add("모듈벨트 폭당 장력 C3가 허용값을 초과합니다. 벨트 시리즈 변경 또는 폭 확대 필요.");
        }
        else
        {
            double theta = i.WrapAngle * Math.PI / 180.0;
            double k = Math.Exp(i.FrictionPulley * theta);
            double t1 = fu * k / (k - 1);
            double t2 = t1 - fu;
            double tw = t1 / i.BeltWidth;
            fw = t1 + t2;
            log.AppendLine($"   오일러식: e^(μθ) = e^({i.FrictionPulley}·{theta:0.###}) = {k:0.###}");
            log.AppendLine($"   긴장측 T1 = FU·e^(μθ)/(e^(μθ)−1) = {t1:0.#} N,  이완측 T2 = T1 − FU = {t2:0.#} N");
            log.AppendLine($"   폭당 장력 = T1 / b0 = {t1:0.#} / {i.BeltWidth} = {tw:0.##} N/mm  ≤ 허용 {i.BeltAllowableTension} N/mm → {(tw <= i.BeltAllowableTension ? "OK" : "NG")}");
            log.AppendLine($"   풀리 축하중 FW ≈ T1 + T2 = {fw:0.#} N");
            r.Add(G_LOAD, "긴장측 장력 T1", t1, "N", "0.#");
            r.Add(G_LOAD, "이완측 장력 T2", t2, "N", "0.#", "T2 ≥ 0 이어야 미끄러짐 없음");
            r.Add(G_LOAD, "벨트 폭당 장력", tw, "N/mm", "0.##", $"허용 {i.BeltAllowableTension} N/mm → {(tw <= i.BeltAllowableTension ? "OK" : "NG")}");
            if (tw > i.BeltAllowableTension) r.Warnings.Add("벨트 폭당 장력이 허용값을 초과합니다. 벨트 강도 상향 또는 폭 확대 필요.");
        }
        r.Add(G_LOAD, "구동 풀리 외경 D1", D1, "mm", "0.#", $"표준: {string.Join("/", StandardData.PulleyDiameters.Where(d => d >= D1 * 0.8 && d <= D1 * 1.3))}");
        r.Add(G_LOAD, "구동 풀리 축하중 FW", fw, "N", "0.#", "베어링 선정용");

        double rr = D1 / 2000.0;
        double j = (m + mB) * rr * rr + 2 * 0.5 * i.PulleyMass * rr * rr;
        return new LoadStage
        {
            Fu = fu, D1 = D1, J = j, MovingMass = mB, Mw = i.PulleyMass,
            SpeedBasis = $"풀리 외경 {D1} mm 기준",
            RpmToSpeed = rpm => rpm * Math.PI * D1 / 1000.0
        };
    }

    private static LoadStage RollerLoad(ConveyorInput i, SelectionResult r, StringBuilder log)
    {
        double m = i.TotalWorkMass;
        int nR = (int)Math.Floor(i.Length * 1000.0 / i.RollerPitch) + 1;
        double mR = nR * i.RollerMass;
        double mChain = i.RollerChainMassPerM * 2 * i.Length;
        double cos = Math.Cos(i.AngleRad), sin = Math.Sin(i.AngleRad);
        double D1 = i.RollerDiameter;

        double fRoll = i.FrictionRolling * StandardData.G * m * cos;
        double fBearing = i.FrictionBearing * StandardData.G * (m * cos + mR) * (i.RollerShaftDiameter / D1);
        double fChain = 0.1 * StandardData.G * mChain * 0.5;
        double fLift = StandardData.G * m * sin;
        double fAcc = i.Accumulation ? i.FrictionProduct * StandardData.G * m : 0;
        double fu = fRoll + fBearing + fChain + fLift + fAcc;

        log.AppendLine("[1] 부하 — 롤러 컨베어 (체인 구동 롤러)");
        log.AppendLine($"   롤러 수 = L/피치 + 1 = {i.Length * 1000}/{i.RollerPitch} + 1 = {nR} 개,  롤러 총질량 = {nR} × {i.RollerMass} = {mR:0.##} kg");
        log.AppendLine($"   굴림 저항 = μ1·g·m·cosθ = {i.FrictionRolling}·9.81·{m:0.##}·{cos:0.###} = {fRoll:0.#} N");
        log.AppendLine($"   베어링 저항 = μ2·g·(m·cosθ + mR)·(d축/D1) = {i.FrictionBearing}·9.81·({m * cos:0.##}+{mR:0.##})·({i.RollerShaftDiameter}/{D1}) = {fBearing:0.#} N");
        log.AppendLine($"   구동체인 저항(개략) = 0.1·g·m체인·0.5 = {fChain:0.#} N  (체인 {mChain:0.##} kg)");
        log.AppendLine($"   경사 저항 = g·m·sinθ = {fLift:0.#} N");
        if (i.Accumulation) log.AppendLine($"   축적 저항 = μST·g·m = {i.FrictionProduct}·9.81·{m:0.##} = {fAcc:0.#} N");
        log.AppendLine($"   유효장력 FU = {fu:0.#} N");

        r.Add(G_LOAD, "총 적재하중 m", m, "kg");
        r.Add(G_LOAD, "롤러 수 / 총질량", $"{nR} 개 / {mR:0.#} kg", "");
        r.Add(G_LOAD, "유효장력 FU", fu, "N", "0.#", $"굴림 {fRoll:0.#} + 베어링 {fBearing:0.#} + 체인 {fChain:0.#} + 경사 {fLift:0.#}" + (i.Accumulation ? $" + 축적 {fAcc:0.#}" : ""));
        r.Add(G_LOAD, "롤러 외경 D1", D1, "mm", "0.#", $"롤러 회전수 {i.Speed * 1000 / (Math.PI * D1):0.#} rpm");
        if (nR > 40) r.Warnings.Add($"롤러 {nR}개를 체인 1계통으로 구동하면 전동 손실이 큽니다. 구동 구간 분할을 검토하십시오.");

        double rr = D1 / 2000.0;
        double j = m * rr * rr + 0.8 * mR * rr * rr + mChain * rr * rr;
        return new LoadStage
        {
            Fu = fu, D1 = D1, J = j, MovingMass = mR + mChain, Mw = i.RollerMass,
            SpeedBasis = $"롤러 외경 {D1} mm 기준",
            RpmToSpeed = rpm => rpm * Math.PI * D1 / 1000.0
        };
    }

    private static LoadStage ChainLoad(ConveyorInput i, SelectionResult r, StringBuilder log)
    {
        double m = i.TotalWorkMass;
        double mC = i.ChainMassPerM * i.ChainStrands * 2 * i.Length;
        double cos = Math.Cos(i.AngleRad), sin = Math.Sin(i.AngleRad);
        double fFric = i.FrictionChain * StandardData.G * (m + mC) * cos;
        double fLift = StandardData.G * m * sin;
        double fAcc = i.Accumulation ? i.FrictionProduct * StandardData.G * m : 0;
        double fu = fFric + fLift + fAcc;
        double D1 = i.ChainPitch / Math.Sin(Math.PI / i.ConveyorSprocketTeeth);

        var spec = StandardData.FindChainByPitch(i.ChainPitch);
        double allowKN = i.ChainAllowableLoad > 0 ? i.ChainAllowableLoad : (spec?.AllowableKN ?? 0);
        double tensileKN = spec?.TensileKN ?? 0;
        double fStrand = fu / i.ChainStrands * 1.1;

        log.AppendLine("[1] 부하 — 체인 컨베어");
        log.AppendLine($"   체인 질량 mC = 단위질량 × 열수 × 2L = {i.ChainMassPerM} × {i.ChainStrands} × {2 * i.Length} = {mC:0.##} kg");
        log.AppendLine($"   마찰 저항 = μ·g·(m+mC)·cosθ = {i.FrictionChain}·9.81·({m:0.##}+{mC:0.##})·{cos:0.###} = {fFric:0.#} N");
        log.AppendLine($"   경사 저항 = g·m·sinθ = {fLift:0.#} N");
        if (i.Accumulation) log.AppendLine($"   축적 저항 = μST·g·m = {fAcc:0.#} N");
        log.AppendLine($"   유효장력 FU = {fu:0.#} N");
        log.AppendLine($"   스프라켓 PCD D1 = p / sin(180°/z) = {i.ChainPitch} / sin(180°/{i.ConveyorSprocketTeeth}) = {D1:0.##} mm");
        log.AppendLine($"   열당 체인 장력 = FU / 열수 × 1.1 = {fu:0.#} / {i.ChainStrands} × 1.1 = {fStrand:0.#} N");
        if (allowKN > 0)
            log.AppendLine($"   허용하중 {allowKN} kN ({spec?.No ?? "입력값"}) → {(fStrand <= allowKN * 1000 ? "OK" : "NG")}" +
                           (tensileKN > 0 ? $",  인장강도 대비 안전율 {tensileKN * 1000 / fStrand:0.#}" : ""));

        r.Add(G_LOAD, "총 적재하중 m", m, "kg");
        r.Add(G_LOAD, "체인 질량 mC", mC, "kg", "0.##", $"{i.ChainStrands}열, 상·하 양측");
        r.Add(G_LOAD, "유효장력 FU", fu, "N", "0.#", $"마찰 {fFric:0.#} + 경사 {fLift:0.#}" + (i.Accumulation ? $" + 축적 {fAcc:0.#}" : ""));
        r.Add(G_LOAD, "컨베어 스프라켓 PCD D1", D1, "mm", "0.##", $"피치 {i.ChainPitch} mm × {i.ConveyorSprocketTeeth}T");
        r.Add(G_LOAD, "열당 체인 장력", fStrand, "N", "0.#",
            allowKN > 0 ? $"허용 {allowKN} kN ({spec?.No ?? "입력"}) → {(fStrand <= allowKN * 1000 ? "OK" : "NG")}" : "허용하중 미입력 — 카탈로그 확인");
        if (allowKN > 0 && fStrand > allowKN * 1000) r.Warnings.Add("컨베어 체인 장력이 허용하중을 초과합니다. 체인 호수 상향 또는 열수 추가 필요.");
        if (tensileKN > 0 && tensileKN * 1000 / fStrand < 7) r.Warnings.Add("컨베어 체인 인장강도 대비 안전율이 7 미만입니다.");

        double rr = D1 / 2000.0;
        double j = (m + mC) * rr * rr + 2 * i.ChainStrands * 0.5 * i.ConveyorSprocketMass * rr * rr;
        return new LoadStage
        {
            Fu = fu, D1 = D1, J = j, MovingMass = mC, Mw = i.ConveyorSprocketMass * i.ChainStrands,
            SpeedBasis = $"피치 {i.ChainPitch} × {i.ConveyorSprocketTeeth}T 기준",
            RpmToSpeed = rpm => rpm * i.ChainPitch * i.ConveyorSprocketTeeth / 1000.0
        };
    }

    // ══════════════════ 보조 ══════════════════

    private static (double ratio, int z3, int z2) BestRatioAndTeeth(double nMotor, double n1Target)
    {
        double best = double.MaxValue;
        (double, int, int) sel = (StandardData.ReducerRatios[0], 15, 15);
        foreach (double ratio in StandardData.ReducerRatios)
        {
            double nOut = nMotor / ratio;
            foreach (int z3 in StandardData.SprocketTeeth)
            {
                if (z3 < 13 || z3 > 25) continue;
                foreach (int z2 in StandardData.SprocketTeeth)
                {
                    double iTr = (double)z2 / z3;
                    if (iTr < 1.0 || iTr > 4.0) continue;
                    double n1 = nOut / iTr;
                    double err = Math.Abs(n1 - n1Target) / n1Target;
                    double score = err + iTr * 0.0005 - z3 * 0.00001;
                    if (score < best) { best = score; sel = (ratio, z3, z2); }
                }
            }
        }
        return sel;
    }

    private static (int z3, int z2) BestTeethPair(double needTr)
    {
        double best = double.MaxValue; (int, int) sel = (15, 15);
        foreach (int z3 in StandardData.SprocketTeeth)
        {
            if (z3 < 13 || z3 > 25) continue;
            foreach (int z2 in StandardData.SprocketTeeth)
            {
                double err = Math.Abs((double)z2 / z3 - needTr) / needTr - z3 * 0.00001;
                if (err < best) { best = err; sel = (z3, z2); }
            }
        }
        return sel;
    }

    private static double ActualCenterDistance(int links, int z2, int z3, double p)
    {
        double a = links - (z2 + z3) / 2.0;
        double b = Math.Pow((z2 - z3) / (2 * Math.PI), 2);
        return p / 4.0 * (a + Math.Sqrt(a * a - 8 * b));
    }

    private static void Validate(ConveyorInput i)
    {
        if (i.Speed <= 0) throw new ArgumentException("반송속도는 0보다 커야 합니다.");
        if (i.Length <= 0) throw new ArgumentException("컨베어 길이는 0보다 커야 합니다.");
        if (i.WorkCount < 0 || i.WorkMass < 0) throw new ArgumentException("워크 질량/개수는 음수일 수 없습니다.");
        if (Math.Abs(i.Angle) >= 90) throw new ArgumentException("경사각은 ±90° 미만이어야 합니다.");
        if (i.ReducerEfficiency <= 0 || i.ReducerEfficiency > 1) throw new ArgumentException("감속기 효율은 0~1 사이여야 합니다.");
        if (i.AccelTime <= 0) throw new ArgumentException("가속 시간은 0보다 커야 합니다.");
        if (i.MotorKind == MotorKind.Servo)
        {
            if (i.DecelTime <= 0) throw new ArgumentException("감속 시간은 0보다 커야 합니다.");
            if (i.DwellTime < 0 || i.MoveDistance < 0) throw new ArgumentException("정지 시간/이동거리는 음수일 수 없습니다.");
            if (i.ServoRatedRpm <= 0 || i.ServoMaxTorqueRatio < 1 || i.ServoMaxInertiaRatio <= 0) throw new ArgumentException("서보 조건(정격 회전수, 최대토크 배율, 허용 관성비)을 확인하십시오.");
            if (i.ServoTorqueUtilization <= 0 || i.ServoTorqueUtilization > 1 || i.ServoSpeedUtilization <= 0 || i.ServoSpeedUtilization > 1)
                throw new ArgumentException("서보 토크/속도 사용률은 0~1 사이여야 합니다.");
        }
        switch (i.Type)
        {
            case ConveyorType.Belt:
                if (i.PulleyDiameter <= 0 || i.BeltWidth <= 0) throw new ArgumentException("풀리 외경/벨트 폭을 확인하십시오.");
                if (i.BeltKind == BeltKind.Modular && i.C2Temperature <= 0) throw new ArgumentException("온도계수 C2는 0보다 커야 합니다.");
                break;
            case ConveyorType.Roller:
                if (i.RollerDiameter <= 0 || i.RollerPitch <= 0) throw new ArgumentException("롤러 외경/피치를 확인하십시오.");
                break;
            case ConveyorType.Chain:
                if (i.ChainPitch <= 0 || i.ConveyorSprocketTeeth < 6 || i.ChainStrands < 1) throw new ArgumentException("체인 피치/스프라켓 잇수/열수를 확인하십시오.");
                break;
        }
        if (i.Transmission == Transmission.ChainDrive && !i.AutoSelectSprockets)
        {
            if (i.DriveSprocketTeeth < 6 || i.DrivenSprocketTeeth < 6) throw new ArgumentException("전동 스프라켓 잇수를 확인하십시오.");
        }
        if (i.Transmission == Transmission.ChainDrive && i.TransmissionCenterDistance <= 0)
            throw new ArgumentException("전동 체인 축간거리를 확인하십시오.");
    }

    public static string TypeName(ConveyorInput i) => i.Type switch
    {
        ConveyorType.Belt => i.BeltKind == BeltKind.Modular ? "벨트 컨베어 (모듈벨트)" : "벨트 컨베어",
        ConveyorType.Roller => "롤러 컨베어",
        _ => "체인 컨베어"
    };

    private static string LoadName(LoadClass c) => c switch
    {
        LoadClass.Uniform => "균일 부하",
        LoadClass.LightShock => "가벼운 충격 부하",
        _ => "무거운 충격 부하"
    };

    private static string HoursName(DailyHours h) => h switch
    {
        DailyHours.Under3 => "3시간 이하/일",
        DailyHours.H3to10 => "3~10시간/일",
        _ => "10시간 이상/일"
    };

    private static string OhlName(OhlPosition p) => p switch
    {
        OhlPosition.ShaftRoot => "축의 밑부분",
        OhlPosition.ShaftEnd => "축단",
        _ => "축의 중앙"
    };
}
