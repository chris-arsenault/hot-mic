namespace HotMic.Core.Dsp.KissFft;

internal sealed class Array<T>
{
    private readonly T[] _fullArray;
    private readonly int _maxSize;
    private readonly int _offset;

    public Array(T[] fullArray)
    {
        _fullArray = fullArray;
        _maxSize = fullArray.Length;
    }

    public Array(Array<T> array)
    {
        _fullArray = array._fullArray;
        _maxSize = array._maxSize;
        _offset = array._offset;
    }

    public Array(Array<T> array, int offset)
    {
        _fullArray = array._fullArray;
        _maxSize = array._maxSize;
        _offset = array._offset + offset;
    }

    public ref T this[int index] => ref _fullArray[_offset + index];

    public override bool Equals(object? o)
    {
        var other = o as Array<T>;
        if (other is not null)
        {
            return this == other;
        }
        return ReferenceEquals(this, o);
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    public static bool operator ==(Array<T>? a, Array<T>? b)
    {
        if (a is not null && b is not null)
        {
            return a._fullArray == b._fullArray && a._offset == b._offset && a._maxSize == b._maxSize;
        }
        return false;
    }

    public static bool operator !=(Array<T> a, Array<T> b)
    {
        return !(a == b);
    }

    public static bool operator ==(Array<T>? a, T[]? b)
    {
        if (a is not null && b is not null)
        {
            return a._fullArray == b;
        }
        return ReferenceEquals(a, b);
    }

    public static bool operator !=(Array<T> a, T[] b)
    {
        return !(a == b);
    }

    public static Array<T> operator ++(Array<T> a)
    {
        return new Array<T>(a, 1);
    }

    public static Array<T> operator +(Array<T> a, int offset)
    {
        return new Array<T>(a, offset);
    }
}
