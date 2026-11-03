using System.Text.Json;
using PaveLens.Domain.Images;
using PaveLens.Domain.Inspections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PaveLens.Infrastructure.Persistence;

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("Inspections");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Title).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Property(i => i.Location).HasMaxLength(200);
        builder.Property(i => i.RoadSegmentId).HasMaxLength(64);
        builder.Property(i => i.SegmentationModel).HasMaxLength(100);
        builder.Property(i => i.ReasoningModel).HasMaxLength(100);
        builder.Property(i => i.Summary).HasMaxLength(4000);
        builder.Property(i => i.FailureReason).HasMaxLength(1000);
        builder.Property(i => i.Version).IsConcurrencyToken();

        // Calibration is always read whole with its inspection: a small JSON document.
        builder.Property(i => i.Calibration)
            .HasColumnName("CalibrationJson")
            .HasConversion(
                v => SerializeCalibration(v),
                v => DeserializeCalibration(v),
                new ValueComparer<GroundCalibration?>(
                    (a, b) => SerializeCalibration(a) == SerializeCalibration(b),
                    v => (SerializeCalibration(v) ?? string.Empty).GetHashCode(StringComparison.Ordinal),
                    v => v));

        // The analysis trace is likewise small and read whole: who was asked what, and how long it took.
        builder.Property(i => i.Trace)
            .HasColumnName("TraceJson")
            .HasConversion(
                v => SerializeTrace(v),
                v => DeserializeTrace(v),
                new ValueComparer<AnalysisTrace?>(
                    (a, b) => SerializeTrace(a) == SerializeTrace(b),
                    v => (SerializeTrace(v) ?? string.Empty).GetHashCode(StringComparison.Ordinal),
                    v => v));

        builder.HasOne<StoredImage>()
            .WithMany()
            .HasForeignKey(i => i.ImageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Distresses)
            .WithOne()
            .HasForeignKey(d => d.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Distresses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.RepairSteps)
            .WithOne()
            .HasForeignKey(s => s.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.RepairSteps).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.CreatedAt);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.ImageId);
        builder.HasIndex(i => i.RoadSegmentId);
    }

    private sealed record CalibrationJson(double[][] Corners, double WidthM, double LengthM);

    private static string? SerializeCalibration(GroundCalibration? c) =>
        c is null
            ? null
            : JsonSerializer.Serialize(new CalibrationJson(
                c.Corners.Select(p => new[] { Math.Round(p.X, 5), Math.Round(p.Y, 5) }).ToArray(), c.WidthM, c.LengthM));

    private static GroundCalibration? DeserializeCalibration(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var c = JsonSerializer.Deserialize<CalibrationJson>(json)!;
        return new GroundCalibration(c.Corners.Select(p => new NormalizedPoint(p[0], p[1])).ToArray(), c.WidthM, c.LengthM);
    }

    private sealed record ConceptJson(string Concept, int Masks);

    private sealed record TraceJson(ConceptJson[] Concepts, long SegmentationMs, long ReasoningMs, int ReasoningAttempts, int SkippedBeyondHorizon, AgentRunTrace? Orchestration = null);

    private static string? SerializeTrace(AnalysisTrace? t) =>
        t is null
            ? null
            : JsonSerializer.Serialize(new TraceJson(
                t.Concepts.Select(c => new ConceptJson(c.Concept, c.Masks)).ToArray(),
                t.SegmentationMs, t.ReasoningMs, t.ReasoningAttempts, t.SkippedBeyondHorizon, t.Orchestration));

    private static AnalysisTrace? DeserializeTrace(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var t = JsonSerializer.Deserialize<TraceJson>(json)!;
        return new AnalysisTrace(
            t.Concepts.Select(c => new ConceptTrace(c.Concept, c.Masks)).ToList(),
            t.SegmentationMs, t.ReasoningMs, t.ReasoningAttempts, t.SkippedBeyondHorizon, t.Orchestration);
    }
}

internal sealed class DistressConfiguration : IEntityTypeConfiguration<Distress>
{
    private static readonly JsonSerializerOptions Json = new();

