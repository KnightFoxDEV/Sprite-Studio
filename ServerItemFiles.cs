using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SpriteStudio;

internal sealed class ServerItemFiles
{
    private const byte NodeStart = 0xFE;
    private const byte NodeEnd = 0xFF;
    private const byte Escape = 0xFD;
    private const byte ItemAttrServerId32 = 0xF0;
    private const byte ItemAttrClientId32 = 0xF1;
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly byte[] _fileHeader;
    private readonly OtbNode _root;
    private readonly List<OtbItem> _items;
    private readonly Dictionary<uint, OtbItem> _itemsByClientId;
    private XDocument? _xml;

    private ServerItemFiles(string otbPath, string? xmlPath, byte[] fileHeader, OtbNode root, List<OtbItem> items, XDocument? xml)
    {
        OtbPath = otbPath;
        XmlPath = xmlPath;
        _fileHeader = fileHeader;
        _root = root;
        _items = items;
        _itemsByClientId = items.GroupBy(item => item.ClientId).ToDictionary(group => group.Key, group => group.Last());
        _xml = xml;
    }

    public string OtbPath { get; }
    public string? XmlPath { get; }
    public int ItemCount => _items.Count;
    public bool HasXml => _xml is not null;
    public bool IsDirty { get; private set; }

    public static ServerItemFiles Load(string otbPath, string? xmlPath = null)
    {
        var bytes = File.ReadAllBytes(otbPath);
        if (bytes.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0)
            throw new InvalidDataException("O arquivo items.otb não tem um cabeçalho válido.");

        var offset = 4;
        var root = ReadNode(bytes, ref offset);
        if (root is null || root.Type != 0)
            throw new InvalidDataException("Não foi possível ler o nó principal do items.otb.");
        if (offset != bytes.Length)
            throw new InvalidDataException("O items.otb contém dados inválidos após o nó principal.");
        ValidateRootVersion(root);

        var items = root.Children.Select(node => new OtbItem(node)).ToList();
        XDocument? xml = null;
        if (xmlPath is not null && File.Exists(xmlPath))
        {
            using var stream = File.OpenRead(xmlPath);
            xml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            if (xml.Root is null) throw new InvalidDataException("O items.xml não tem um elemento principal.");
        }
        else xmlPath = null;

        return new ServerItemFiles(otbPath, xmlPath, bytes[..4], root, items, xml);
    }

    public ServerItemInfo? FindByClientId(uint clientId)
    {
        var item = FindItem(clientId);
        return item?.GetInfo();
    }

    public XmlItemInfo GetXmlInfo(uint clientId)
    {
        if (_xml?.Root is null) return new XmlItemInfo("", "", "");
        var serverId = FindItem(clientId)?.ServerId;
        if (serverId is null) return new XmlItemInfo("", "", "");
        var element = FindXmlItem(serverId.Value);
        return new XmlItemInfo((string?)element?.Attribute("name") ?? "",
            (string?)element?.Attribute("article") ?? "", (string?)element?.Attribute("plural") ?? "");
    }

