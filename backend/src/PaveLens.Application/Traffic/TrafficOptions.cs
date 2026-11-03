using System.ComponentModel.DataAnnotations;

namespace PaveLens.Application.Traffic;

/// <summary>Time window in which the work zone is open.</summary>
public enum WorkPeriod
{
    /// <summary>Morning or evening peak (07–09 h, 17–19 h).</summary>
    Peak,

    /// <summary>Daytime off-peak (09–17 h).</summary>
    OffPeak,

    /// <summary>Night (22–05 h).</summary>
    Night
}

/// <summary>Demand profile and user-cost parameters of the work-zone simulation.</summary>
public sealed class TrafficOptions
{
    public const string SectionName = "Traffic";

    /// <summary>Share of the peak-hour demand present in each period. Illustrative urban profile; replace with counts.</summary>
    [Range(0.01, 2)] public double PeakDemandFactor { get; set; } = 1.0;
    [Range(0.01, 2)] public double OffPeakDemandFactor { get; set; } = 0.65;
    [Range(0.01, 2)] public double NightDemandFactor { get; set; } = 0.2;

    /// <summary>
    /// Value of time per vehicle-hour, PEN. Reference value (≈ 1.6 occupants × S/ 6.0 per person-hour);
    /// replace with the MEF social value of time in force for the project evaluation.
    /// </summary>
    [Range(0, 1000)] public double ValueOfTimePenPerVehH { get; set; } = 9.6;

    public double DemandFactor(WorkPeriod period) => period switch
    {
        WorkPeriod.OffPeak => OffPeakDemandFactor,
        WorkPeriod.Night => NightDemandFactor,
        _ => PeakDemandFactor
    };
}