    public void Configure(EntityTypeBuilder<Distress> builder)
    {
        builder.ToTable("Distresses");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Code).HasMaxLength(16).IsRequired();
        builder.Property(d => d.SourceConcept).HasMaxLength(100);
        builder.Property(d => d.Diagnosis).HasMaxLength(2000);
        builder.Property(d => d.RecommendedAction).HasMaxLength(2000);
        builder.Ignore(d => d.IsPatchable);

        builder.ComplexProperty(d => d.Box, box =>
        {
            box.Property(b => b.X).HasColumnName("BoxX");
            box.Property(b => b.Y).HasColumnName("BoxY");
            box.Property(b => b.Width).HasColumnName("BoxWidth");
            box.Property(b => b.Height).HasColumnName("BoxHeight");
        });

        builder.ComplexProperty(d => d.Measurement, m =>
        {
            m.Property(x => x.AreaM2).HasColumnName("AreaM2");
            m.Property(x => x.PerimeterM).HasColumnName("PerimeterM");
            m.Property(x => x.LengthM).HasColumnName("LengthM");
            m.Property(x => x.WidthM).HasColumnName("WidthM");
            m.Property(x => x.Eccentricity).HasColumnName("Eccentricity");
            m.Property(x => x.OrientationDeg).HasColumnName("OrientationDeg");
        });

        // Simplified mask as a compact JSON array of [x, y] pairs.
        builder.Property(d => d.Mask)
            .HasColumnName("MaskJson")
            .HasConversion(
                v => SerializeMask(v),
                v => DeserializeMask(v),
                new ValueComparer<IReadOnlyList<NormalizedPoint>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, p) => HashCode.Combine(hash, p)),
                    v => v.ToArray()))
            .IsRequired();

        // Null for cracks (sealed, not patched); a nullable group of columns otherwise.
        builder.OwnsOne(d => d.Quantities, q =>
        {
            q.Property(x => x.DepthM).HasColumnName("DepthM");
            q.Property(x => x.DepthSource).HasColumnName("DepthSource");
            q.Property(x => x.CutAreaM2).HasColumnName("CutAreaM2");
            q.Property(x => x.VolumeM3).HasColumnName("VolumeM3");
            q.Property(x => x.MassT).HasColumnName("MassT");
        });

        builder.HasIndex(d => new { d.InspectionId, d.Code }).IsUnique();
    }

    private static string SerializeMask(IReadOnlyList<NormalizedPoint> mask) =>
        JsonSerializer.Serialize(mask.Select(p => new[] { Math.Round(p.X, 5), Math.Round(p.Y, 5) }), Json);

    private static NormalizedPoint[] DeserializeMask(string json) =>
        (JsonSerializer.Deserialize<double[][]>(json, Json) ?? [])
            .Select(pair => new NormalizedPoint(pair[0], pair[1]))
            .ToArray();
}

internal sealed class RepairStepConfiguration : IEntityTypeConfiguration<RepairStep>
{
    public void Configure(EntityTypeBuilder<RepairStep> builder)
    {
        builder.ToTable("RepairSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Instructions).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.TargetCode).HasMaxLength(16);
        builder.HasIndex(s => new { s.InspectionId, s.Order });
    }
}

internal sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ConversationMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Content).HasMaxLength(8000).IsRequired();

        builder.HasOne<Inspection>()
            .WithMany()
            .HasForeignKey(m => m.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Distresses are deleted together with their inspection, before it; their questions go with them.
        builder.HasOne<Distress>()
            .WithMany()
            .HasForeignKey(m => m.DistressId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.InspectionId, m.DistressId, m.CreatedAt });
    }
}

internal sealed class StoredImageConfiguration : IEntityTypeConfiguration<StoredImage>
{
    public void Configure(EntityTypeBuilder<StoredImage> builder)
    {
        builder.ToTable("Images");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(i => i.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(i => i.OriginalFileName).HasMaxLength(255);
        builder.HasIndex(i => i.Sha256).IsUnique();
    }
}
