using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace SpriteStudio;

internal sealed record DatAnimationFrame(uint[] SpriteIds, uint MinimumDuration, uint MaximumDuration);
internal sealed record DatFrameGroup(byte Type, int Width, int Height, int CropSize, int Layers,
    int PatternX, int PatternY, int PatternZ, byte AnimationMode, int LoopCount, sbyte StartFrame,
    IReadOnlyList<DatAnimationFrame> Frames);
internal sealed record DatThing(uint Id, string Category, IReadOnlyList<DatFrameGroup> FrameGroups, int SpriteCount, byte[] RawProperties)
{
    public DatFrameGroup? PreviewGroup => FrameGroups.FirstOrDefault();
    public int Width => PreviewGroup?.Width ?? 0;
    public int Height => PreviewGroup?.Height ?? 0;
}
internal sealed record DatFileModel(uint Signature, string VersionName, int VersionValue, ushort ItemCount, ushort OutfitCount, ushort EffectCount, ushort MissileCount, IReadOnlyList<DatThing> Things, bool Extended, bool ImprovedAnimations, bool FrameGroups);
internal sealed record DatTargetVersion(int Value, string Name, uint DatSignature, uint SprSignature);

internal static class ClientVersionCatalog
{
    private sealed record VersionEntry(uint DatSignature, uint SprSignature, int Value, string Name);
    private static readonly IReadOnlyList<VersionEntry> Entries = LoadEntries();

    private static IReadOnlyList<VersionEntry> LoadEntries()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpriteStudio.Config.versions.xml")
            ?? throw new InvalidOperationException("Embedded client version list is missing.");
        var document = XDocument.Load(stream);
        return document.Root!.Elements("version").Select(element => new VersionEntry(
            Convert.ToUInt32((string?)element.Attribute("dat") ?? "0", 16),
            Convert.ToUInt32((string?)element.Attribute("spr") ?? "0", 16),
            (int?)element.Attribute("value") ?? 0,
            (string?)element.Attribute("string") ?? "Unknown")).ToArray();
    }

    public static (int Value, string Name)? FindByDatSignature(uint signature) => Find(signature, useSpr: false);
    public static (int Value, string Name)? FindBySprSignature(uint signature) => Find(signature, useSpr: true);
    public static bool AreMatchingSignatures(uint datSignature, uint sprSignature) =>
        Entries.Any(entry => entry.DatSignature == datSignature && entry.SprSignature == sprSignature);
    public static IReadOnlyList<(int Value, string Name)> GetExportVersions() =>
        Entries.Select(entry => (entry.Value, entry.Name)).ToArray();
    public static IReadOnlyList<DatTargetVersion> GetDatCompileVersions() => Entries
        .GroupBy(entry => entry.Value)
        .Select(group => group.Last())
        .OrderBy(entry => entry.Value)
        .Select(entry => new DatTargetVersion(entry.Value, entry.Name, entry.DatSignature, entry.SprSignature))
        .ToArray();

    public static DatTargetVersion GetDatCompileVersion(int value) => GetDatCompileVersions()
        .FirstOrDefault(version => version.Value == value)
        ?? throw new InvalidDataException($"Tibia version {value} is not in the supported version list.");

    private static (int Value, string Name)? Find(uint signature, bool useSpr)
    {
        var found = Entries.LastOrDefault(entry => (useSpr ? entry.SprSignature : entry.DatSignature) == signature);
        return found is null ? null : (found.Value, found.Name);
    }
}

internal static class DatFileParser
{
    private const uint MaximumSpriteReferencesPerThing = 1_000_000;

