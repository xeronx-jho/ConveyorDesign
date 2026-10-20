using System.Collections.ObjectModel;

namespace ConveyorDesign.Models;

public sealed class ResultRow
{
    public string Group { get; init; } = "";
    public string Item { get; init; } = "";
    public string Value { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Note { get; init; } = "";
}

public sealed class SelectionResult
{
    public bool Ok { get; set; } = true;
    public List<string> Warnings { get; } = new();
    public ObservableCollection<ResultRow> Rows { get; } = new();
    public string CalculationLog { get; set; } = "";

    // 주요 수치 (저장/비교용)
    public double EffectiveTension { get; set; }      // FU [N]
    public double DriveShaftRpm { get; set; }         // 구동축 회전수 [rpm]
    public double DriveShaftTorque { get; set; }      // 구동축 토크 [N·m]
    public double ShaftPowerKw { get; set; }          // PA [kW]
    public double RequiredMotorKw { get; set; }
    public double SelectedMotorKw { get; set; }
    public double MotorRpm { get; set; }
    public double ReducerRatio { get; set; }
    public double ReducerOutputRpm { get; set; }
    public double ReducerOutputTorque { get; set; }
    public int DriveSprocketTeeth { get; set; }
    public int DrivenSprocketTeeth { get; set; }
    public string TransmissionChain { get; set; } = "";
    public int TransmissionChainLinks { get; set; }
    public double ActualSpeed { get; set; }
    public double SpeedErrorPercent { get; set; }

    public void Add(string group, string item, double value, string unit, string fmt = "0.##", string note = "")
        => Rows.Add(new ResultRow { Group = group, Item = item, Value = value.ToString(fmt), Unit = unit, Note = note });

    public void Add(string group, string item, string value, string unit = "", string note = "")
        => Rows.Add(new ResultRow { Group = group, Item = item, Value = value, Unit = unit, Note = note });
}
