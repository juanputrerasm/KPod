#if !NET5_0_OR_GREATER
namespace System.Runtime.Versioning;

/// <summary>
/// No-op stand-in for the platform-compatibility attribute, which .NET Framework
/// does not ship. The Framework build is Windows-only by definition, so it
/// carries no meaning here, but the source is shared with the .NET build.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly
                | AttributeTargets.Class
                | AttributeTargets.Constructor
                | AttributeTargets.Enum
                | AttributeTargets.Event
                | AttributeTargets.Field
                | AttributeTargets.Method
                | AttributeTargets.Module
                | AttributeTargets.Property
                | AttributeTargets.Struct,
                AllowMultiple = true,
                Inherited = false)]
internal sealed class SupportedOSPlatformAttribute(string platformName) : Attribute
{
    public string PlatformName { get; } = platformName;
}
#endif
