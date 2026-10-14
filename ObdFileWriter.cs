using SharpCompress.Compressors.LZMA;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SpriteStudio;

internal static class ObdFileWriter
{
    private const int SpriteByteCount = 32 * 32 * 4;
    private const int MaximumSpriteCount = 4096;

    public static byte[] Serialize(DatThing thing, int sourceVersion, int clientVersion, int obdVersion,
        IReadOnlyDictionary<uint, byte[]> spritePixels)
    {
        if (obdVersion is < 1 or > 3) throw new InvalidDataException("A versão OBD selecionada não é válida.");
        if (clientVersion is < 0 or > ushort.MaxValue) throw new InvalidDataException("A versão do cliente não cabe no formato OBD.");
        IReadOnlyList<DatFrameGroup> groups = obdVersion == 3 && thing.Category == "outfit"
            ? thing.FrameGroups
            : thing.FrameGroups.Take(1).ToArray();
        if (groups.Count == 0) throw new InvalidDataException($"O objeto #{thing.Id} não tem grupo de quadros.");
        if (obdVersion == 3 && thing.Category == "outfit" && groups.Count > byte.MaxValue)
            throw new InvalidDataException("O objeto tem grupos demais para o formato OBD.");

        using var rawStream = new MemoryStream();
        using (var writer = new BinaryWriter(rawStream, Encoding.UTF8, leaveOpen: true))
        {
            if (obdVersion == 1)
            {
                writer.Write(checked((ushort)clientVersion));
                var categoryBytes = Encoding.UTF8.GetBytes(thing.Category);
                writer.Write(checked((ushort)categoryBytes.Length));
                writer.Write(categoryBytes);
                writer.Write(DatPropertiesCodec.ConvertToClientVersion(thing.RawProperties, sourceVersion, clientVersion));
            }
            else
            {
                writer.Write(checked((ushort)(obdVersion * 100)));
                writer.Write(checked((ushort)clientVersion));
                writer.Write(CategoryValue(thing.Category));
                var texturePatternsPosition = rawStream.Position;
                writer.Write(0u);
                WriteProperties(writer, thing, sourceVersion);
                var patternDataPosition = checked((uint)rawStream.Position);
                var end = rawStream.Position;
                rawStream.Position = texturePatternsPosition;
                writer.Write(patternDataPosition);
                rawStream.Position = end;
            }

            if (obdVersion == 3 && thing.Category == "outfit") writer.Write(checked((byte)groups.Count));
            foreach (var group in groups)
            {
                if (obdVersion == 3 && thing.Category == "outfit")
                    writer.Write(groups.Count < 2 ? (byte)1 : group.Type);
                WriteFrameGroup(writer, thing, group, obdVersion, spritePixels);
            }
            writer.Flush();
        }
        return CompressLzma(rawStream.ToArray());
    }

    private static void WriteFrameGroup(BinaryWriter writer, DatThing thing, DatFrameGroup group, int obdVersion,
        IReadOnlyDictionary<uint, byte[]> spritePixels)
    {
        WriteByte(writer, group.Width, "largura");
        WriteByte(writer, group.Height, "altura");
        if (group.Width > 1 || group.Height > 1) WriteByte(writer, group.CropSize, "recorte", allowZero: true);
        WriteByte(writer, group.Layers, "camadas");
        WriteByte(writer, group.PatternX, "padrão X");
        WriteByte(writer, group.PatternY, "padrão Y");
        WriteByte(writer, group.PatternZ, "padrão Z");
        WriteByte(writer, group.Frames.Count, "quadros de animação");

        if (group.Frames.Count > 1 && obdVersion >= 2)
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

        var spriteCount = group.Frames.Sum(frame => (long)frame.SpriteIds.Length);
        if (spriteCount > MaximumSpriteCount)
            throw new InvalidDataException($"O objeto #{thing.Id} tem {spriteCount:N0} sprites; o formato OBD aceita até {MaximumSpriteCount:N0} por objeto.");

        foreach (var spriteId in group.Frames.SelectMany(frame => frame.SpriteIds))
        {
            if (!spritePixels.TryGetValue(spriteId, out var pixels) || pixels.Length != SpriteByteCount)
                throw new InvalidDataException($"Não foi possível ler o sprite {spriteId} do objeto #{thing.Id}.");
            writer.Write(spriteId);
            if (obdVersion != 2) writer.Write(checked((uint)pixels.Length));
            writer.Write(pixels);
        }
    }

