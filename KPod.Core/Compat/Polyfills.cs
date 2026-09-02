#if !NET5_0_OR_GREATER
// Types and members the C# compiler expects but .NET Framework does not ship.
// Keeping them here means the rest of the codebase is written once, in modern
// C#, and compiles unchanged for every target.
using System.Collections.Generic;

namespace System;

/// <summary>Supports the <c>^n</c> index syntax.</summary>
internal readonly struct Index(int value, bool fromEnd = false) : IEquatable<Index>
{
    private readonly int _value = fromEnd ? ~value : value;

    public int Value => _value < 0 ? ~_value : _value;

    public bool IsFromEnd => _value < 0;

    public static Index Start => new(0);

    public static Index End => new(0, fromEnd: true);

    public static Index FromStart(int value) => new(value);

    public static Index FromEnd(int value) => new(value, fromEnd: true);

    public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

    public static implicit operator Index(int value) => new(value);

    public bool Equals(Index other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Index other && Equals(other);

    public override int GetHashCode() => _value;
}

/// <summary>Supports the <c>a..b</c> range syntax.</summary>
internal readonly struct Range(Index start, Index end) : IEquatable<Range>
{
    public Index Start { get; } = start;

    public Index End { get; } = end;

    public static Range All => new(Index.Start, Index.End);

    public static Range StartAt(Index start) => new(start, Index.End);

    public static Range EndAt(Index end) => new(Index.Start, end);

    public (int Offset, int Length) GetOffsetAndLength(int length)
    {
        int start = Start.GetOffset(length);
        int end = End.GetOffset(length);
        return (start, end - start);
    }

    public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);

    public override bool Equals(object? obj) => obj is Range other && Equals(other);

    public override int GetHashCode() => (Start.GetHashCode() * 31) + End.GetHashCode();
}
#endif