    public static DatFileModel Parse(string path, int? versionFallback = null, bool? extendedOverride = null, bool? improvedAnimationsOverride = null, bool? frameGroupsOverride = null)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 12) throw new InvalidDataException("The DAT file is too small to contain a valid header.");
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        var signature = ReadUInt32(reader);
        var version = ClientVersionCatalog.FindByDatSignature(signature);
        if (version is null && versionFallback is null)
            throw new InvalidDataException($"Unknown DAT signature 0x{signature:X8}. Open its matching SPR file first so the client version can be identified.");

        var versionValue = version?.Value ?? versionFallback!.Value;
        var versionName = version?.Name ?? $"Tibia {versionValue / 100}.{versionValue % 100:00}";
        var itemCount = ReadUInt16(reader);
        var outfitCount = ReadUInt16(reader);
        var effectCount = ReadUInt16(reader);
        var missileCount = ReadUInt16(reader);
        var readerVersion = versionValue <= 730 ? 1 : versionValue <= 750 ? 2 : versionValue <= 772 ? 3 : versionValue <= 854 ? 4 : versionValue <= 986 ? 5 : 6;
        var defaults = ReadFormatOptions(path, versionValue);
        var extended = extendedOverride ?? defaults.Extended;
        var improvedAnimations = improvedAnimationsOverride ?? defaults.ImprovedAnimations;
        var frameGroups = frameGroupsOverride ?? defaults.FrameGroups;
        var things = new List<DatThing>(Math.Min(100_000, itemCount + outfitCount + effectCount + missileCount));

        ReadThings(reader, things, 100, itemCount, "item", readerVersion, extended, improvedAnimations, frameGroups);
        ReadThings(reader, things, 1, outfitCount, "outfit", readerVersion, extended, improvedAnimations, frameGroups);
        ReadThings(reader, things, 1, effectCount, "effect", readerVersion, extended, improvedAnimations, frameGroups);
        ReadThings(reader, things, 1, missileCount, "missile", readerVersion, extended, improvedAnimations, frameGroups);
        if (stream.Position != stream.Length)
            throw new InvalidDataException($"The DAT parser stopped at byte {stream.Position:N0} of {stream.Length:N0}. The selected client version may not match this file.");

        return new DatFileModel(signature, versionName, versionValue, itemCount, outfitCount, effectCount, missileCount, things, extended, improvedAnimations, frameGroups);
    }

    private static (bool Extended, bool ImprovedAnimations, bool FrameGroups) ReadFormatOptions(string datPath, int versionValue)
    {
        var defaults = (Extended: versionValue >= 960, ImprovedAnimations: versionValue >= 1050, FrameGroups: versionValue >= 1057);
        var otfiPath = Path.ChangeExtension(datPath, ".otfi");
        if (!File.Exists(otfiPath)) return defaults;

        var options = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["extended"] = defaults.Extended,
            ["frame-durations"] = defaults.ImprovedAnimations,
            ["frame-groups"] = defaults.FrameGroups
        };
        foreach (var line in File.ReadLines(otfiPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var separator = trimmed.IndexOf(':');
            if (separator <= 0) continue;
            var key = trimmed[..separator].Trim();
            if (!options.ContainsKey(key)) continue;
            var value = trimmed[(separator + 1)..].Split('#', 2)[0].Trim();
            if (!bool.TryParse(value, out var enabled))
                throw new InvalidDataException($"Invalid OTFI value for '{key}' in {Path.GetFileName(otfiPath)}.");
            options[key] = enabled;
        }

        return (options["extended"], options["frame-durations"], options["frame-groups"]);
    }

    private static void ReadThings(BinaryReader reader, List<DatThing> things, uint firstId, uint lastId, string category,
        int readerVersion, bool extended, bool improvedAnimations, bool frameGroups)
    {
        if (lastId < firstId) return;
        for (var id = firstId; id <= lastId; id++)
        {
            var rawProperties = SkipProperties(reader, readerVersion, category, id);
            var groups = category == "outfit" && frameGroups ? ReadByte(reader) : 1;
            var frameGroupList = new List<DatFrameGroup>(groups);
            var spriteCount = 0;
            for (var group = 0; group < groups; group++)
            {
                var groupType = category == "outfit" && frameGroups ? ReadByte(reader) : (byte)0;
                var width = ReadByte(reader);
                var height = ReadByte(reader);
                var cropSize = width > 1 || height > 1 ? ReadByte(reader) : (byte)32;
                var layers = ReadByte(reader);
                var patternX = ReadByte(reader);
                var patternY = ReadByte(reader);
                var patternZ = readerVersion >= 3 ? ReadByte(reader) : 1;
                var frames = ReadByte(reader);
                var animationMode = (byte)0;
                var loopCount = 0;
                sbyte startFrame = 0;
                var minimumDurations = new uint[frames];
                var maximumDurations = new uint[frames];
                Array.Fill(minimumDurations, 100u);
                Array.Fill(maximumDurations, 100u);
                if (frames > 1)
                {
                    if (improvedAnimations)
                    {
                        animationMode = ReadByte(reader);
                        loopCount = ReadInt32(reader);
                        startFrame = ReadSByte(reader);
                        for (var frame = 0; frame < frames; frame++)
                        {
                            minimumDurations[frame] = ReadUInt32(reader);
                            maximumDurations[frame] = ReadUInt32(reader);
                        }
                    }
                }

                var count64 = (ulong)width * (ulong)height * (ulong)layers * (ulong)patternX * (ulong)patternY * (ulong)patternZ * (ulong)frames;
                if (count64 > MaximumSpriteReferencesPerThing)
                    throw new InvalidDataException($"An object has an unreasonable number of sprite references ({count64:N0}).");
                var count = (int)count64;
                var allSpriteIds = new uint[count];
                for (var index = 0; index < count; index++)
                {
                    allSpriteIds[index] = extended ? ReadUInt32(reader) : ReadUInt16(reader);
                }
                spriteCount += count;
                var spritesPerFrame = frames == 0 ? 0 : count / frames;
                var animationFrames = new List<DatAnimationFrame>(frames);
                for (var frame = 0; frame < frames; frame++)
                {
                    var frameSprites = new uint[spritesPerFrame];
                    Array.Copy(allSpriteIds, frame * spritesPerFrame, frameSprites, 0, spritesPerFrame);
                    animationFrames.Add(new DatAnimationFrame(frameSprites, minimumDurations[frame], maximumDurations[frame]));
                }
                frameGroupList.Add(new DatFrameGroup(groupType, width, height, cropSize, layers,
                    patternX, patternY, patternZ, animationMode, loopCount, startFrame, animationFrames));
            }
            if (frameGroupList.Count > 0)
                things.Add(new DatThing(id, category, frameGroupList, spriteCount, rawProperties));
            if (id == uint.MaxValue) break;
        }
    }

    private static byte[] SkipProperties(BinaryReader reader, int readerVersion, string category, uint id)
    {
        var start = reader.BaseStream.Position;
        for (var guard = 0; guard < 256; guard++)
        {
            var flag = ReadByte(reader);
            if (flag == 0xFF)
            {
                var length = checked((int)(reader.BaseStream.Position - start));
                reader.BaseStream.Position = start;
                var bytes = reader.ReadBytes(length);
                if (bytes.Length != length) throw new EndOfStreamException();
                return bytes;
            }
            var payloadBytes = PropertyPayloadBytes(flag, readerVersion, reader);
            if (payloadBytes > 0) Skip(reader, payloadBytes);
        }
        throw new InvalidDataException($"An object has an invalid property list in {category} {id} at byte {reader.BaseStream.Position:N0}.");
    }

    internal static int PropertyPayloadBytes(byte flag, int version, BinaryReader reader)
    {
        if (version == 1)
        {
            return flag switch { 0x00 or 0x07 or 0x08 or 0x10 or 0x13 or 0x16 or 0x1A => flag == 0x10 ? 4 : 2,
                0x14 => 0,
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F or 0x11 or 0x12 or 0x17 or 0x18 or 0x19 or 0x24 or 0x25 or 0x26 => 0,
                _ => throw UnknownFlag(flag, version) };
        }
        if (version == 2)
        {
            return flag switch { 0x00 or 0x07 or 0x08 or 0x10 or 0x13 or 0x16 or 0x1D => flag == 0x10 ? 4 : 2,
                0x14 => 0,
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F or 0x11 or 0x12 or 0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x1C or 0x24 or 0x25 or 0x26 => 0,
                _ => throw UnknownFlag(flag, version) };
        }
        if (version == 3)
        {
            return flag switch { 0x00 or 0x08 or 0x09 or 0x19 or 0x1C or 0x1D => 2, 0x15 or 0x18 => 4,
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x17 or 0x1A or 0x1B or 0x1E => 0,
                _ => throw UnknownFlag(flag, version) };
        }
        if (version == 4)
        {
            return flag switch { 0x00 or 0x09 or 0x0A or 0x1A or 0x1D or 0x1E => 2, 0x16 or 0x19 => 4, 0x27 => 16,
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x17 or 0x18 or 0x1B or 0x1C or 0x1F or 0x20 or 0x24 or 0x25 => 0,
                _ => throw UnknownFlag(flag, version) };
        }
        if (version == 5)
        {
            return flag switch
            {
                0x00 or 0x15 or 0x1D => flag == 0x15 ? 4 : 2,
                0x18 => 4,
                0x08 or 0x09 or 0x19 or 0x1C or 0x20 => 2,
                0x21 => ReadMarketPayloadLength(reader),
                0x27 => 16,
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x16 or 0x17 or 0x1A or 0x1B or 0x1E or 0x1F => 0,
                _ => throw UnknownFlag(flag, version)
            };
        }
        return flag switch
        {
            0x00 or 0x08 or 0x09 or 0x1A or 0x1D or 0x1E or 0x21 or 0x23 => 2,
            0x16 or 0x19 => 4,
            0x22 => ReadMarketPayloadLength(reader),
            0x27 => 16,
            0xFF => 0,
            0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x17 or 0x18 or 0x1B or 0x1C or 0x1F or 0x20 or 0x24 or 0x25 or 0x26 or 0xFE => 0,
            _ => throw UnknownFlag(flag, version)
        };
    }

    private static int ReadMarketPayloadLength(BinaryReader reader)
    {
        _ = ReadUInt16(reader); _ = ReadUInt16(reader); _ = ReadUInt16(reader);
        var nameLength = ReadUInt16(reader);
        return checked(nameLength + 4); // market name plus profession and level
    }

    private static InvalidDataException UnknownFlag(byte flag, int readerVersion) => new($"Unknown DAT property 0x{flag:X2} for metadata reader {readerVersion}.");
    private static void Skip(BinaryReader reader, int count)
    {
        if (reader.BaseStream.Position + count > reader.BaseStream.Length) throw new EndOfStreamException();
        reader.BaseStream.Seek(count, SeekOrigin.Current);
    }
    private static byte ReadByte(BinaryReader reader) { try { return reader.ReadByte(); } catch (EndOfStreamException) { throw new InvalidDataException("The DAT file ended while reading an object."); } }
    private static ushort ReadUInt16(BinaryReader reader) { try { return reader.ReadUInt16(); } catch (EndOfStreamException) { throw new InvalidDataException("The DAT file ended while reading an object."); } }
    private static uint ReadUInt32(BinaryReader reader) { try { return reader.ReadUInt32(); } catch (EndOfStreamException) { throw new InvalidDataException("The DAT file ended while reading an object."); } }
    private static int ReadInt32(BinaryReader reader) { try { return reader.ReadInt32(); } catch (EndOfStreamException) { throw new InvalidDataException("The DAT file ended while reading an object."); } }
    private static sbyte ReadSByte(BinaryReader reader) { try { return reader.ReadSByte(); } catch (EndOfStreamException) { throw new InvalidDataException("The DAT file ended while reading an object."); } }
}
