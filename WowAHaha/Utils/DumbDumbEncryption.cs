using System.Text;
using JetBrains.Annotations;

namespace WowAHaha.Utils;

public static class DumbDumbEncryption
{
    public const string Magic = "!enc:";

    [MustUseReturnValue]
    internal static string Encrypt(string value, string key)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valueBytes = Encoding.UTF8.GetBytes(value);

        for (var i = 0; i < valueBytes.Length; i++)
        {
            valueBytes[i] = (byte)(valueBytes[i] ^ keyBytes[i % keyBytes.Length]);
        }

        Array.Reverse(valueBytes);

        return Magic + Convert.ToBase64String(valueBytes);
    }

    [MustUseReturnValue]
    internal static string Decrypt(string value, string key)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!value.StartsWith(Magic))
        {
            return value;
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valueBytes = Convert.FromBase64String(value[Magic.Length..]);
        Array.Reverse(valueBytes);
        for (var i = 0; i < valueBytes.Length; i++)
        {
            valueBytes[i] = (byte)(valueBytes[i] ^ keyBytes[i % keyBytes.Length]);
        }

        return Encoding.UTF8.GetString(valueBytes);
    }
}