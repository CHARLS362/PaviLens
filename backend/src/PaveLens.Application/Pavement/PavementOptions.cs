using System.ComponentModel.DataAnnotations;

namespace PaveLens.Application.Pavement;

/// <summary>
/// Patch quantities and unit prices. Every value is a configurable reference, not a code constant:
/// margins and rates must be checked against the current EG-2013 text and the project's own price analysis.
/// </summary>
public sealed class PavementOptions
{
    public const string SectionName = "Pavement";

    /// <summary>Saw-cut margin beyond the visible outline on each side, in metres (EG-2013 §401 criterion).</summary>
    [Range(0, 1)] public double PatchMarginM { get; set; } = 0.15;

    /// <summary>Compacted hot-mix density, t/m³ (Marshall design of the job; typically 2.30–2.40).</summary>
    [Range(1.5, 3)] public double HmaDensityTPerM3 { get; set; } = 2.35;

    /// <summary>Waste factor applied to the hot-mix mass.</summary>
    [Range(0, 0.5)] public double WasteFactor { get; set; } = 0.05;

    /// <summary>Depth used when the inspector has not measured one, per severity, in metres. Shown as an estimate.</summary>
    [Range(0.01, 0.3)] public double AssumedDepthLowM { get; set; } = 0.03;
    [Range(0.01, 0.3)] public double AssumedDepthMediumM { get; set; } = 0.05;
    [Range(0.01, 0.3)] public double AssumedDepthHighM { get; set; } = 0.075;

    [Required] public PriceList Prices { get; set; } = new();
    [Required] public RegionProfile Region { get; set; } = new();
}

/// <summary>Illustrative unit prices in soles (PEN). Replace them with the job's price analysis.</summary>
public sealed class PriceList
{
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "PEN";
    [Range(0, 10_000)] public decimal SawCutPerM { get; set; } = 6.50m;
    [Range(0, 10_000)] public decimal RemovalPerM3 { get; set; } = 85.00m;
    [Range(0, 10_000)] public decimal TackCoatPerM2 { get; set; } = 3.80m;
    [Range(0, 10_000)] public decimal HotMixPerT { get; set; } = 260.00m;
    [Range(0, 10_000)] public decimal CompactionPerM2 { get; set; } = 12.00m;
    [Range(0, 10_000)] public decimal CrackSealPerM { get; set; } = 9.00m;
    [Range(0, 100)] public decimal OverheadPct { get; set; } = 10m;
    [Range(0, 100)] public decimal ProfitPct { get; set; } = 5m;
    [Range(0, 100)] public decimal IgvPct { get; set; } = 18m;
}

/// <summary>Climate context of the work zone (PLANIFICACION §5.C). Explanatory; it does not change the quantities.</summary>
public sealed class RegionProfile
{
    [Required] public string Name { get; set; } = "Puno (altiplano)";
    [Range(0, 6000)] public double AltitudeM { get; set; } = 3827;
    [Required] public string RecommendedBinder { get; set; } = "Asfalto modificado con polímero SBS, grado PG 58-28 o PG 64-28 según temperaturas de diseño";

    public List<string> Notes { get; set; } =
    [
        "Heladas nocturnas frecuentes entre mayo y agosto: programar el bacheo en horas de sol.",
        "La mezcla se enfría rápido por aire frío, viento y radiación nocturna: transportar en tolva cubierta y compactar sin demora.",
        "Radiación UV extrema: envejecimiento acelerado del ligante; sellar las fisuras pronto.",
        "Datos climáticos de referencia: series de SENAMHI para las estaciones de Puno."
    ];
}
