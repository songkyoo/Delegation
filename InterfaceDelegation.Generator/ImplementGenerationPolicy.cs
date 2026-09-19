using Macaron.Delegation;
using Microsoft.CodeAnalysis;

using static Macaron.Delegation.DelegationMemberGenerationDecision;
using static Macaron.InterfaceDelegation.ImplementationMode;
using static Macaron.Delegation.MethodReturnTypeComparison;
using static Microsoft.CodeAnalysis.Accessibility;

namespace Macaron.InterfaceDelegation;

internal static class ImplementGenerationPolicy
{
    public static DelegationDispatch CreateDispatch(ImplementGenerationContext context)
    {
        var declaredSymbol = context.DeclaredSymbol;

        if (!RequiresInterfaceDispatch(context))
        {
            return new DirectDelegationDispatch(declaredSymbol.Name);
        }

        return declaredSymbol is IFieldSymbol
            ? new ConstrainedFieldDelegationDispatch(declaredSymbol.Name, context.DelegationTypeSymbol)
            : new InterfaceCastDelegationDispatch(declaredSymbol.Name, context.DelegationTypeSymbol);
    }

    public static bool RequiresInterfaceDispatch(ImplementGenerationContext context)
    {
        var targetTypeSymbol = DelegationTargetSymbol.GetDeclaredType(context.DeclaredSymbol);

        return
            !SymbolEqualityComparer.Default.Equals(targetTypeSymbol, context.DelegationTypeSymbol)
            && MemberComparisonHelper.ImplementsInterface(targetTypeSymbol, context.DelegationTypeSymbol);
    }

    public static IEnumerable<ISymbol> GetTargetMembers(ImplementGenerationContext context)
    {
        foreach (var symbol in DelegationMemberProvider.GetMembersIncludingBaseTypes(context.DelegationTypeSymbol))
        {
            if (ImplementMemberRules.IsSupportedInterfaceMember(symbol))
            {
                yield return symbol;
            }
        }
    }

    public static DelegationMemberGenerationContext? CreateMemberGenerationContext(
        ImplementGenerationContext context,
        ISymbol symbol,
        MemberImplementationIndex implementationIndex
    )
    {
        var typeSymbol = context.DeclaredSymbol.ContainingType;
        var symbolName = symbol.Name;
        var implicitMember = implementationIndex.FindImplicit(symbol, symbolName, Match);
        var explicitMember = implementationIndex.FindExplicit(symbol, symbolName, Match);
        var mode = symbolName == typeSymbol.Name ? Explicit : context.Mode;
        var decision = mode switch
        {
            Explicit => explicitMember == null ? GenerateExplicitInterfaceImplementation : Skip,
            _ => (implicitMember, explicitMember) switch
            {
                (null, null) => Generate,
                ({ IsAbstract: true }, null) when !SymbolEqualityComparer.Default.Equals(
                    implicitMember.ContainingType, typeSymbol
                ) => OverrideAbstractMember,
                _ => Skip,
            },
        };

        if (decision == Skip)
        {
            return null;
        }

        DelegationMemberDeclaration declaration = decision switch
        {
            Generate => new ImplicitDelegationMemberDeclaration(Accessibility: Public),
            GenerateExplicitInterfaceImplementation => new ExplicitInterfaceDelegationMemberDeclaration(
                context.DelegationTypeSymbol
            ),
            OverrideAbstractMember => new OverrideDelegationMemberDeclaration(Accessibility: Public),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
        };

        return new DelegationMemberGenerationContext(
            Symbol: symbol,
            SymbolName: symbolName,
            Declaration: declaration
        );
    }
}
