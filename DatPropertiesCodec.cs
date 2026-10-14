using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SpriteStudio;

internal sealed record DatPropertyField(bool Enabled, int[] Values, string Text);
internal sealed record DatRawProperty(byte Flag, byte[] Payload);

internal static class DatPropertiesCodec
{
    private enum PayloadKind { Flag, UInt16, SignedPair, Light, Market, Bones }
    private sealed record Descriptor(string Key, byte Flag, PayloadKind Kind, int[] Defaults);

    public static int ReaderVersion(int clientVersion) => clientVersion <= 730 ? 1
        : clientVersion <= 750 ? 2
        : clientVersion <= 772 ? 3
        : clientVersion <= 854 ? 4
        : clientVersion <= 986 ? 5 : 6;

    public static IReadOnlyDictionary<string, DatPropertyField> Read(byte[] raw, int clientVersion)
    {
        var readerVersion = ReaderVersion(clientVersion);
        var descriptors = Descriptors(readerVersion);
        var fields = descriptors.ToDictionary(pair => pair.Key,
            pair => new DatPropertyField(false, pair.Value.Defaults.ToArray(), string.Empty), StringComparer.Ordinal);
        foreach (var property in Parse(raw, readerVersion))
        {
            var descriptor = descriptors.Values.FirstOrDefault(candidate => candidate.Flag == property.Flag);
            if (descriptor is null) continue;
            fields[descriptor.Key] = Decode(descriptor, property.Payload);
        }
        return fields;
    }

    public static bool Supports(int clientVersion, string key) => Descriptors(ReaderVersion(clientVersion)).ContainsKey(key);

    public static bool HasEditableValues(int clientVersion, string key) =>
        !(ReaderVersion(clientVersion) <= 2 && key == "offset");

    public static DatPropertyField Get(IReadOnlyDictionary<string, DatPropertyField> fields, string key) =>
        fields.TryGetValue(key, out var field) ? field : new DatPropertyField(false, [], string.Empty);

    public static byte[] Set(byte[] raw, int clientVersion, string key, DatPropertyField field)
    {
        var readerVersion = ReaderVersion(clientVersion);
        var descriptors = Descriptors(readerVersion);
        if (!descriptors.TryGetValue(key, out var descriptor))
            throw new InvalidOperationException($"The {key} DAT property is not available in metadata reader {readerVersion}.");

        var properties = Parse(raw, readerVersion).ToList();
        properties.RemoveAll(property => property.Flag == descriptor.Flag);
        if (field.Enabled) properties.Add(new DatRawProperty(descriptor.Flag, Encode(descriptor, field)));
        return Serialize(properties);
    }

    public static byte[] SetRenderOrder(byte[] raw, int clientVersion, string key)
    {
        var readerVersion = ReaderVersion(clientVersion);
        var descriptors = Descriptors(readerVersion);
        var renderKeys = new[] { "groundBorder", "onBottom", "onTop" };
        var renderFlags = renderKeys.Where(descriptors.ContainsKey).Select(item => descriptors[item].Flag).ToHashSet();
        var properties = Parse(raw, readerVersion).Where(property => !renderFlags.Contains(property.Flag)).ToList();
        if (!string.Equals(key, "common", StringComparison.Ordinal))
        {
            if (!descriptors.TryGetValue(key, out var descriptor))
                throw new InvalidOperationException($"The {key} render order is not available in metadata reader {readerVersion}.");
            properties.Add(new DatRawProperty(descriptor.Flag, []));
        }
        return Serialize(properties);
    }

    public static byte[] ConvertToClientVersion(byte[] raw, int sourceVersion, int targetVersion)
    {
        var sourceFields = Read(raw, sourceVersion);
        var targetDescriptors = Descriptors(ReaderVersion(targetVersion));
        var properties = new List<DatRawProperty>();
        var renderOrder = new[] { "ground", "groundBorder", "onBottom", "onTop" };
        var selectedRenderOrder = renderOrder.FirstOrDefault(key =>
            targetDescriptors.ContainsKey(key) && Get(sourceFields, key).Enabled);

        foreach (var (key, descriptor) in targetDescriptors)
        {
            if (renderOrder.Contains(key, StringComparer.Ordinal))
            {
                if (key != selectedRenderOrder) continue;
            }
            var field = Get(sourceFields, key);
            if (field.Enabled) properties.Add(new DatRawProperty(descriptor.Flag, Encode(descriptor, field)));
        }
        return Serialize(properties);
    }

