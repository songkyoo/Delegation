using System.Diagnostics;

using static System.AttributeTargets;

namespace Macaron.InterfaceDelegation;

[Conditional("SOURCE_GENERATOR_ONLY")]
[AttributeUsage(validOn: Property | Field | Parameter, AllowMultiple = true)]
public sealed class ImplementAttribute(
    Type? interfaceType = null,
    ImplementationMode mode = ImplementationMode.Implicit
) : Attribute
{
    public Type? InterfaceType { get; } = interfaceType;

    public ImplementationMode Mode { get; } = mode;
}