    public IReadOnlyList<XmlItemAttributeInfo> GetXmlAttributes(uint clientId)
    {
        if (_xml?.Root is null) return [];
        var item = FindItem(clientId);
        if (item is null) return [];
        var range = FindXmlRangeItem(item.ServerId);
        var exact = FindExactXmlItem(item.ServerId);
        return new[] { range, exact }.Where(element => element is not null)
            .SelectMany(element => element!.Elements().Where(child => child.Name.LocalName == "attribute"
                && child.Elements().Count() == 0 && !string.IsNullOrWhiteSpace((string?)child.Attribute("key")))
                .Select(child => new XmlItemAttributeInfo((string)child.Attribute("key")!, (string?)child.Attribute("value") ?? "")))
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool UpdateOtbField(uint clientId, string field, string value)
    {
        var item = FindItem(clientId);
        if (item is null) return false;
        switch (field)
        {
            case "otbName": item.SetName(value); break;
            case "groundSpeed": item.SetUInt16(0x14, ParseU16(value, field)); break;
            case "minimapColor": item.SetUInt16(0x21, ParseU16(value, field)); break;
            case "lightLevel":
            case "lightColor":
                var info = item.GetInfo();
                item.SetLight(field == "lightLevel" ? ParseU16(value, field) : info.LightLevel,
                    field == "lightColor" ? ParseU16(value, field) : info.LightColor);
                break;
            case "tradeAs": item.SetUInt16(0x2D, ParseU16(value, field)); break;
            default: return false;
        }
        IsDirty = true;
        return true;
    }

    public bool UpdateXmlField(uint clientId, string field, string value)
    {
        if (_xml?.Root is null || XmlPath is null) return false;
        var item = FindItem(clientId);
        if (item is null) return false;
        var element = GetOrCreateExactXmlItem(item.ServerId);
        if (field is not ("xmlName" or "article" or "plural")) return false;
        var attributeName = field == "xmlName" ? "name" : field;
        if (value.Length == 0) element.Attribute(attributeName)?.Remove();
        else element.SetAttributeValue(attributeName, value);
        IsDirty = true;
        return true;
    }

    public bool UpdateXmlAttribute(uint clientId, string key, string value)
    {
        if (_xml?.Root is null || XmlPath is null || string.IsNullOrWhiteSpace(key)) return false;
        var item = FindItem(clientId);
        if (item is null) return false;
        var element = GetOrCreateExactXmlItem(item.ServerId);
        var attribute = element.Elements().LastOrDefault(child => child.Name.LocalName == "attribute"
            && string.Equals((string?)child.Attribute("key"), key, StringComparison.OrdinalIgnoreCase));
        if (attribute is null)
        {
            attribute = new XElement(_xml.Root.Name.Namespace + "attribute", new XAttribute("key", key));
            element.Add(attribute);
        }
        attribute.SetAttributeValue("value", value);
        IsDirty = true;
        return true;
    }

    public void SynchronizeFromDat(DatThing thing, int clientVersion)
    {
        if (thing.Category != "item") return;
        var item = FindItem(thing.Id);
        if (item is null) return;

        var fields = DatPropertiesCodec.Read(thing.RawProperties, clientVersion);
        var changed = false;
        void Flag(string key, uint mask, bool invert = false)
        {
            if (!DatPropertiesCodec.Supports(clientVersion, key)) return;
            var field = DatPropertiesCodec.Get(fields, key);
            changed |= item.SetFlag(mask, invert ? !field.Enabled : field.Enabled);
        }
        Flag("unpassable", 1u << 0);
        Flag("blockMissile", 1u << 1);
        Flag("blockPathfinder", 1u << 2);
        var elevation = DatPropertiesCodec.Get(fields, "elevation");
        if (DatPropertiesCodec.Supports(clientVersion, "elevation"))
            changed |= item.SetFlag(1u << 3, elevation.Enabled && elevation.Values.FirstOrDefault() > 0);
        Flag("multiUse", 1u << 4);
        Flag("pickupable", 1u << 5);
        Flag("unmovable", 1u << 6, invert: true);
        Flag("stackable", 1u << 7);
        Flag("rotatable", 1u << 15);
        Flag("hangable", 1u << 16);
        Flag("horizontal", 1u << 17);
        Flag("vertical", 1u << 18);
        Flag("ignoreLook", 1u << 23);
        if (clientVersion >= 1010)
        {
            Flag("forceUse", 1u << 26);
            Flag("fullGround", 1u << 25);
        }
        else
        {
            changed |= item.SetFlag(1u << 26, false);
            changed |= item.SetFlag(1u << 25, false);
        }
        changed |= item.SetFlag(1u << 20, false);
        changed |= item.SetFlag(1u << 22, false);

        var writable = DatPropertiesCodec.Get(fields, "writable");
        var writableOnce = DatPropertiesCodec.Get(fields, "writableOnce");
        var lensHelp = DatPropertiesCodec.Get(fields, "lensHelp");
        if (DatPropertiesCodec.Supports(clientVersion, "writable") || DatPropertiesCodec.Supports(clientVersion, "writableOnce")
            || DatPropertiesCodec.Supports(clientVersion, "lensHelp"))
            changed |= item.SetFlag(1u << 14,
                writable.Enabled || writableOnce.Enabled || (lensHelp.Enabled && lensHelp.Values.FirstOrDefault() == 1112));

        var defaultGroup = thing.FrameGroups.FirstOrDefault(group => group.Type == 0) ?? thing.FrameGroups.FirstOrDefault();
        if (defaultGroup is not null) changed |= item.SetFlag(1u << 24, defaultGroup.Frames.Count > 1);

        var light = DatPropertiesCodec.Get(fields, "light");
        if (DatPropertiesCodec.Supports(clientVersion, "light"))
            changed |= item.SetLight(light.Enabled && light.Values.Length >= 2 ? U16(light.Values[0]) : (ushort)0,
                light.Enabled && light.Values.Length >= 2 ? U16(light.Values[1]) : (ushort)0);
        var ground = DatPropertiesCodec.Get(fields, "ground");
        if (item.Group == 1 && DatPropertiesCodec.Supports(clientVersion, "ground"))
            changed |= item.SetUInt16(0x14, ground.Enabled ? U16(ground.Values.FirstOrDefault()) : (ushort)0);
        var minimap = DatPropertiesCodec.Get(fields, "automap");
        if (DatPropertiesCodec.Supports(clientVersion, "automap"))
            changed |= item.SetUInt16(0x21, minimap.Enabled ? U16(minimap.Values.FirstOrDefault()) : (ushort)0);
        if (DatPropertiesCodec.Supports(clientVersion, "writable"))
            changed |= item.SetUInt16(0x22, writable.Enabled ? U16(writable.Values.FirstOrDefault()) : (ushort)0);
        if (DatPropertiesCodec.Supports(clientVersion, "writableOnce"))
            changed |= item.SetUInt16(0x23, writableOnce.Enabled ? U16(writableOnce.Values.FirstOrDefault()) : (ushort)0);

        var stackOrder = DatPropertiesCodec.Get(fields, "groundBorder").Enabled ? 1
            : DatPropertiesCodec.Get(fields, "onBottom").Enabled ? 2
            : DatPropertiesCodec.Get(fields, "onTop").Enabled ? 3 : 0;
        changed |= item.SetFlag(1u << 13, stackOrder != 0);
        changed |= item.SetByte(0x2B, stackOrder);

        var market = DatPropertiesCodec.Get(fields, "market");
        if (market.Enabled)
        {
            if (market.Text.Length > 0) changed |= item.SetName(market.Text);
            if (market.Values.Length > 1 && market.Values[1] != 0) changed |= item.SetUInt16(0x2D, U16(market.Values[1]));
        }
        if (changed) IsDirty = true;
    }

    public bool AddFromDat(DatThing thing, int clientVersion)
    {
        if (thing.Category != "item" || FindItem(thing.Id) is not null) return false;
        var fields = DatPropertiesCodec.Read(thing.RawProperties, clientVersion);
        var group = DatPropertiesCodec.Get(fields, "ground").Enabled ? (byte)1
            : DatPropertiesCodec.Get(fields, "container").Enabled ? (byte)2
            : DatPropertiesCodec.Get(fields, "fluidContainer").Enabled ? (byte)12
            : DatPropertiesCodec.Get(fields, "fluid").Enabled ? (byte)11 : (byte)0;
        var maxServerId = _items.Count == 0 ? 99u : _items.Max(item => item.ServerId);
        if (maxServerId == uint.MaxValue) throw new InvalidDataException("Não há mais IDs disponíveis no items.otb.");
        var newItem = OtbItem.CreateNew(group);
        var node = newItem.Node;
        newItem.SetUInt32(ItemAttrServerId32, maxServerId + 1);
        newItem.SetUInt32(ItemAttrClientId32, thing.Id);
        newItem.SetAttribute(0x20, new byte[16]);
        _root.Children.Add(node);
        _items.Add(newItem);
        _itemsByClientId[thing.Id] = newItem;
        IsDirty = true;
        SynchronizeFromDat(thing, clientVersion);
        return true;
    }

    public void ValidateCanAddFromDat(IEnumerable<DatThing> things, int clientVersion)
    {
        var missing = things.Where(thing => thing.Category == "item" && FindItem(thing.Id) is null).ToArray();
        foreach (var thing in missing)
        {
            _ = DatPropertiesCodec.Read(thing.RawProperties, clientVersion);
        }
        var maxServerId = _items.Count == 0 ? 99u : _items.Max(item => item.ServerId);
        if ((ulong)maxServerId + (ulong)missing.Length > uint.MaxValue)
            throw new InvalidDataException("Não há mais IDs disponíveis no items.otb para todos os itens importados.");
    }

    public bool RemoveByClientId(uint clientId)
    {
        var item = FindItem(clientId);
        if (item is null) return false;
        _items.Remove(item);
        _root.Children.Remove(item.Node);
        _itemsByClientId.Remove(clientId);
        if (_xml?.Root is not null)
            FindExactXmlItem(item.ServerId)?.Remove();
        IsDirty = true;
        return true;
    }

    public byte[] SerializeOtb()
    {
        using var stream = new MemoryStream();
        stream.Write(_fileHeader);
        WriteNode(stream, _root);
        return stream.ToArray();
    }

    public byte[]? SerializeXml()
    {
        if (_xml is null || XmlPath is null) return null;
        using var stream = new MemoryStream();
        if (_xml.Declaration is not null) _xml.Declaration.Encoding = "utf-8";
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = _xml.Declaration is null };
        using (var writer = XmlWriter.Create(stream, settings)) _xml.Save(writer);
        return stream.ToArray();
    }