    private static DatPropertyField Decode(Descriptor descriptor, byte[] payload)
    {
        var values = descriptor.Defaults.ToArray();
        var span = payload.AsSpan();
        switch (descriptor.Kind)
        {
            case PayloadKind.Flag:
                break;
            case PayloadKind.UInt16:
                values[0] = ReadUInt16(span, 0);
                break;
            case PayloadKind.SignedPair:
                values[0] = ReadInt16(span, 0);
                values[1] = ReadInt16(span, 2);
                break;
            case PayloadKind.Light:
                values[0] = ReadUInt16(span, 0); // ObjectBuilder stores intensity before color.
                values[1] = ReadUInt16(span, 2);
                break;
            case PayloadKind.Market:
                values[0] = ReadUInt16(span, 0);
                values[1] = ReadUInt16(span, 2);
                values[2] = ReadUInt16(span, 4);
                var nameLength = ReadUInt16(span, 6);
                if (8 + nameLength + 4 > span.Length) throw new InvalidDataException("The DAT market property has an invalid name length.");
                var name = Encoding.Latin1.GetString(span.Slice(8, nameLength));
                values[3] = ReadUInt16(span, 8 + nameLength);
                values[4] = ReadUInt16(span, 10 + nameLength);
                return new DatPropertyField(true, values, name);
            case PayloadKind.Bones:
                for (var index = 0; index < 8; index++) values[index] = ReadInt16(span, index * 2);
                break;
            default:
                throw new InvalidDataException("Unknown DAT property payload type.");
        }
        return new DatPropertyField(true, values, string.Empty);
    }

