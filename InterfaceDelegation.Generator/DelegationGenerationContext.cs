using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal readonly record struct DelegationGenerationContext(
    DelegationDispatch Dispatch,
    MemberImplementationIndex ImplementationIndex
)
{
    public static DelegationGenerationContext Create(
        ISymbol declaredSymbol,
        ITypeSymbol delegationTypeSymbol,
        DelegationDispatch dispatch
    )
    {
        return new DelegationGenerationContext(
            Dispatch: dispatch,
            ImplementationIndex: MemberComparisonHelper.CreateImplementationIndex(
                declaredSymbol.ContainingType,
                delegationTypeSymbol
            )
        );
    }
}
