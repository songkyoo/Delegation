using Microsoft.CodeAnalysis;

namespace Macaron.Delegation;

internal sealed class CompatibleImplementationChecker(
    MemberImplementationIndex implementationIndex,
    HashSet<ISymbol> explicitImplementations,
    Func<ISymbol, bool>? isAccessible = null
)
{
    public bool HasImplementation(ISymbol interfaceMemberSymbol)
    {
        var implementation = implementationIndex.FindImplicit(
            interfaceMemberSymbol,
            interfaceMemberSymbol.Name,
            MethodReturnTypeComparison.Match
        );

        return implementation != null
            && HasAccessibleAccessors(interfaceMemberSymbol, implementation)
            || explicitImplementations.Contains(interfaceMemberSymbol);
    }

    private bool HasAccessibleAccessors(ISymbol contract, ISymbol implementation)
    {
        if (contract is not IPropertySymbol property || implementation is not IPropertySymbol targetProperty)
        {
            return true;
        }

        return (property.GetMethod == null || IsAccessible(targetProperty.GetMethod))
            && (property.SetMethod == null || IsAccessible(targetProperty.SetMethod));
    }

    private bool IsAccessible(IMethodSymbol? accessor)
    {
        return accessor != null && (isAccessible == null || isAccessible(accessor));
    }
}
