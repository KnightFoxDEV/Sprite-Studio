using SharpCompress.Compressors.LZMA;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Text;

namespace SpriteStudio;

internal sealed record ObdObjectData(DatThing Thing, IReadOnlyDictionary<uint, byte[]> SpritePixels);

internal static class ObdFileReader
{
    private const int SpritePixelBytes = 32 * 32 * 4;
    private const int MaximumFileBytes = 256 * 1024 * 1024;
    private const int MaximumObjectBytes = 256 * 1024 * 1024;
    private const int MaximumSpriteReferences = 1_000_000;

    public static ObdObjectData Read(string path, int targetClientVersion)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("O arquivo OBD não foi encontrado.", path);
        if (file.Length is < 13 or > MaximumFileBytes)
            throw new InvalidDataException("O tamanho do arquivo OBD é inválido ou excede 256 MB.");

        var compressed = File.ReadAllBytes(path);
        var raw = DecompressLzma(compressed);
        using var stream = new MemoryStream(raw, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var headerVersion = ReadUInt16(reader);
        var obdVersion = 0;
        var clientVersion = 0;
        string category;
        byte[] properties;
        if (headerVersion >= 710)
        {
            obdVersion = 100;
            clientVersion = headerVersion;
            var categoryLength = ReadUInt16(reader);
            if (categoryLength == 0 || categoryLength > 32) throw new InvalidDataException("A categoria do OBD v1 é inválida.");
            category = Encoding.UTF8.GetString(ReadExactly(reader, categoryLength));
            if (category is not ("item" or "outfit" or "effect" or "missile"))
                throw new InvalidDataException("O arquivo OBD contém uma categoria desconhecida.");
            properties = ReadLegacyProperties(reader, clientVersion, targetClientVersion);
        }
        else if (headerVersion is 200 or 300)
        {
            obdVersion = headerVersion;
            clientVersion = ReadUInt16(reader);
            category = ReadCategory(ReadByte(reader));
            _ = ReadUInt32(reader); // Texture pattern offset.
            properties = ReadCanonicalProperties(reader, targetClientVersion);
        }
        else
        {
            throw new InvalidDataException($"Versão OBD {headerVersion} não é compatível.");
        }

        var spritePixels = new Dictionary<uint, byte[]>();
        var groups = new List<DatFrameGroup>();
        var groupCount = obdVersion == 300 && category == "outfit" ? ReadByte(reader) : 1;
        if (groupCount is < 1 or > byte.MaxValue) throw new InvalidDataException("A quantidade de grupos de quadros no OBD é inválida.");

        long totalReferences = 0;
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupType = obdVersion == 300 && category == "outfit" ? ReadByte(reader) : (byte)0;
            var width = ReadByte(reader);
            var height = ReadByte(reader);
            var cropSize = width > 1 || height > 1 ? ReadByte(reader) : 32;
            var layers = ReadByte(reader);
            var patternX = ReadByte(reader);
            var patternY = ReadByte(reader);
            var patternZ = ReadByte(reader);
            var frameCount = ReadByte(reader);
            if (width == 0 || height == 0 || layers == 0 || patternX == 0 || patternY == 0 || patternZ == 0 || frameCount == 0)
                throw new InvalidDataException("O OBD contém dimensões de quadro inválidas.");

            byte animationMode = 0;
            var loopCount = 0;
            sbyte startFrame = 0;
            var minimumDurations = Enumerable.Repeat(100u, frameCount).ToArray();
            var maximumDurations = Enumerable.Repeat(100u, frameCount).ToArray();
            if (frameCount > 1 && obdVersion >= 200)
            {
                animationMode = ReadByte(reader);
                loopCount = ReadInt32(reader);
                startFrame = ReadSByte(reader);
                for (var frame = 0; frame < frameCount; frame++)
                {
                    minimumDurations[frame] = ReadUInt32(reader);
                    maximumDurations[frame] = ReadUInt32(reader);
                }
            }

            var spritesPerFrame64 = (long)width * height * layers * patternX * patternY * patternZ;
            var groupReferences = spritesPerFrame64 * frameCount;
            totalReferences += groupReferences;
            if (spritesPerFrame64 > MaximumSpriteReferences || totalReferences > MaximumSpriteReferences)
                throw new InvalidDataException("O OBD contém referências demais de sprites.");
            var spritesPerFrame = checked((int)spritesPerFrame64);
            var frames = new List<DatAnimationFrame>(frameCount);
            for (var frame = 0; frame < frameCount; frame++)
            {
                var ids = new uint[spritesPerFrame];
                for (var index = 0; index < spritesPerFrame; index++)
                {
                    var spriteId = ReadUInt32(reader);
                    var dataLength = obdVersion == 200 ? SpritePixelBytes : ReadUInt32(reader);
                    if (dataLength != SpritePixelBytes)
                        throw new InvalidDataException($"O sprite {spriteId} no OBD não tem 32 × 32 pixels ARGB.");
                    var pixels = ReadExactly(reader, checked((int)dataLength));
                    ids[index] = spriteId;
                    if (spritePixels.TryGetValue(spriteId, out var priorPixels))
                    {
                        if (!priorPixels.AsSpan().SequenceEqual(pixels))
                            throw new InvalidDataException($"O sprite {spriteId} aparece com dados diferentes dentro do mesmo OBD.");
                    }
                    else
                    {
                        spritePixels.Add(spriteId, pixels);
                    }
                }
                frames.Add(new DatAnimationFrame(ids, minimumDurations[frame], maximumDurations[frame]));
            }

            groups.Add(new DatFrameGroup(groupType, width, height, cropSize, layers, patternX, patternY, patternZ,
                animationMode, loopCount, startFrame, frames));
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException("O arquivo OBD contém dados extras ou está corrompido.");
        if (clientVersion is < 700 or > ushort.MaxValue)
            throw new InvalidDataException("A versão do cliente registrada no OBD é inválida.");

        var thing = new DatThing(0, category, groups,
            checked((int)totalReferences), properties);
        return new ObdObjectData(thing, spritePixels);
    }

