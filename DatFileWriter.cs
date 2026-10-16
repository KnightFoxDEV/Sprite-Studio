using System.IO;

namespace SpriteStudio;

internal static class DatFileWriter
{
    private const int MaximumSpriteReferencesPerThing = 1_000_000;

    public static byte[] Serialize(DatFileModel model, bool? extendedOverride = null,
        bool? improvedAnimationsOverride = null, bool? frameGroupsOverride = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var uses32BitIds = model.Uses32BitIds || model.ItemCount > ushort.MaxValue ||
            model.OutfitCount > ushort.MaxValue || model.EffectCount > ushort.MaxValue ||
            model.MissileCount > ushort.MaxValue;
        if (uses32BitIds)
        {
            writer.Write(DatFileParser.Dat32Magic);
            writer.Write(model.Signature);
            writer.Write(model.ItemCount);
            writer.Write(model.OutfitCount);
            writer.Write(model.EffectCount);
            writer.Write(model.MissileCount);
        }
        else
        {
            writer.Write(model.Signature);
            writer.Write(checked((ushort)model.ItemCount));
            writer.Write(checked((ushort)model.OutfitCount));
            writer.Write(checked((ushort)model.EffectCount));
            writer.Write(checked((ushort)model.MissileCount));
        }

        var extended = (extendedOverride ?? model.Extended) || uses32BitIds;
        var improvedAnimations = improvedAnimationsOverride ?? model.ImprovedAnimations;
        var hasFrameGroups = frameGroupsOverride ?? model.FrameGroups;
        foreach (var thing in model.Things)
        {
            if (thing.RawProperties.Length == 0 || thing.RawProperties[^1] != 0xFF)
                throw new InvalidDataException($"Object {thing.Id} has no valid DAT property terminator.");
            writer.Write(thing.RawProperties);

            var groups = thing.FrameGroups;
            if (groups.Count == 0) throw new InvalidDataException($"Object {thing.Id} has no frame group.");
            if (thing.Category == "outfit" && hasFrameGroups)
            {
                WriteByte(writer, groups.Count, "frame groups");
            }

            var groupCount = thing.Category == "outfit" && hasFrameGroups ? groups.Count : 1;
            for (var index = 0; index < groupCount; index++)
            {
                var group = groups[index];
                if (thing.Category == "outfit" && hasFrameGroups) writer.Write(group.Type);
                WriteFrameGroup(writer, group, thing.Id, extended, improvedAnimations, model.VersionValue > 750);
            }
        }

        return stream.ToArray();
    }

    private static void WriteFrameGroup(BinaryWriter writer, DatFrameGroup group, uint thingId,
        bool extended, bool improvedAnimations, bool hasPatternZ)
    {
        WriteByte(writer, group.Width, "width");
        WriteByte(writer, group.Height, "height");
        if (group.Width > 1 || group.Height > 1)
        {
            if (group.CropSize is < 0 or > byte.MaxValue) throw new InvalidDataException("Crop size must be between 0 and 255.");
            writer.Write((byte)group.CropSize);
        }
        WriteByte(writer, group.Layers, "layers");
        WriteByte(writer, group.PatternX, "pattern X");
        WriteByte(writer, group.PatternY, "pattern Y");
        if (hasPatternZ) WriteByte(writer, group.PatternZ, "pattern Z");

        if (group.Frames.Count == 0) throw new InvalidDataException($"Object {thingId} has no animation frames.");
        WriteByte(writer, group.Frames.Count, "animation frames");
        if (group.Frames.Count > 1 && improvedAnimations)
        {
            writer.Write(group.AnimationMode);
            writer.Write(group.LoopCount);
            writer.Write(group.StartFrame);
            foreach (var frame in group.Frames)
            {
                writer.Write(frame.MinimumDuration);
                writer.Write(frame.MaximumDuration);
            }
        }

        var spritesPerFrame64 = (long)group.Width * group.Height * group.Layers
            * group.PatternX * group.PatternY * group.PatternZ;
        var totalSpriteCount = spritesPerFrame64 * group.Frames.Count;
        if (spritesPerFrame64 <= 0 || totalSpriteCount > MaximumSpriteReferencesPerThing)
            throw new InvalidDataException($"Object {thingId} has too many sprite references.");
        var spritesPerFrame = checked((int)spritesPerFrame64);
        foreach (var frame in group.Frames)
        {
            if (frame.SpriteIds.Length != spritesPerFrame)
                throw new InvalidDataException($"Object {thingId} has an invalid sprite count for a frame.");
            foreach (var spriteId in frame.SpriteIds)
            {
                if (extended) writer.Write(spriteId);
                else writer.Write(checked((ushort)spriteId));
            }
        }
    }

    private static void WriteByte(BinaryWriter writer, int value, string field)
    {
        if (value is < 1 or > byte.MaxValue)
            throw new InvalidDataException($"{field} must be between 1 and 255.");
        writer.Write((byte)value);
    }
}
