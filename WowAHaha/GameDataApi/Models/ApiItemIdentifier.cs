using System.Diagnostics;

namespace WowAHaha.GameDataApi.Models;

[DebuggerDisplay("{ItemId}")]
public readonly struct ApiItemIdentifier : IComparable<ApiItemIdentifier>, IComparable, IEquatable<ApiItemIdentifier>
{
    private readonly ulong _itemId;

    // ReSharper disable once ConvertToPrimaryConstructor
    // ReSharper disable once MemberCanBePrivate.Global
    public ApiItemIdentifier(ulong itemId)
    {
        _itemId = itemId;
    }

    // ReSharper disable once ConvertToAutoPropertyWhenPossible
    public ulong ItemId => _itemId;

    public static implicit operator ulong(ApiItemIdentifier id)
    {
        return id._itemId;
    }

    public static implicit operator ApiItemIdentifier(ulong id)
    {
        return new ApiItemIdentifier(id);
    }

    public override string ToString()
    {
        return _itemId.ToString();
    }

    #region IComparable<ApiItemIdentifier> implementation

    public int CompareTo(ApiItemIdentifier other)
    {
        return _itemId.CompareTo(other._itemId);
    }

    public int CompareTo(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return 1;
        }

        return obj is ApiItemIdentifier other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(ApiItemIdentifier)}");
    }

    #endregion

    #region IEquatable<ApiItemIdentifier> implementation

    public bool Equals(ApiItemIdentifier other)
    {
        return _itemId == other._itemId;
    }

    public override bool Equals(object? obj)
    {
        return obj is ApiItemIdentifier other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _itemId.GetHashCode();
    }

    public static bool operator ==(ApiItemIdentifier left, ApiItemIdentifier right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ApiItemIdentifier left, ApiItemIdentifier right)
    {
        return !left.Equals(right);
    }

    #endregion
}