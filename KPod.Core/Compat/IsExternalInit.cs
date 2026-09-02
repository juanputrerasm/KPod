#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Records and init-only setters need this marker type, which .NET Framework
/// does not ship. Defining it locally is the standard polyfill.
/// </summary>
internal static class IsExternalInit
{
}
#endif
