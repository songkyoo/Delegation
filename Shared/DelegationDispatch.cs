using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal abstract record DelegationDispatch(string TargetName);

internal sealed record DirectDelegationDispatch(
    string TargetName
) : DelegationDispatch(TargetName);

internal sealed record InterfaceCastDelegationDispatch(
    string TargetName,
    ITypeSymbol InterfaceTypeSymbol
) : DelegationDispatch(TargetName);

internal sealed record ConstrainedFieldDelegationDispatch(
    string TargetName,
    ITypeSymbol InterfaceTypeSymbol
) : DelegationDispatch(TargetName);
