using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PaveLens.Application.Pavement;
using PaveLens.Domain.Common;

namespace PaveLens.Application.Orchestration;

public sealed class RegionalCatalogOptions
{
    public List<RegionalConfiguration> Profiles { get; set; } = [];
}

public sealed record RegionalSource(string Title, string Url, string Version);

public sealed class RegionalConfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string Version { get; set; } = "";
    public string ConditionMethod { get; set; } = "ASTM D6433 reference model";
    public bool JurisdictionValidated { get; set; }
    public bool PricesAvailable { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public PavementOptions Pavement { get; set; } = new()
    {
        Prices = new PriceList { Currency = "XXX", SawCutPerM = 0, RemovalPerM3 = 0, TackCoatPerM2 = 0, HotMixPerT = 0, CompactionPerM2 = 0, CrackSealPerM = 0, OverheadPct = 0, ProfitPct = 0, IgvPct = 0 },
        Region = new RegionProfile { Name = "Sin perfil local validado", AltitudeM = 0, RecommendedBinder = "Requiere diseño y validación local", Notes = ["No se aplican precios ni recomendaciones climáticas de Puno a esta ubicación."] }
    };
    public List<RegionalSource> Sources { get; set; } = [];
}

/// <summary>Explicit configurations snapshotted before an inspection is queued.</summary>
public sealed class RegionalCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, RegionalConfiguration> _profiles;

    public RegionalCatalog(IOptions<RegionalCatalogOptions> catalog, IOptions<PavementOptions> pavement)
    {
        var profiles = new List<RegionalConfiguration>
        {
            new() { Id = "pe-puno", Name = "Puno · Perú", CountryCode = "PE", Version = "reference-2026-11", PricesAvailable = true, Latitude = -15.8402, Longitude = -70.0219, Pavement = pavement.Value },
            new()
            {
                Id = "global-reference", Name = "Referencia internacional · sin precios locales", CountryCode = "ZZ",
                Version = "reference-2026-11",
                PricesAvailable = false, Latitude = 0, Longitude = 0
            }
        };
        foreach (var profile in catalog.Value.Profiles)
        {
            Validate(profile);
            profiles.RemoveAll(p => p.Id == profile.Id);
            profiles.Add(profile);
        }
        _profiles = profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<RegionalConfiguration> List() => _profiles.Values.Select(Clone).ToArray();
    public RegionalConfiguration Get(string? id) => _profiles.TryGetValue(id ?? "pe-puno", out var profile)
        ? Clone(profile) : throw new DomainException($"Perfil regional no disponible: {id}.");
    public static string Snapshot(RegionalConfiguration profile) => JsonSerializer.Serialize(profile, Json);
    public static RegionalConfiguration Restore(string json)
    {
        var profile = JsonSerializer.Deserialize<RegionalConfiguration>(json, Json) ?? throw new DomainException("Perfil regional vacío.");
        Validate(profile);
        return profile;
    }
    private static RegionalConfiguration Clone(RegionalConfiguration profile) => Restore(Snapshot(profile));

    private static void Validate(RegionalConfiguration profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 80 || string.IsNullOrWhiteSpace(profile.Version) ||
            string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.CountryCode) || profile.CountryCode.Length != 2 ||
            profile.Pavement is null || profile.Pavement.Prices is null || profile.Pavement.Region is null ||
            !double.IsFinite(profile.Latitude) || !double.IsFinite(profile.Longitude) ||
            profile.Latitude is < -85 or > 85 || profile.Longitude is < -180 or > 180 ||
            profile.ConditionMethod != "ASTM D6433 reference model")
            throw new DomainException("Perfil regional inválido o método de evaluación no implementado.");
        Validator.ValidateObject(profile.Pavement, new ValidationContext(profile.Pavement), true);
        Validator.ValidateObject(profile.Pavement.Prices, new ValidationContext(profile.Pavement.Prices), true);
        Validator.ValidateObject(profile.Pavement.Region, new ValidationContext(profile.Pavement.Region), true);
        var prices = profile.Pavement.Prices;
        if (profile.PricesAvailable && (prices.Currency == "XXX" || prices.SawCutPerM + prices.RemovalPerM3 + prices.TackCoatPerM2 + prices.HotMixPerT + prices.CompactionPerM2 + prices.CrackSealPerM <= 0))
            throw new DomainException("Para presupuestar se necesita una moneda y un libro de precios explícitos.");
        if (profile.JurisdictionValidated && profile.Sources.Count == 0)
            throw new DomainException("Un perfil validado necesita referencias técnicas versionadas.");
        if (profile.Sources.Any(s => !Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrWhiteSpace(s.Version)))
            throw new DomainException("Las fuentes regionales necesitan URL HTTPS y versión.");
    }
}