    public void ClearDirty() => IsDirty = false;

    private OtbItem? FindItem(uint clientId) => _itemsByClientId.GetValueOrDefault(clientId);

    private XElement? FindXmlItem(uint serverId)
    {
        if (_xml?.Root is null) return null;
        var idText = serverId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var candidates = _xml.Root.DescendantsAndSelf().Where(element => element.Name.LocalName == "item");
        return candidates.LastOrDefault(element => (string?)element.Attribute("id") == idText)
            ?? candidates.LastOrDefault(element => uint.TryParse((string?)element.Attribute("fromid"), out var from)
                && uint.TryParse((string?)element.Attribute("toid"), out var to) && serverId >= from && serverId <= to);
    }

    private XElement? FindExactXmlItem(uint serverId)
    {
        if (_xml?.Root is null) return null;
        var idText = serverId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _xml.Root.DescendantsAndSelf().LastOrDefault(element => element.Name.LocalName == "item"
            && (string?)element.Attribute("id") == idText);
    }

    private XElement? FindXmlRangeItem(uint serverId)
    {
        if (_xml?.Root is null) return null;
        return _xml.Root.DescendantsAndSelf().LastOrDefault(element => element.Name.LocalName == "item"
            && uint.TryParse((string?)element.Attribute("fromid"), out var from)
            && uint.TryParse((string?)element.Attribute("toid"), out var to) && serverId >= from && serverId <= to);
    }