    private static byte[] Encode(Descriptor descriptor, DatPropertyField field)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        int Value(int index) => index < field.Values.Length ? field.Values[index] : descriptor.Defaults[index];
        switch (descriptor.Kind)
        {
            case PayloadKind.Flag:
                break;
            case PayloadKind.UInt16:
                writer.Write(checked((ushort)Value(0)));
                break;
            case PayloadKind.SignedPair:
                writer.Write(checked((short)Value(0)));
                writer.Write(checked((short)Value(1)));
                break;
            case PayloadKind.Light:
                writer.Write(checked((ushort)Value(0)));
                writer.Write(checked((ushort)Value(1)));
                break;
            case PayloadKind.Market:
                var nameBytes = Encoding.Latin1.GetBytes(field.Text ?? string.Empty);
                if (nameBytes.Length > ushort.MaxValue) throw new InvalidDataException("The market name is too long for the DAT format.");
                writer.Write(checked((ushort)Value(0)));
                writer.Write(checked((ushort)Value(1)));
                writer.Write(checked((ushort)Value(2)));
                writer.Write((ushort)nameBytes.Length);
                writer.Write(nameBytes);
                writer.Write(checked((ushort)Value(3)));
                writer.Write(checked((ushort)Value(4)));
                break;
            case PayloadKind.Bones:
                for (var index = 0; index < 8; index++) writer.Write(checked((short)Value(index)));
                break;
            default:
                throw new InvalidDataException("Unknown DAT property payload type.");
        }
        return stream.ToArray();
    }

    private static IEnumerable<DatRawProperty> Parse(byte[] raw, int readerVersion)
    {
        using var stream = new MemoryStream(raw, writable: false);
        using var reader = new BinaryReader(stream);
        for (var count = 0; count < 256 && stream.Position < stream.Length; count++)
        {
            var flag = reader.ReadByte();
            if (flag == 0xFF) yield break;
            var payloadStart = stream.Position;
            var remainingBytes = DatFileParser.PropertyPayloadBytes(flag, readerVersion, reader);
            var payloadLength = checked((int)(stream.Position - payloadStart) + remainingBytes);
            stream.Position = payloadStart;
            var payload = reader.ReadBytes(payloadLength);
            if (payload.Length != payloadLength) throw new InvalidDataException("A DAT property is incomplete.");
            yield return new DatRawProperty(flag, payload);
        }
        throw new InvalidDataException("The DAT property list is invalid.");
    }

    private static byte[] Serialize(IEnumerable<DatRawProperty> properties)
    {
        using var stream = new MemoryStream();
        foreach (var property in properties.OrderBy(property => property.Flag))
        {
            if (property.Flag == 0xFF) continue;
            stream.WriteByte(property.Flag);
            stream.Write(property.Payload);
        }
        stream.WriteByte(0xFF);
        return stream.ToArray();
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> payload, int offset)
    {
        if (offset < 0 || offset + 2 > payload.Length) throw new InvalidDataException("A DAT property payload is incomplete.");
        return BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, 2));
    }

    private static short ReadInt16(ReadOnlySpan<byte> payload, int offset)
    {
        if (offset < 0 || offset + 2 > payload.Length) throw new InvalidDataException("A DAT property payload is incomplete.");
        return BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(offset, 2));
    }

    private static Dictionary<string, Descriptor> Descriptors(int version)
    {
        var result = new Dictionary<string, Descriptor>(StringComparer.Ordinal);
        void Add(string key, byte flag, PayloadKind kind = PayloadKind.Flag, params int[] defaults) =>
            result.Add(key, new Descriptor(key, flag, kind, defaults));
        void U16(string key, byte flag, int value = 0) => Add(key, flag, PayloadKind.UInt16, value);
        void Flags(params (string Key, byte Flag)[] flags)
        {
            foreach (var (key, flag) in flags) Add(key, flag);
        }

        U16("ground", 0x00);
        if (version <= 2)
        {
            Flags(("onBottom", 0x01), ("onTop", 0x02), ("container", 0x03), ("stackable", 0x04),
                ("multiUse", 0x05), ("forceUse", 0x06));
            U16("writable", 0x07); U16("writableOnce", 0x08);
            Flags(("fluidContainer", 0x09), ("fluid", 0x0A), ("unpassable", 0x0B), ("unmovable", 0x0C),
                ("blockMissile", 0x0D), ("blockPathfinder", 0x0E), ("pickupable", 0x0F));
            Add("light", 0x10, PayloadKind.Light, 0, 0);
            Flags(("floorChange", 0x11), ("fullGround", 0x12));
            U16("elevation", 0x13);
            Add("offset", 0x14, PayloadKind.Flag, 8, 8);
            U16("automap", 0x16);
            if (version == 1)
            {
                Flags(("rotatable", 0x17), ("lyingObject", 0x18), ("animateAlways", 0x19));
                U16("lensHelp", 0x1A, 1100);
                Flags(("wrappable", 0x24), ("unwrappable", 0x25), ("topEffect", 0x26));
            }
            else
            {
                Flags(("rotatable", 0x17), ("lyingObject", 0x18), ("hangable", 0x19),
                    ("vertical", 0x1A), ("horizontal", 0x1B), ("animateAlways", 0x1C));
                U16("lensHelp", 0x1D, 1100);
                Flags(("wrappable", 0x24), ("unwrappable", 0x25), ("topEffect", 0x26));
            }
            return result;
        }

        if (version == 3)
        {
            Flags(("groundBorder", 0x01), ("onBottom", 0x02), ("onTop", 0x03), ("container", 0x04),
                ("stackable", 0x05), ("forceUse", 0x06), ("multiUse", 0x07));
            U16("writable", 0x08); U16("writableOnce", 0x09);
            Flags(("fluidContainer", 0x0A), ("fluid", 0x0B), ("unpassable", 0x0C), ("unmovable", 0x0D),
                ("blockMissile", 0x0E), ("blockPathfinder", 0x0F), ("pickupable", 0x10),
                ("hangable", 0x11), ("vertical", 0x12), ("horizontal", 0x13), ("rotatable", 0x14));
            Add("light", 0x15, PayloadKind.Light, 0, 0);
            Flags(("floorChange", 0x17));
            Add("offset", 0x18, PayloadKind.SignedPair, 0, 0);
            U16("elevation", 0x19);
            Flags(("lyingObject", 0x1A), ("animateAlways", 0x1B));
            U16("automap", 0x1C); U16("lensHelp", 0x1D, 1100);
            Flags(("fullGround", 0x1E));
            return result;
        }

        if (version == 4)
        {
            Flags(("groundBorder", 0x01), ("onBottom", 0x02), ("onTop", 0x03), ("container", 0x04),
                ("stackable", 0x05), ("forceUse", 0x06), ("multiUse", 0x07), ("hasCharges", 0x08));
            U16("writable", 0x09); U16("writableOnce", 0x0A);
            Flags(("fluidContainer", 0x0B), ("fluid", 0x0C), ("unpassable", 0x0D), ("unmovable", 0x0E),
                ("blockMissile", 0x0F), ("blockPathfinder", 0x10), ("pickupable", 0x11), ("hangable", 0x12),
                ("vertical", 0x13), ("horizontal", 0x14), ("rotatable", 0x15));
            Add("light", 0x16, PayloadKind.Light, 0, 0);
            Flags(("dontHide", 0x17), ("floorChange", 0x18));
            Add("offset", 0x19, PayloadKind.SignedPair, 0, 0);
            U16("elevation", 0x1A);
            Flags(("lyingObject", 0x1B), ("animateAlways", 0x1C));
            U16("automap", 0x1D); U16("lensHelp", 0x1E, 1100);
            Flags(("fullGround", 0x1F), ("ignoreLook", 0x20), ("wrappable", 0x24), ("unwrappable", 0x25));
            Add("bones", 0x27, PayloadKind.Bones, new int[8]);
            return result;
        }

        if (version == 5)
        {
            Flags(("groundBorder", 0x01), ("onBottom", 0x02), ("onTop", 0x03), ("container", 0x04),
                ("stackable", 0x05), ("forceUse", 0x06), ("multiUse", 0x07));
            U16("writable", 0x08); U16("writableOnce", 0x09);
            Flags(("fluidContainer", 0x0A), ("fluid", 0x0B), ("unpassable", 0x0C), ("unmovable", 0x0D),
                ("blockMissile", 0x0E), ("blockPathfinder", 0x0F), ("pickupable", 0x10), ("hangable", 0x11),
                ("vertical", 0x12), ("horizontal", 0x13), ("rotatable", 0x14));
            Add("light", 0x15, PayloadKind.Light, 0, 0);
            Flags(("dontHide", 0x16), ("translucent", 0x17));
            Add("offset", 0x18, PayloadKind.SignedPair, 0, 0);
            U16("elevation", 0x19);
            Flags(("lyingObject", 0x1A), ("animateAlways", 0x1B));
            U16("automap", 0x1C); U16("lensHelp", 0x1D, 1100);
            Flags(("fullGround", 0x1E), ("ignoreLook", 0x1F));
            U16("cloth", 0x20);
            Add("market", 0x21, PayloadKind.Market, 0, 0, 0, 0, 0);
            Add("bones", 0x27, PayloadKind.Bones, new int[8]);
            return result;
        }

        Flags(("groundBorder", 0x01), ("onBottom", 0x02), ("onTop", 0x03), ("container", 0x04),
            ("stackable", 0x05), ("forceUse", 0x06), ("multiUse", 0x07));
        U16("writable", 0x08); U16("writableOnce", 0x09);
        Flags(("fluidContainer", 0x0A), ("fluid", 0x0B), ("unpassable", 0x0C), ("unmovable", 0x0D),
            ("blockMissile", 0x0E), ("blockPathfinder", 0x0F), ("noMoveAnimation", 0x10), ("pickupable", 0x11),
            ("hangable", 0x12), ("vertical", 0x13), ("horizontal", 0x14), ("rotatable", 0x15));
        Add("light", 0x16, PayloadKind.Light, 0, 0);
        Flags(("dontHide", 0x17), ("translucent", 0x18));
        Add("offset", 0x19, PayloadKind.SignedPair, 0, 0);
        U16("elevation", 0x1A);
        Flags(("lyingObject", 0x1B), ("animateAlways", 0x1C));
        U16("automap", 0x1D); U16("lensHelp", 0x1E, 1100);
        Flags(("fullGround", 0x1F), ("ignoreLook", 0x20));
        U16("cloth", 0x21);
        Add("market", 0x22, PayloadKind.Market, 0, 0, 0, 0, 0);
        U16("defaultAction", 0x23);
        Flags(("wrappable", 0x24), ("unwrappable", 0x25), ("topEffect", 0x26));
        Add("bones", 0x27, PayloadKind.Bones, new int[8]);
        Flags(("usable", 0xFE));
        return result;
    }
}
