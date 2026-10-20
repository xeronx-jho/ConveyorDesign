namespace ConveyorDesign.Data;

/// <summary>롤러체인 규격 (KS B 1407 / ANSI). 전동용 정격은 소(小)스프라켓 17T, 1열 기준의 근사치.</summary>
public sealed record ChainSpec(
    string No,            // RS40 등
    double Pitch,         // [mm]
    double RollerDia,     // [mm]
    double TensileKN,     // 평균 인장강도 [kN]
    double AllowableKN,   // 최대 허용 하중 [kN]
    double MassKgPerM,    // 단위질량 [kg/m]
    double[] KwAtRpm)     // StandardData.RatingRpm 에 대응하는 정격 전달동력 [kW]
{
    public double PitchDiameter(int teeth) => Pitch / Math.Sin(Math.PI / teeth);
}

/// <summary>AC 서보모터 용량별 대표값 (저관성 3000 rpm 계열 근사치). 정격토크는 정격 회전수로부터 계산.</summary>
public sealed record ServoSpec(double Kw, double RotorInertia /* [kg·m²] */)
{
    public double RatedTorque(double ratedRpm) => 9550.0 * Kw / ratedRpm;
    public string Name => Kw < 1 ? $"{Kw * 1000:0} W" : $"{Kw:0.#} kW";
}

/// <summary>
/// 표준 규격 데이터. 제조사 카탈로그 값이 아니므로 최종 선정 시 반드시 카탈로그로 확인할 것.
/// </summary>
public static class StandardData
{
    public const double G = 9.81;

    // ─── 모터 표준 출력 [kW] (KS C 4202 3상 유도전동기) ───
    public static readonly double[] MotorKw =
        { 0.1, 0.2, 0.4, 0.75, 1.5, 2.2, 3.7, 5.5, 7.5, 11, 15, 18.5, 22, 30, 37, 45, 55 };

    /// <summary>극수·주파수에 따른 정격(전부하) 회전수 근사 [rpm]</summary>
    public static double MotorRatedRpm(int poles, double hz)
    {
        double sync = 120.0 * hz / poles;
        double slip = poles switch { 2 => 0.04, 4 => 0.045, 6 => 0.05, _ => 0.06 };
        return Math.Round(sync * (1 - slip));
    }

    // ─── 기어모터/감속기 표준 감속비 ───
    public static readonly double[] ReducerRatios =
        { 3, 5, 7.5, 10, 12.5, 15, 20, 25, 30, 40, 50, 60, 80, 100, 120, 150, 200, 240, 300, 360, 450, 600, 900, 1200 };

    // ─── 서보용 감속기(유성 등) 표준 감속비. 1 = 직결 ───
    public static readonly double[] ServoReducerRatios =
        { 1, 3, 4, 5, 7, 9, 10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 100 };

    // ─── 서보모터 용량표 (로터 관성은 저관성 계열 대표값 — 카탈로그로 확인) ───
    public static readonly ServoSpec[] Servos =
    {
        new(0.05, 0.025e-4),
        new(0.1,  0.05e-4),
        new(0.2,  0.22e-4),
        new(0.4,  0.37e-4),
        new(0.75, 1.3e-4),
        new(1.0,  2.0e-4),
        new(1.5,  3.0e-4),
        new(2.0,  4.0e-4),
        new(3.0,  6.0e-4),
        new(5.0,  10e-4),
        new(7.0,  15e-4),
        new(11.0, 30e-4),
        new(15.0, 45e-4),
    };

    // ─── 스프라켓 표준 잇수 ───
    public static readonly int[] SprocketTeeth =
        { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 30, 32, 35, 38, 40, 45, 48, 50, 60 };

    // ─── 풀리 표준 외경 [mm] ───
    public static readonly double[] PulleyDiameters =
        { 30, 40, 50, 60, 76, 89, 100, 114, 125, 139, 150, 165, 200, 250, 315, 400, 500, 630 };

    // ─── 롤러 표준 외경 [mm] ───
    public static readonly double[] RollerDiameters = { 38.1, 42.7, 48.6, 50.8, 57, 60.5, 76.3, 89.1, 101.6 };

    // ─── 롤러체인 정격 전달동력 표의 소스프라켓 회전수 열 [rpm] ───
    public static readonly double[] RatingRpm = { 10, 25, 50, 100, 200, 300, 500, 700, 1000, 1400 };

    /// <summary>
    /// 롤러체인 규격. 정격 kW는 17T 소스프라켓·1열 기준 근사치(일본 규격 계열 카탈로그 참고).
    /// </summary>
    public static readonly ChainSpec[] Chains =
    {
        new("RS25",  6.35,  3.30,   3.5,   0.44, 0.15, new[]{0.012,0.03,0.055,0.10,0.19,0.27,0.43,0.57,0.78,1.0}),
        new("RS35",  9.525, 5.08,   7.8,   1.07, 0.33, new[]{0.03,0.07,0.13,0.25,0.47,0.67,1.07,1.44,1.95,2.5}),
        new("RS40", 12.70,  7.92,  17.7,   3.0,  0.64, new[]{0.06,0.14,0.26,0.49,0.92,1.32,2.1,2.85,3.9,5.1}),
        new("RS50", 15.875,10.16,  29.4,   5.1,  1.04, new[]{0.12,0.27,0.50,0.95,1.78,2.57,4.1,5.5,7.5,9.8}),
        new("RS60", 19.05, 11.91,  44.1,   7.5,  1.53, new[]{0.20,0.46,0.86,1.63,3.07,4.4,7.0,9.5,12.9,16.5}),
        new("RS80", 25.40, 15.88,  78.5,  13.6,  2.66, new[]{0.46,1.05,1.95,3.7,6.9,10.0,15.9,21.3,28.5,35.0}),
        new("RS100",31.75, 19.05, 118.0,  21.1,  3.99, new[]{0.87,2.0,3.7,7.0,13.2,19.0,30.0,40.0,51.0,58.0}),
        new("RS120",38.10, 22.23, 167.0,  30.0,  5.90, new[]{1.5,3.4,6.3,12.0,22.5,32.5,51.0,67.0,80.0,85.0}),
        new("RS140",44.45, 25.40, 226.0,  40.0,  7.84, new[]{2.3,5.3,9.9,18.8,35.0,50.5,78.0,100.0,110.0,110.0}),
        new("RS160",50.80, 28.58, 294.0,  53.0, 10.20, new[]{3.4,7.9,14.7,27.8,52.0,75.0,113.0,135.0,140.0,140.0}),
        new("RS200",63.50, 39.68, 461.0,  83.0, 16.30, new[]{6.3,14.5,27.0,51.0,95.0,135.0,190.0,210.0,210.0,210.0}),
    };