    private XElement GetOrCreateExactXmlItem(uint serverId)
    {
        if (_xml?.Root is null) throw new InvalidDataException("O items.xml não tem um elemento principal.");
        var exact = FindExactXmlItem(serverId);
        if (exact is not null) return exact;
        var template = FindXmlRangeItem(serverId);
        var element = new XElement(_xml.Root.Name.Namespace + "item", new XAttribute("id", serverId));
        if (template is not null)
        {
            foreach (var attribute in template.Attributes().Where(attribute => attribute.Name.LocalName is not ("fromid" or "toid")))
                element.SetAttributeValue(attribute.Name, attribute.Value);
            foreach (var child in template.Elements()) element.Add(new XElement(child));
        }
        _xml.Root.Add(element);
        return element;
    }

    private static ushort ParseU16(string value, string field)
    {
        if (!ushort.TryParse(value, out var parsed)) throw new InvalidDataException($"O campo {field} deve ser um número entre 0 e 65535.");
        return parsed;
    }

    private static ushort U16(int value) => checked((ushort)Math.Clamp(value, 0, ushort.MaxValue));

    private static void ValidateRootVersion(OtbNode root)
    {
        var data = root.Data;
        if (data.Length < 4) throw new InvalidDataException("O cabeçalho do items.otb está incompleto.");
        var found = false;
        for (var offset = 4; offset + 3 <= data.Length;)
        {
            var id = data[offset++];
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2)); offset += 2;
            if (offset + length > data.Length) throw new InvalidDataException("O items.otb contém um atributo incompleto.");
            if (id == 0x01)
            {
                if (length != 140) throw new InvalidDataException("A versão do items.otb não é reconhecida.");
                found = true;
            }
            offset += length;
        }
        if (!found) throw new InvalidDataException("O items.otb não informa sua versão.");
    }

    private static OtbNode? ReadNode(byte[] bytes, ref int offset)
    {
        if (offset >= bytes.Length || bytes[offset++] != NodeStart) throw new InvalidDataException("Nó items.otb inválido.");
        var type = ReadEscapedByte(bytes, ref offset);
        var data = new List<byte>();
        var children = new List<OtbNode>();
        while (offset < bytes.Length)
        {
            var value = bytes[offset++];
            if (value == Escape)
            {
                if (offset >= bytes.Length) throw new InvalidDataException("Escape incompleto no items.otb.");
                data.Add(bytes[offset++]);
            }
            else if (value == NodeStart)
            {
                offset--;
                var child = ReadNode(bytes, ref offset) ?? throw new InvalidDataException("Nó items.otb inválido.");
                children.Add(child);
            }
            else if (value == NodeEnd) return new OtbNode(type, data.ToArray(), children);
            else data.Add(value);
        }
        throw new InvalidDataException("Nó items.otb sem fechamento.");
    }

    private static byte ReadEscapedByte(byte[] bytes, ref int offset)
    {
        if (offset >= bytes.Length) throw new InvalidDataException("Nó items.otb incompleto.");
        var value = bytes[offset++];
        if (value == Escape)
        {
            if (offset >= bytes.Length) throw new InvalidDataException("Escape incompleto no items.otb.");
            value = bytes[offset++];
        }
        return value;
    }

    private static void WriteNode(Stream stream, OtbNode node)
    {
        stream.WriteByte(NodeStart);
        WriteEscaped(stream, node.Type);
        foreach (var value in node.Data) WriteEscaped(stream, value);
        foreach (var child in node.Children) WriteNode(stream, child);
        stream.WriteByte(NodeEnd);
    }

    private static void WriteEscaped(Stream stream, byte value)
    {
        if (value is NodeStart or NodeEnd or Escape) stream.WriteByte(Escape);
        stream.WriteByte(value);
    }

    private sealed class OtbNode(byte type, byte[] data, List<OtbNode> children)
    {
        public byte Type { get; } = type;
        public byte[] Data { get; set; } = data;
        public List<OtbNode> Children { get; } = children;
    }

    private sealed class OtbItem
    {
        private readonly OtbNode _node;
        private uint _flags;
        private byte _group;

        public OtbItem(OtbNode node)
        {
            _node = node;
            if (node.Data.Length < 5) throw new InvalidDataException("Um item no items.otb está incompleto.");
            _group = node.Type;
            _flags = BinaryPrimitives.ReadUInt32LittleEndian(node.Data.AsSpan(0, 4));
            foreach (var attribute in Attributes())
                if (attribute.Data.Length < (attribute.Id is 0x10 or 0x11 ? 2 : attribute.Id is ItemAttrServerId32 or ItemAttrClientId32 ? 4 : 0))
                    throw new InvalidDataException("Um atributo de item no items.otb está incompleto.");
        }

        public static OtbItem CreateNew(byte group) => new(new OtbNode(group,
        [
            0, 0, 0, 0,
            0x10, 2, 0, 0, 0,
            0x11, 2, 0, 0, 0
        ], []));

        public OtbNode Node => _node;
        public byte Group => _group;
        public uint ServerId => ReadId(ItemAttrServerId32, 0x10);
        public uint ClientId => ReadId(ItemAttrClientId32, 0x11);

        public ServerItemInfo GetInfo() => new(ServerId, ClientId, ReadString(0x12), ReadU16(0x14), ReadU16(0x21),
            ReadPair(0x2A, 0), ReadPair(0x2A, 2), ReadU16(0x2D), _flags);

        public bool SetFlag(uint mask, bool value)
        {
            var updated = value ? _flags | mask : _flags & ~mask;
            if (_flags == updated) return false;
            _flags = updated;
            BinaryPrimitives.WriteUInt32LittleEndian(_node.Data.AsSpan(0, 4), _flags);
            return true;
        }

        public bool SetName(string value) => SetAttribute(0x12, value.Length == 0 ? null : Utf8.GetBytes(value));
        public bool SetUInt16(byte id, ushort value) => SetAttribute(id, value == 0 ? null : U16Bytes(value));
        public bool SetUInt32(byte id, uint value)
        {
            if (value == 0) return SetAttribute(id, null);
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            return SetAttribute(id, bytes);
        }
        public bool SetByte(byte id, int value) => SetAttribute(id, value == 0 ? null : [(byte)value]);

        public bool SetLight(ushort level, ushort color)
        {
            if (level == 0 && color == 0) return SetAttribute(0x2A, null);
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, level);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), color);
            return SetAttribute(0x2A, bytes);
        }

        private ushort ReadU16(byte id)
        {
            var attr = Attributes().LastOrDefault(value => value.Id == id);
            return attr is { Data.Length: >= 2 } ? BinaryPrimitives.ReadUInt16LittleEndian(attr.Data) : (ushort)0;
        }

        private uint ReadId(byte id32, byte legacyId)
        {
            var attr = Attributes().LastOrDefault(value => value.Id == id32);
            return attr is { Data.Length: >= 4 }
                ? BinaryPrimitives.ReadUInt32LittleEndian(attr.Data)
                : ReadU16(legacyId);
        }

        private ushort ReadPair(byte id, int offset)
        {
            var attr = Attributes().LastOrDefault(value => value.Id == id);
            return attr is not null && attr.Data.Length >= offset + 2 ? BinaryPrimitives.ReadUInt16LittleEndian(attr.Data.AsSpan(offset, 2)) : (ushort)0;
        }

        private string ReadString(byte id)
        {
            var attr = Attributes().LastOrDefault(value => value.Id == id);
            return attr is null ? "" : Utf8.GetString(attr.Data);
        }

        public bool SetAttribute(byte id, byte[]? payload)
        {
            var attributes = Attributes();
            var existing = attributes.LastOrDefault(value => value.Id == id);
            if (payload is not null && existing is not null && existing.Data.AsSpan().SequenceEqual(payload)) return false;
            if (payload is null && existing is null) return false;
            if (payload is null) attributes.RemoveAll(value => value.Id == id);
            else if (existing is null) attributes.Add(new OtbAttribute(id, payload));
            else
            {
                attributes.RemoveAll(value => value.Id == id);
                attributes.Add(new OtbAttribute(id, payload));
            }
            SerializeAttributes(attributes);
            return true;
        }

        private List<OtbAttribute> Attributes()
        {
            var values = new List<OtbAttribute>();
            var data = _node.Data.AsSpan(4);
            for (var offset = 0; offset < data.Length;)
            {
                if (offset + 3 > data.Length) throw new InvalidDataException("A lista de atributos no items.otb está incompleta.");
                var id = data[offset++];
                var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)); offset += 2;
                if (offset + length > data.Length) throw new InvalidDataException("Um atributo no items.otb excede o tamanho disponível.");
                values.Add(new OtbAttribute(id, data.Slice(offset, length).ToArray()));
                offset += length;
            }
            return values;
        }

        private void SerializeAttributes(List<OtbAttribute> attributes)
        {
            using var stream = new MemoryStream();
            Span<byte> flags = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(flags, _flags); stream.Write(flags);
            var length = new byte[2];
            foreach (var attr in attributes)
            {
                if (attr.Data.Length > ushort.MaxValue) throw new InvalidDataException("Um atributo do items.otb excede o limite de tamanho.");
                stream.WriteByte(attr.Id);
                BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)attr.Data.Length); stream.Write(length);
                stream.Write(attr.Data);
            }
            _node.Data = stream.ToArray();
        }

        private static byte[] U16Bytes(ushort value)
        {
            var bytes = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); return bytes;
        }
    }

    private sealed record OtbAttribute(byte Id, byte[] Data);
}

internal sealed record ServerItemInfo(uint ServerId, uint ClientId, string Name, ushort GroundSpeed,
    ushort MinimapColor, ushort LightLevel, ushort LightColor, ushort TradeAs, uint Flags);
internal sealed record XmlItemInfo(string Name, string Article, string Plural);
internal sealed record XmlItemAttributeInfo(string Key, string Value);
