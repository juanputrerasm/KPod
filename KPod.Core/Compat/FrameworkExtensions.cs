#if !NET5_0_OR_GREATER
using System.Collections.Generic;

namespace KPod.Core.Compat;

/// <summary>Instance methods added after .NET Framework, supplied as extensions.</summary>
internal static class FrameworkExtensions
{
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key, TValue value)
        where TKey : notnull
    {
        if (map.ContainsKey(key))
        {
            return false;
        }

        map.Add(key, value);
        return true;
    }

    public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

    public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;

    public static bool Contains(this string text, string value, StringComparison comparison) =>
        text.IndexOf(value, comparison) >= 0;

    public static bool Contains(this string text, char value, StringComparison comparison) =>
        text.IndexOf(value.ToString(), comparison) >= 0;
}
#endif
