#if !NET5_0_OR_GREATER

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// The marker the C# compiler requires to emit <c>init</c> accessors. It ships in the framework
/// from .NET 5 on; the down-level legs (net48, netstandard2.0) need this internal shim, which is
/// never part of the public surface.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}

#endif