    private static byte[] ReadLegacyProperties(BinaryReader reader, int sourceVersion, int targetVersion)
    {
        if (sourceVersion is < 700 or > ushort.MaxValue) throw new InvalidDataException("A versão do cliente no OBD v1 é inválida.");
        var readerVersion = DatPropertiesCodec.ReaderVersion(sourceVersion);
        var stream = reader.BaseStream;
        var start = stream.Position;
        for (var guard = 0; guard < 256; guard++)
        {
            var flag = ReadByte(reader);
            if (flag == 0xFF)
            {
                var length = checked((int)(stream.Position - start));
                stream.Position = start;
                var raw = ReadExactly(reader, length);
                return DatPropertiesCodec.ConvertToClientVersion(raw, sourceVersion, targetVersion);
            }

            var payloadStart = stream.Position;
            var remaining = DatFileParser.PropertyPayloadBytes(flag, readerVersion, reader);
            var payloadLength = checked((int)(stream.Position - payloadStart) + remaining);
            if (payloadLength < 0 || stream.Position + remaining > stream.Length)
                throw new InvalidDataException("A lista de propriedades do OBD v1 está truncada.");
            stream.Position = checked(payloadStart + payloadLength);
        }
        throw new InvalidDataException("A lista de propriedades do OBD v1 é inválida.");
    }

    private static byte[] ReadCanonicalProperties(BinaryReader reader, int targetVersion)
    {
        byte[] raw = [0xFF];
        for (var guard = 0; guard < 256; guard++)
        {
            var flag = ReadByte(reader);
            if (flag == 0xFF) return raw;

            string key;
            DatPropertyField value;
            void Flag(string name) { key = name; value = new DatPropertyField(true, [], string.Empty); }
            void Number(string name, int number) { key = name; value = new DatPropertyField(true, [number], string.Empty); }

            switch (flag)
            {
                case 0x00: Number("ground", ReadUInt16(reader)); break;
                case 0x01: Flag("groundBorder"); break;
                case 0x02: Flag("onBottom"); break;
                case 0x03: Flag("onTop"); break;
                case 0x04: Flag("container"); break;
                case 0x05: Flag("stackable"); break;
                case 0x06: Flag("forceUse"); break;
                case 0x07: Flag("multiUse"); break;
                case 0x08: Number("writable", ReadUInt16(reader)); break;
                case 0x09: Number("writableOnce", ReadUInt16(reader)); break;
                case 0x0A: Flag("fluidContainer"); break;
                case 0x0B: Flag("fluid"); break;
                case 0x0C: Flag("unpassable"); break;
                case 0x0D: Flag("unmovable"); break;
                case 0x0E: Flag("blockMissile"); break;
                case 0x0F: Flag("blockPathfinder"); break;
                case 0x10: Flag("noMoveAnimation"); break;
                case 0x11: Flag("pickupable"); break;
                case 0x12: Flag("hangable"); break;
                case 0x13: Flag("vertical"); break;
                case 0x14: Flag("horizontal"); break;
                case 0x15: Flag("rotatable"); break;
                case 0x16: key = "light"; value = new DatPropertyField(true, [ReadUInt16(reader), ReadUInt16(reader)], string.Empty); break;
                case 0x17: Flag("dontHide"); break;
                case 0x18: Flag("translucent"); break;
                case 0x19: key = "offset"; value = new DatPropertyField(true, [ReadInt16(reader), ReadInt16(reader)], string.Empty); break;
                case 0x1A: Number("elevation", ReadUInt16(reader)); break;
                case 0x1B: Flag("lyingObject"); break;
                case 0x1C: Flag("animateAlways"); break;
                case 0x1D: Number("automap", ReadUInt16(reader)); break;
                case 0x1E: Number("lensHelp", ReadUInt16(reader)); break;
                case 0x1F: Flag("fullGround"); break;
                case 0x20: Flag("ignoreLook"); break;
                case 0x21: Number("cloth", ReadUInt16(reader)); break;
                case 0x22:
                    var marketCategory = ReadUInt16(reader);
                    var tradeAs = ReadUInt16(reader);
                    var showAs = ReadUInt16(reader);
                    var nameLength = ReadUInt16(reader);
                    var name = Encoding.Latin1.GetString(ReadExactly(reader, nameLength));
                    var profession = ReadUInt16(reader);
                    var level = ReadUInt16(reader);
                    key = "market";
                    value = new DatPropertyField(true, [marketCategory, tradeAs, showAs, profession, level], name);
                    break;
                case 0x23: Number("defaultAction", ReadUInt16(reader)); break;
                case 0x24: Flag("wrappable"); break;
                case 0x25: Flag("unwrappable"); break;
                case 0x26: Flag("topEffect"); break;
                case 0xFC: Flag("hasCharges"); break;
                case 0xFD: Flag("floorChange"); break;
                case 0xFE: Flag("usable"); break;
                default: throw new InvalidDataException($"A propriedade OBD 0x{flag:X2} não é reconhecida.");
            }

            if (!DatPropertiesCodec.Supports(targetVersion, key))
                throw new InvalidDataException($"A propriedade '{key}' do OBD não existe na versão {targetVersion} do DAT aberto.");
            raw = DatPropertiesCodec.Set(raw, targetVersion, key, value);
        }
        throw new InvalidDataException("A lista de propriedades do OBD é inválida.");
    }

