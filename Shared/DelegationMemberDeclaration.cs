using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal abstract record DelegationMemberDeclaration;

internal sealed record ImplicitDelegationMemberDeclaration(
    Accessibility Accessibility
) : DelegationMemberDeclaration;

internal sealed record ExplicitInterfaceDelegationMemberDeclaration(
    ITypeSymbol InterfaceTypeSymbol
) : DelegationMemberDeclaration;

internal sealed record OverrideDelegationMemberDeclaration(
    Accessibility Accessibility
) : DelegationMemberDeclaration;
