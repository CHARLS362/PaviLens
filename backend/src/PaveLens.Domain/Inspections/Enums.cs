namespace PaveLens.Domain.Inspections;

public enum InspectionStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

/// <summary>Step of the analysis pipeline an inspection in <see cref="InspectionStatus.Processing"/> is running.</summary>
public enum AnalysisStage
{
    /// <summary>Meta SAM 3 is segmenting the photo by concept.</summary>
    Segmenting,

    /// <summary>Masks are being projected to the road plane and measured.</summary>
    Measuring,

    /// <summary>Meta Muse Wonder is classifying the measured segments and planning the work.</summary>
    Reasoning,

    /// <summary>PCI, patch quantities and the stored result are being computed.</summary>
    Computing
}

/// <summary>Pavement distress classes. RDD2020/2022 codes where they exist (D00, D10, D20, D40).</summary>
public enum DistressType
{
    /// <summary>D00 — longitudinal crack.</summary>
    LongitudinalCrack,

    /// <summary>D10 — transverse crack.</summary>
    TransverseCrack,

    /// <summary>D20 — alligator (fatigue) cracking.</summary>
    AlligatorCrack,

    /// <summary>D40 — pothole.</summary>
    Pothole,

    /// <summary>Rutting along the wheel paths (no RDD code).</summary>
    Rutting,

    Other
}

/// <summary>ASTM D6433 severity levels.</summary>
public enum Severity
{
    Low,
    Medium,
    High
}

/// <summary>Whether a distress depth was measured in the field or assumed from its severity.</summary>
public enum DepthSource
{
    Assumed,
    Measured
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

/// <summary>Which backend produced an analysis. Simulation results must never be presented as real.</summary>
public enum AnalysisEngine
{
    Meta,
    Simulation
}

/// <summary>Language the models write the report in, chosen when the inspection is created.</summary>
public enum ReportLanguage
{
    Es,
    En
}

public enum MessageRole
{
    User,
    Assistant
}
