using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using ConveyorDesign.Data;
using ConveyorDesign.Engine;
using ConveyorDesign.Models;
using Microsoft.Win32;

namespace ConveyorDesign.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public MainViewModel()
    {
        Input = new ConveyorInput();
        CalculateCommand = new RelayCommand(Calculate);
        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open);
        SaveCommand = new RelayCommand(Save);
        ExportReportCommand = new RelayCommand(ExportReport);
        CopyLogCommand = new RelayCommand(() => { if (!string.IsNullOrEmpty(Log)) Clipboard.SetText(Log); });
        Calculate();
    }

    // ───── 입력 ─────
    private ConveyorInput _input = null!;
    public ConveyorInput Input
    {
        get => _input;
        private set
        {
            if (_input != null) _input.PropertyChanged -= OnInputChanged;
            Set(ref _input, value);
            HookInput();
            RaiseTypeFlags();
        }
    }

    private void HookInput()
    {
        if (_input != null) _input.PropertyChanged += OnInputChanged;
    }

    private void OnInputChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConveyorInput.Type) or nameof(ConveyorInput.BeltKind)
            or nameof(ConveyorInput.Transmission) or nameof(ConveyorInput.AutoSelectSprockets)
            or nameof(ConveyorInput.MotorKind))
        {
            if (e.PropertyName == nameof(ConveyorInput.Type)) ApplyTypeDefaults();
            RaiseTypeFlags();
        }
        if (AutoCalculate) Calculate();
    }

    private void ApplyTypeDefaults()
    {
        // 종류 변경 시 대표 마찰계수만 기본값으로 안내 (사용자가 수정 가능)
        switch (Input.Type)
        {
            case ConveyorType.Belt: if (Input.FrictionBelt <= 0) Input.FrictionBelt = 0.3; break;
            case ConveyorType.Roller: if (Input.FrictionRolling <= 0) Input.FrictionRolling = 0.05; break;
            case ConveyorType.Chain: if (Input.FrictionChain <= 0) Input.FrictionChain = 0.2; break;
        }
    }

    private void RaiseTypeFlags()
    {
        OnPropertyChanged(nameof(IsBelt));
        OnPropertyChanged(nameof(IsRubberBelt));
        OnPropertyChanged(nameof(IsModularBelt));
        OnPropertyChanged(nameof(IsRoller));
        OnPropertyChanged(nameof(IsChain));
        OnPropertyChanged(nameof(IsChainDrive));
        OnPropertyChanged(nameof(IsManualSprocket));
        OnPropertyChanged(nameof(IsServo));
        OnPropertyChanged(nameof(IsInduction));
    }

    public bool IsServo => Input.MotorKind == MotorKind.Servo;
    public bool IsInduction => Input.MotorKind == MotorKind.Induction;

    public bool IsBelt => Input.Type == ConveyorType.Belt;
    public bool IsRubberBelt => IsBelt && Input.BeltKind == BeltKind.Rubber;
    public bool IsModularBelt => IsBelt && Input.BeltKind == BeltKind.Modular;
    public bool IsRoller => Input.Type == ConveyorType.Roller;
    public bool IsChain => Input.Type == ConveyorType.Chain;
    public bool IsChainDrive => Input.Transmission == Transmission.ChainDrive;
    public bool IsManualSprocket => IsChainDrive && !Input.AutoSelectSprockets;

    private bool _autoCalculate = true;
    public bool AutoCalculate { get => _autoCalculate; set => Set(ref _autoCalculate, value); }

    // ───── 선택지 ─────
    public List<KeyValuePair<ConveyorType, string>> TypeOptions { get; } = new()
    {
        new(ConveyorType.Belt, "벨트 컨베어"),
        new(ConveyorType.Roller, "롤러 컨베어"),
        new(ConveyorType.Chain, "체인 컨베어"),
    };
    public List<KeyValuePair<BeltKind, string>> BeltKindOptions { get; } = new()
    {
        new(BeltKind.Rubber, "고무 / PVC 벨트"),
        new(BeltKind.Modular, "모듈 플라스틱 벨트 (FMS 계산법)"),
    };
    public List<KeyValuePair<MotorKind, string>> MotorKindOptions { get; } = new()
    {
        new(MotorKind.Induction, "3상 유도전동기 (+인버터)"),
        new(MotorKind.Servo, "AC 서보모터"),
    };
    public List<KeyValuePair<Transmission, string>> TransmissionOptions { get; } = new()
    {
        new(Transmission.ChainDrive, "스프라켓·체인 전동"),
        new(Transmission.Direct, "직결 (커플링 / 중공축)"),
    };
    public List<KeyValuePair<LoadClass, string>> LoadClassOptions { get; } = new()
    {
        new(LoadClass.Uniform, "균일 부하"),
        new(LoadClass.LightShock, "가벼운 충격 부하"),
        new(LoadClass.HeavyShock, "무거운 충격 부하"),
    };
    public List<KeyValuePair<DailyHours, string>> DailyHoursOptions { get; } = new()
    {
        new(DailyHours.Under3, "3시간 이하"),
        new(DailyHours.H3to10, "3 ~ 10시간"),
        new(DailyHours.Over10, "10시간 이상"),
    };
    public List<KeyValuePair<OhlPosition, string>> OhlOptions { get; } = new()
    {
        new(OhlPosition.ShaftRoot, "축의 밑부분 (K2 0.75)"),
        new(OhlPosition.ShaftCenter, "축의 중앙 (K2 1.00)"),
        new(OhlPosition.ShaftEnd, "축단 (K2 1.50)"),
    };
    public List<int> PoleOptions { get; } = new() { 2, 4, 6, 8 };
    public List<double> FrequencyOptions { get; } = new() { 50, 60 };
    public List<double> PulleyOptions { get; } = StandardData.PulleyDiameters.ToList();
    public List<double> RollerOptions { get; } = StandardData.RollerDiameters.ToList();
    public List<double> ChainPitchOptions { get; } = new() { 12.7, 15.875, 19.05, 25.4, 31.75, 38.1, 44.45, 50.8, 63.5, 76.2, 101.6 };
    public List<int> TeethOptions { get; } = StandardData.SprocketTeeth.ToList();
    public List<double> RatioOptions { get; } = new double[] { 0 }.Concat(StandardData.ReducerRatios).ToList();

    // ───── 결과 ─────
    private SelectionResult _result = new();
    public SelectionResult Result { get => _result; private set => Set(ref _result, value); }

    private string _log = "";
    public string Log { get => _log; private set => Set(ref _log, value); }

    private string _warnings = "";
    public string Warnings { get => _warnings; private set => Set(ref _warnings, value); }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private string _status = "준비";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string? _currentFile;
    public string Title => "컨베어 구동부 선정" + (_currentFile != null ? " — " + Path.GetFileName(_currentFile) : "");

    // ───── 명령 ─────
    public RelayCommand CalculateCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ExportReportCommand { get; }
    public RelayCommand CopyLogCommand { get; }

    public void Calculate()
    {
        var res = ConveyorCalculator.Calculate(Input);
        Result = res;
        Log = res.CalculationLog;
        Warnings = res.Warnings.Count == 0 ? "" : string.Join(Environment.NewLine, res.Warnings.Select(w => "⚠ " + w));
        Summary = res.Ok ? BuildSummary(res) : "계산 실패";
        Status = res.Ok ? $"계산 완료  {DateTime.Now:HH:mm:ss}" : "오류";
    }

    private string BuildSummary(SelectionResult r)
    {
        var sb = new StringBuilder();
        if (Input.MotorKind == MotorKind.Servo)
        {
            string kw = r.SelectedMotorKw < 1 ? $"{r.SelectedMotorKw * 1000:0} W" : $"{r.SelectedMotorKw:0.#} kW";
            sb.Append($"서보 {kw} ({r.MotorRpm:0} rpm)   |   감속비 {(r.ReducerRatio == 1 ? "직결" : $"1/{r.ReducerRatio:0.#}")} (출력 {r.ReducerOutputRpm:0.#} rpm, {r.ReducerOutputTorque:0.#} N·m)");
        }
        else
            sb.Append($"모터 {r.SelectedMotorKw} kW {Input.MotorPoles}P   |   감속비 1/{r.ReducerRatio:0.#} (출력 {r.ReducerOutputRpm:0.#} rpm, {r.ReducerOutputTorque:0.#} N·m)");
        if (Input.Transmission == Transmission.ChainDrive)
            sb.Append($"   |   스프라켓 {r.DriveSprocketTeeth}T → {r.DrivenSprocketTeeth}T, {r.TransmissionChain} × {r.TransmissionChainLinks}L");
        if (Input.MotorKind == MotorKind.Servo)
            sb.Append($"   |   최고속도 {r.ActualSpeed:0.#} m/min");
        else
            sb.Append($"   |   실속도 {r.ActualSpeed:0.#} m/min ({r.SpeedErrorPercent:+0.0;-0.0} %)");
        return sb.ToString();
    }

    private void New()
    {
        Input = new ConveyorInput();
        _currentFile = null;
        OnPropertyChanged(nameof(Title));
        Calculate();
    }

    private void Open()
    {
        var dlg = new OpenFileDialog { Filter = "컨베어 선정 파일 (*.cvd.json)|*.cvd.json|JSON (*.json)|*.json|모든 파일|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            var input = JsonSerializer.Deserialize<ConveyorInput>(json, JsonOpt);
            if (input == null) throw new InvalidDataException("파일 형식이 올바르지 않습니다.");
            Input = input;
            _currentFile = dlg.FileName;
            OnPropertyChanged(nameof(Title));
            Calculate();
        }
        catch (Exception ex)
        {
            MessageBox.Show("열기 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "컨베어 선정 파일 (*.cvd.json)|*.cvd.json",
            FileName = SafeName(Input.ProjectName) + ".cvd.json"
        };
        if (_currentFile != null) dlg.InitialDirectory = Path.GetDirectoryName(_currentFile);
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(Input, JsonOpt), Encoding.UTF8);
            _currentFile = dlg.FileName;
            OnPropertyChanged(nameof(Title));
            Status = "저장: " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("저장 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportReport()
    {
        Calculate();
        var dlg = new SaveFileDialog
        {
            Filter = "텍스트 보고서 (*.txt)|*.txt|CSV (*.csv)|*.csv",
            FileName = SafeName(Input.ProjectName) + "_선정보고서.txt"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var sb = new StringBuilder();
            if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("구분,항목,값,단위,비고");
                foreach (var row in Result.Rows)
                    sb.AppendLine($"{Csv(row.Group)},{Csv(row.Item)},{Csv(row.Value)},{Csv(row.Unit)},{Csv(row.Note)}");
            }
            else
            {
                sb.AppendLine("컨베어 구동부 선정 보고서");
                sb.AppendLine($"프로젝트: {Input.ProjectName}   종류: {ConveyorCalculator.TypeName(Input)}   작성: {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine(new string('=', 90));
                sb.AppendLine("[선정 요약]  " + Summary);
                sb.AppendLine();
                sb.AppendLine("[선정 사양]");
                string lastGroup = "";
                foreach (var row in Result.Rows)
                {
                    if (row.Group != lastGroup) { sb.AppendLine(); sb.AppendLine("  " + row.Group); lastGroup = row.Group; }
                    sb.AppendLine($"    {row.Item,-28} {row.Value,14} {row.Unit,-7} {row.Note}");
                }
                if (Result.Warnings.Count > 0)
                {
                    sb.AppendLine(); sb.AppendLine("[검토 사항]");
                    foreach (var w in Result.Warnings) sb.AppendLine("  ⚠ " + w);
                }
                sb.AppendLine(); sb.AppendLine(new string('=', 90));
                sb.AppendLine(Log);
                sb.AppendLine();
                sb.AppendLine("※ 표준 규격 데이터 기준 계산 결과이며, 최종 선정은 제조사 카탈로그(허용토크·OHL·허용관성·체인 정격)로 확인해야 합니다.");
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            Status = "보고서 저장: " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("보고서 저장 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "conveyor" : s.Trim();
    }
}
