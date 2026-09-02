namespace KPod.Core.Pods;

/// <summary>
/// A POD archive could not be parsed. Derives from <see cref="IOException"/> so
/// callers that already handle file errors handle malformed archives too.
/// </summary>
public sealed class PodFormatException(string message) : IOException(message);