    private static void WriteProperties(BinaryWriter writer, DatThing thing, int sourceVersion)
    {
        var fields = DatPropertiesCodec.Read(thing.RawProperties, sourceVersion);
        DatPropertyField Get(string key) => DatPropertiesCodec.Get(fields, key);
        bool Has(string key) => Get(key).Enabled;
        int Value(string key, int index = 0)
        {
            var values = Get(key).Values;
            return index < values.Length ? values[index] : 0;
        }
        void Flag(byte id, string key)
        {
            if (Has(key)) writer.Write(id);
        }
        void U16(byte id, string key, int index = 0)
        {
            if (!Has(key)) return;
            writer.Write(id);
            writer.Write(checked((ushort)Value(key, index)));
        }

        if (Has("ground")) U16(0x00, "ground");
        else if (Has("groundBorder")) Flag(0x01, "groundBorder");
        else if (Has("onBottom")) Flag(0x02, "onBottom");
        else if (Has("onTop")) Flag(0x03, "onTop");
        Flag(0x04, "container");
        Flag(0x05, "stackable");
        Flag(0x06, "forceUse");
        Flag(0x07, "multiUse");
        U16(0x08, "writable");
        U16(0x09, "writableOnce");
        Flag(0x0A, "fluidContainer");
        Flag(0x0B, "fluid");
        Flag(0x0C, "unpassable");
        Flag(0x0D, "unmovable");
        Flag(0x0E, "blockMissile");
        Flag(0x0F, "blockPathfinder");
        Flag(0x10, "noMoveAnimation");
        Flag(0x11, "pickupable");
        Flag(0x12, "hangable");
        Flag(0x13, "vertical");
        Flag(0x14, "horizontal");
        Flag(0x15, "rotatable");
        if (Has("light"))
        {
            writer.Write((byte)0x16);
            writer.Write(checked((ushort)Value("light")));
            writer.Write(checked((ushort)Value("light", 1)));
        }
        Flag(0x17, "dontHide");
        Flag(0x18, "translucent");
        if (Has("offset"))
        {
            writer.Write((byte)0x19);
            writer.Write(checked((short)Value("offset")));
            writer.Write(checked((short)Value("offset", 1)));
        }
        U16(0x1A, "elevation");
        Flag(0x1B, "lyingObject");
        Flag(0x1C, "animateAlways");
        U16(0x1D, "automap");
        U16(0x1E, "lensHelp");
        Flag(0x1F, "fullGround");
        Flag(0x20, "ignoreLook");
        U16(0x21, "cloth");
        if (Has("market"))
        {
            var market = Get("market");
            var name = Encoding.Latin1.GetBytes(market.Text ?? string.Empty);
            if (name.Length > ushort.MaxValue) throw new InvalidDataException("O nome do mercado é longo demais para o formato OBD.");
            writer.Write((byte)0x22);
            writer.Write(checked((ushort)Value("market")));
            writer.Write(checked((ushort)Value("market", 1)));
            writer.Write(checked((ushort)Value("market", 2)));
            writer.Write(checked((ushort)name.Length));
            writer.Write(name);
            writer.Write(checked((ushort)Value("market", 3)));
            writer.Write(checked((ushort)Value("market", 4)));
        }
        U16(0x23, "defaultAction");
        Flag(0x24, "wrappable");
        Flag(0x25, "unwrappable");
        Flag(0x26, "topEffect");
        Flag(0xFC, "hasCharges");
        Flag(0xFD, "floorChange");
        Flag(0xFE, "usable");
        writer.Write((byte)0xFF);
    }

    private static byte CategoryValue(string category) => category switch
    {
        "item" => 1, "outfit" => 2, "effect" => 3, "missile" => 4,
        _ => throw new InvalidDataException("A categoria deste objeto não é válida para exportação OBD.")
    };

    private static void WriteByte(BinaryWriter writer, int value, string field, bool allowZero = false)
    {
        if (value < (allowZero ? 0 : 1) || value > byte.MaxValue) throw new InvalidDataException($"O campo {field} não cabe no formato OBD.");
        writer.Write((byte)value);
    }

    private static byte[] CompressLzma(byte[] source)
    {
        using var compressed = new MemoryStream();
        var encoderType = typeof(LzmaStream).Assembly.GetType("SharpCompress.Compressors.LZMA.Encoder")
            ?? throw new InvalidOperationException("O codificador LZMA não está disponível.");
        var encoder = Activator.CreateInstance(encoderType)
            ?? throw new InvalidOperationException("Não foi possível iniciar o codificador LZMA.");
        using var input = new MemoryStream(source, writable: false);
        using var payload = new MemoryStream();
        try
        {
            var writeProperties = encoderType.GetMethod("WriteCoderProperties", [typeof(Stream)])
                ?? throw new InvalidOperationException("As propriedades do codificador LZMA não estão disponíveis.");
            writeProperties.Invoke(encoder, [compressed]);
            var progressType = typeof(LzmaStream).Assembly.GetType("SharpCompress.Compressors.LZMA.ICodeProgress")!;
            var codeMethod = encoderType.GetMethod("Code", [typeof(Stream), typeof(Stream), typeof(long), typeof(long), progressType])
                ?? throw new InvalidOperationException("O método de compressão LZMA não está disponível.");
            codeMethod.Invoke(encoder, [input, payload, input.Length, -1L, null]);
        }
        finally
        {
            if (encoder is IDisposable disposable) disposable.Dispose();
        }
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(length, source.LongLength);
        compressed.Write(length);
        payload.Position = 0;
        payload.CopyTo(compressed);
        return compressed.ToArray();
    }
}