    private static string ReadCategory(byte value) => value switch
    {
        1 => "item", 2 => "outfit", 3 => "effect", 4 => "missile",
        _ => throw new InvalidDataException("O arquivo OBD contém uma categoria desconhecida.")
    };

    private static byte[] DecompressLzma(byte[] data)
    {
        if (data.Length < 13) throw new InvalidDataException("O arquivo OBD não contém um cabeçalho LZMA válido.");
        var expectedLength = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(5, 8));
        if (expectedLength is < 0 or > MaximumObjectBytes)
            throw new InvalidDataException("O conteúdo descompactado do OBD excede 256 MB.");

        var assembly = typeof(LzmaStream).Assembly;
        var decoderType = assembly.GetType("SharpCompress.Compressors.LZMA.Decoder")
            ?? throw new InvalidOperationException("O decodificador LZMA não está disponível.");
        var progressType = assembly.GetType("SharpCompress.Compressors.LZMA.ICodeProgress")
            ?? throw new InvalidOperationException("A interface de progresso LZMA não está disponível.");
        var decoder = Activator.CreateInstance(decoderType)
            ?? throw new InvalidOperationException("Não foi possível iniciar o decodificador LZMA.");
        using var input = new MemoryStream(data, 13, data.Length - 13, writable: false);
        using var output = new MemoryStream(checked((int)expectedLength));
        try
        {
            var setProperties = decoderType.GetMethod("SetDecoderProperties", [typeof(byte[])])
                ?? throw new InvalidOperationException("As propriedades do decodificador LZMA não estão disponíveis.");
            setProperties.Invoke(decoder, [data.AsSpan(0, 5).ToArray()]);
            var code = decoderType.GetMethod("Code", [typeof(Stream), typeof(Stream), typeof(long), typeof(long), progressType])
                ?? throw new InvalidOperationException("O método de descompactação LZMA não está disponível.");
            code.Invoke(decoder, [input, output, input.Length, expectedLength, null]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidDataException("Não foi possível descompactar os dados do OBD.", ex.InnerException);
        }
        finally
        {
            if (decoder is IDisposable disposable) disposable.Dispose();
        }

        if (output.Length != expectedLength) throw new InvalidDataException("O tamanho descompactado do OBD não corresponde ao cabeçalho.");
        return output.ToArray();
    }

    private static byte ReadByte(BinaryReader reader) { try { return reader.ReadByte(); } catch (EndOfStreamException) { throw new InvalidDataException("O arquivo OBD terminou antes do esperado."); } }
    private static sbyte ReadSByte(BinaryReader reader) { try { return reader.ReadSByte(); } catch (EndOfStreamException) { throw new InvalidDataException("O arquivo OBD terminou antes do esperado."); } }
    private static ushort ReadUInt16(BinaryReader reader) { try { return reader.ReadUInt16(); } catch (EndOfStreamException) { throw new InvalidDataException("O arquivo OBD terminou antes do esperado."); } }
    private static short ReadInt16(BinaryReader reader) { try { return reader.ReadInt16(); } catch (EndOfStreamException) { throw new InvalidDataException("O arquivo OBD terminou antes do esperado."); } }
    private static int ReadInt32(BinaryReader reader) { try { return reader.ReadInt32(); } catch (EndOfStreamException) { throw new InvalidDataException("O arquivo OBD terminou antes do esperado."); } }
    private static uint ReadUInt32(BinaryReader reader) { try { return reader.ReadUInt32(); } catch (EndOfStreamException) { throw new InvalidDataException("O arquivo OBD terminou antes do esperado."); } }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        if (count < 0 || count > MaximumObjectBytes || reader.BaseStream.Position + count > reader.BaseStream.Length)
            throw new InvalidDataException("O arquivo OBD terminou antes do esperado.");
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("O arquivo OBD terminou antes do esperado.");
        return bytes;
    }
}
