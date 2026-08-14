using System.Text;
using Trafty.Core.Models;

namespace Trafty.Core.Models.Nif;

/// <summary>
/// Sequential little-endian cursor over a .nif byte buffer, covering the primitive types used
/// by NIF block layouts at format version 4.2.2.0 (see field-level comments in
/// <see cref="NifDocument"/> and the block classes for how each maps to the public NifTools
/// nif.xml spec at that version). Kept separate from <see cref="NifHeader"/> since the header
/// line is a special case (newline-terminated, not length-prefixed).
/// </summary>
internal ref struct NifByteReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public NifByteReader(ReadOnlySpan<byte> data, int startPosition)
    {
        _data = data;
        _position = startPosition;
    }

    public int Position => _position;
    public int Remaining => _data.Length - _position;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _data[_position++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        ushort value = (ushort)(_data[_position] | (_data[_position + 1] << 8));
        _position += 2;
        return value;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        uint value = (uint)(_data[_position] | (_data[_position + 1] << 8) |
                             (_data[_position + 2] << 16) | (_data[_position + 3] << 24));
        _position += 4;
        return value;
    }

    public int ReadInt32() => (int)ReadUInt32();

    public float ReadSingle()
    {
        EnsureAvailable(4);
        float value = BitConverter.ToSingle(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>Block reference / pointer: a signed int32 index into the file's block list, -1 = none.</summary>
    public int ReadRef() => ReadInt32();

    /// <summary>SizedString: uint32 length followed by that many ASCII bytes (no NUL terminator).</summary>
    public string ReadString()
    {
        uint length = ReadUInt32();
        EnsureAvailable((int)length);
        string value = Encoding.ASCII.GetString(_data.Slice(_position, (int)length));
        _position += (int)length;
        return value;
    }

    public (float X, float Y, float Z) ReadVector3() => (ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>3x3 rotation matrix, stored row-major as 9 floats.</summary>
    public float[] ReadMatrix33()
    {
        var m = new float[9];
        for (int i = 0; i < 9; i++)
        {
            m[i] = ReadSingle();
        }

        return m;
    }

    public (float R, float G, float B) ReadColor3() => (ReadSingle(), ReadSingle(), ReadSingle());

    public (float R, float G, float B, float A) ReadColor4() => (ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    public (float U, float V) ReadTexCoord() => (ReadSingle(), ReadSingle());

    public (ushort V1, ushort V2, ushort V3) ReadTriangle() => (ReadUInt16(), ReadUInt16(), ReadUInt16());

    /// <summary>NiBound: a bounding sphere (center + radius), 16 bytes.</summary>
    public void SkipNiBound()
    {
        ReadVector3();
        ReadSingle();
    }

    public void Skip(int byteCount)
    {
        EnsureAvailable(byteCount);
        _position += byteCount;
    }

    private readonly void EnsureAvailable(int byteCount)
    {
        if (_position + byteCount > _data.Length)
        {
            throw new ModelFormatException(
                $"Unexpected end of file at offset {_position}: need {byteCount} more byte(s).");
        }
    }
}