    public static ChainSpec? FindChain(string no) => Chains.FirstOrDefault(c => c.No == no);

    /// <summary>컨베어 체인 피치로 가장 가까운 규격을 찾는다 (더블피치 체인은 기본 RS 체인의 2배 피치).</summary>
    public static ChainSpec? FindChainByPitch(double pitch)
    {
        var exact = Chains.FirstOrDefault(c => Math.Abs(c.Pitch - pitch) < 0.05);
        if (exact != null) return exact;
        // 더블피치(C2040=25.4, C2050=31.75, C2060=38.1, C2080=50.8): 기본 체인 강도와 동일
        var half = Chains.FirstOrDefault(c => Math.Abs(c.Pitch * 2 - pitch) < 0.05);
        if (half != null) return half with { No = half.No.Replace("RS", "C2") + "0(더블피치)", Pitch = pitch, MassKgPerM = half.MassKgPerM * 0.8 };
        return null;
    }

    /// <summary>소스프라켓 회전수에서의 정격 전달동력 (log-log 보간)</summary>
    public static double ChainRatingKw(ChainSpec chain, double rpm, int smallTeeth)
    {
        if (rpm <= 0) return 0;
        double[] x = RatingRpm;
        double[] y = chain.KwAtRpm;
        double kw;
        if (rpm <= x[0]) kw = y[0] * rpm / x[0];
        else if (rpm >= x[^1]) kw = y[^1];
        else
        {
            int i = 0;
            while (rpm > x[i + 1]) i++;
            double lx0 = Math.Log(x[i]), lx1 = Math.Log(x[i + 1]);
            double ly0 = Math.Log(y[i]), ly1 = Math.Log(y[i + 1]);
            kw = Math.Exp(ly0 + (ly1 - ly0) * (Math.Log(rpm) - lx0) / (lx1 - lx0));
        }
        // 잇수 보정 (17T 기준) : 근사식 (z/17)^0.9
        kw *= Math.Pow(smallTeeth / 17.0, 0.9);
        return kw;
    }

    // ─── 서비스 팩터 (닛세이 기준: 부하 상태 × 일일 운전시간) ───
    public static double ServiceFactor(Models.LoadClass load, Models.DailyHours hours) => (load, hours) switch
    {
        (Models.LoadClass.Uniform, _) => 1.0,
        (Models.LoadClass.LightShock, Models.DailyHours.Over10) => 1.25,
        (Models.LoadClass.LightShock, _) => 1.0,
        (Models.LoadClass.HeavyShock, Models.DailyHours.Under3) => 1.0,
        (Models.LoadClass.HeavyShock, Models.DailyHours.H3to10) => 1.25,
        (Models.LoadClass.HeavyShock, Models.DailyHours.Over10) => 1.5,
        _ => 1.0
    };

    // ─── 전동 체인 사용계수 K1 (구동기: 전동기, 피동기 충격 정도) ───
    public static double ChainUsageFactor(Models.LoadClass load) => load switch
    {
        Models.LoadClass.Uniform => 1.0,
        Models.LoadClass.LightShock => 1.3,
        _ => 1.5
    };

    // ─── 관성 모멘트 보정계수 C (닛세이) ───
    public static double InertiaFactor(Models.Transmission tr, int startsPerDay) => (tr, startsPerDay > 70) switch
    {
        (Models.Transmission.Direct, false) => 1.0,
        (Models.Transmission.Direct, true) => 1.5,
        (Models.Transmission.ChainDrive, false) => 2.0,
        _ => 3.0
    };

    // ─── OHL 연결방식 계수 K1 / 하중위치 계수 K2 (닛세이) ───
    public static double OhlK1(Models.Transmission tr) => 1.0; // 체인·타이밍벨트 1.0 (기어 1.25, V벨트 1.5 — 현재 체인 전동만 지원)
    public static double OhlK2(Models.OhlPosition p) => p switch
    {
        Models.OhlPosition.ShaftRoot => 0.75,
        Models.OhlPosition.ShaftEnd => 1.5,
        _ => 1.0
    };

    /// <summary>값 이상인 표준값 중 최소값. 없으면 최대값.</summary>
    public static double CeilToStandard(double value, double[] table)
    {
        foreach (var v in table) if (v >= value - 1e-9) return v;
        return table[^1];
    }

    /// <summary>표준값 중 가장 가까운 값</summary>
    public static double NearestStandard(double value, double[] table)
        => table.OrderBy(v => Math.Abs(v - value)).First();
}
