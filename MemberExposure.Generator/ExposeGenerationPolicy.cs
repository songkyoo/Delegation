using Macaron.Delegation;
using Microsoft.CodeAnalysis;

using static Macaron.Delegation.DelegationMemberGenerationDecision;
using static Macaron.Delegation.MethodReturnTypeComparison;
using static Microsoft.CodeAnalysis.Accessibility;

namespace Macaron.MemberExposure;

internal static class ExposeGenerationPolicy
{
    public static IEnumerable<ISymbol> GetTargetMembers(ExposeGenerationContext context, Compilation compilation)
    {
        var symbols = !context.PrecomputedTargetMembers.IsDefault
            ? context.PrecomputedTargetMembers
            : context.IncludeBaseTypes
            ? DelegationMemberProvider.GetMembersIncludingBaseTypes(context.DelegationTypeSymbol)
            : DelegationMemberProvider.GetDeclaredMembers(context.DelegationTypeSymbol);

        foreach (var symbol in symbols)
        {
            if (context.PrecomputedTargetMembers.IsDefault
                && !IsExposableMember(symbol, context.DeclaredSymbol, context.DelegationTypeSymbol, compilation)
                || !ShouldIncludeSymbol(context, symbol))
            {
                continue;
            }

            yield return symbol;
        }
    }

    public static DelegationMemberGenerationContext? CreateMemberGenerationContext(
        ExposeGenerationContext context,
        ISymbol symbol,
        MemberImplementationIndex implementationIndex,
        Compilation compilation
    )
    {
        var typeSymbol = context.DeclaredSymbol.ContainingType;
        var symbolName = context.Rename.TryGetValue(symbol.Name, out var renamed)
            ? renamed
            : symbol.Name;
        var propertyAccessors = symbol is IPropertySymbol property
            ? GetPropertyAccessors(property, context.DeclaredSymbol, context.DelegationTypeSymbol, compilation)
            : DelegationPropertyAccessors.None;
        var implicitMember = implementationIndex.FindImplicit(symbol, symbolName, Ignore, propertyAccessors);
        var decision = implicitMember switch
        {
            null => Generate,
            { IsAbstract: true } when !SymbolEqualityComparer.Default.Equals(
                implicitMember.ContainingType, typeSymbol
            ) => OverrideAbstractMember,
            _ => Skip,
        };

        if (decision == Skip)
        {
            return null;
        }

        DelegationMemberDeclaration declaration = decision switch
        {
            Generate => new ImplicitDelegationMemberDeclaration(symbol.DeclaredAccessibility),
            OverrideAbstractMember => new OverrideDelegationMemberDeclaration(symbol.DeclaredAccessibility),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
        };

        return new DelegationMemberGenerationContext(
            Symbol: symbol,
            SymbolName: symbolName,
            Declaration: declaration,
            PropertyAccessors: propertyAccessors
        );
    }

    public static bool IsExposableMember(
        ISymbol symbol,
        ISymbol declaredSymbol,
        ITypeSymbol delegationTypeSymbol,
        Compilation compilation
    )
    {
        if (symbol.DeclaredAccessibility is not Public and not Internal
            || !compilation.IsSymbolAccessibleWithin(symbol, declaredSymbol.ContainingType, delegationTypeSymbol))
        {
            return false;
        }

        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false } => true,
            IPropertySymbol { IsIndexer: false } property =>
                GetPropertyAccessors(
                    property,
                    declaredSymbol,
                    delegationTypeSymbol,
                    compilation
                ) != DelegationPropertyAccessors.None,
            IEventSymbol => true,
            _ => false,
        };
    }

    private static DelegationPropertyAccessors GetPropertyAccessors(
        IPropertySymbol property,
        ISymbol declaredSymbol,
        ITypeSymbol delegationTypeSymbol,
        Compilation compilation
    )
    {
        var accessors = DelegationPropertyAccessors.None;

        if (property.GetMethod is { } getter
            && compilation.IsSymbolAccessibleWithin(getter, declaredSymbol.ContainingType, delegationTypeSymbol))
        {
            accessors |= DelegationPropertyAccessors.Get;
        }

        if (property.SetMethod is { IsInitOnly: false } setter
            && compilation.IsSymbolAccessibleWithin(setter, declaredSymbol.ContainingType, delegationTypeSymbol))
        {
            accessors |= DelegationPropertyAccessors.Set;
        }

        return accessors;
    }

    private static bool ShouldIncludeSymbol(ExposeGenerationContext context, ISymbol symbol)
    {
        if (!context.Filter.IsEmpty && !context.Filter.Contains(symbol.Name))
        {
            return false;
        }

        return !context.Remove.Contains(symbol.Name);
    }
}
