using System.IO;

namespace SpriteStudio;

internal sealed record DatCompileOptions(
    DatTargetVersion TargetVersion,
    bool Extended,
    bool Transparency,
    bool ImprovedAnimations,
    bool FrameGroups);

internal sealed record DatCompileResult(DatFileModel Model, IReadOnlyList<uint> SourceSpriteIds);

internal static class DatCompiler
{
    public static DatCompileResult Prepare(DatFileModel source, int sourceSpriteCount, DatCompileOptions options)
    {
        var convertedThings = source.Things.Select(thing =>
        {
            var groups = thing.FrameGroups;
            if (thing.Category == "outfit" && !options.FrameGroups)
            {
                groups = new[] { groups.FirstOrDefault(group => group.Type == 0) ?? groups[0] };
            }

            var normalizedGroups = groups.Select(group => NormalizeFrameGroup(group, options)).ToArray();
            return thing with
            {
                RawProperties = DatPropertiesCodec.ConvertToClientVersion(
                    thing.RawProperties, source.VersionValue, options.TargetVersion.Value),
                FrameGroups = normalizedGroups
            };
        }).ToArray();

        var invalidSpriteId = convertedThings.SelectMany(thing => thing.FrameGroups)
            .SelectMany(group => group.Frames)
            .SelectMany(frame => frame.SpriteIds)
            .FirstOrDefault(id => id > sourceSpriteCount);
        if (invalidSpriteId != 0)
            throw new InvalidDataException($"The DAT refers to sprite {invalidSpriteId}, but the source SPR contains only {sourceSpriteCount:N0} sprites.");

        IReadOnlyList<uint> sourceSpriteIds;
        if (options.Extended)
        {
            sourceSpriteIds = Enumerable.Range(1, sourceSpriteCount).Select(id => (uint)id).ToArray();
        }
        else
        {
            var referencedIds = convertedThings.SelectMany(thing => thing.FrameGroups)
                .SelectMany(group => group.Frames)
                .SelectMany(frame => frame.SpriteIds)
                .Where(id => id != 0)
                .Distinct()
                .Order()
                .ToArray();
            if (referencedIds.Length > ushort.MaxValue)
                throw new InvalidDataException($"This DAT references {referencedIds.Length:N0} unique sprites, which exceeds the 65,535 sprite limit without Extended.");

            var remap = referencedIds.Select((sourceId, index) => (sourceId, outputId: (uint)index + 1))
                .ToDictionary(pair => pair.sourceId, pair => pair.outputId);
            convertedThings = convertedThings.Select(thing => thing with
            {
                FrameGroups = thing.FrameGroups.Select(group => group with
                {
                    Frames = group.Frames.Select(frame => frame with
                    {
                        SpriteIds = frame.SpriteIds.Select(id => id == 0 ? 0 : remap[id]).ToArray()
                    }).ToArray()
                }).ToArray()
            }).ToArray();
            sourceSpriteIds = referencedIds;
        }

        var model = source with
        {
            Signature = options.TargetVersion.DatSignature,
            VersionName = options.TargetVersion.Name,
            VersionValue = options.TargetVersion.Value,
            Things = convertedThings,
            Extended = options.Extended,
            ImprovedAnimations = options.ImprovedAnimations,
            FrameGroups = options.FrameGroups
        };
        return new DatCompileResult(model, sourceSpriteIds);
    }

    private static DatFrameGroup NormalizeFrameGroup(DatFrameGroup group, DatCompileOptions options)
    {
        var result = group;
        if (options.TargetVersion.Value <= 750 && result.PatternZ > 1)
        {
            var spritesPerZ = checked(result.Width * result.Height * result.Layers * result.PatternX * result.PatternY);
            result = result with
            {
                PatternZ = 1,
                Frames = result.Frames.Select(frame => frame with
                {
                    SpriteIds = frame.SpriteIds.Take(spritesPerZ).ToArray()
                }).ToArray()
            };
        }

        if (!options.ImprovedAnimations && result.Frames.Count > 1)
        {
            result = result with
            {
                AnimationMode = 0,
                LoopCount = 0,
                StartFrame = 0,
                Frames = result.Frames.Select(frame => frame with { MinimumDuration = 100, MaximumDuration = 100 }).ToArray()
            };
        }
        return result;
    }
}
