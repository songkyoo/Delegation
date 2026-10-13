using Macaron.Delegation;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal static class ImplementContextFactory
{
    public static GenerationAnalysisResult<ImplementGenerationContext> Create(
        AttributeData attributeData,
        ISymbol declaredSymbol,
        Compilation compilation,
        CancellationToken cancellationToken = default
    )
    {
        var constructorArguments = attributeData.ConstructorArguments;

        if (declaredSymbol is IPropertySymbol { Type.IsValueType: true })
        {
            return new GenerationAnalysisResult<ImplementGenerationContext>(
                Context: null,
                Diagnostics: ImmutableArray.Create(Diagnostic.Create(
                    descriptor: ImplementDiagnostics.ValueTypePropertyCannotBeDelegatedRule,
                    location: declaredSymbol.Locations.FirstOrDefault(),
                    messageArgs: [declaredSymbol.Name]
                ))
            );
        }

        var declaredTypeSymbol = DelegationTargetSymbol.GetDeclaredType(declaredSymbol);
        var suppliedTypeSymbol = constructorArguments[0].Value;
        var interfaceTypeSymbol = suppliedTypeSymbol == null
            ? GetInferredInterfaceType(declaredTypeSymbol)
            : suppliedTypeSymbol as INamedTypeSymbol;

        if (interfaceTypeSymbol is not { TypeKind: TypeKind.Interface, IsUnboundGenericType: false })
        {
            var location = suppliedTypeSymbol == null
                ? attributeData.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                : AttributeArgumentReader.GetFirstArgumentLocation(attributeData);

            return new GenerationAnalysisResult<ImplementGenerationContext>(
                Context: null,
                Diagnostics: ImmutableArray.Create(Diagnostic.Create(
                    descriptor: ImplementDiagnostics.InvalidImplementationTargetRule,
                    location: location,
                    messageArgs: [suppliedTypeSymbol ?? declaredTypeSymbol]
                ))
            );
        }

        var contractDiagnostics = CreateContractDiagnostics(
            attributeData,
            declaredTypeSymbol,
            interfaceTypeSymbol,
            declaredSymbol.ContainingType,
            compilation,
            cancellationToken
        );

        if (!contractDiagnostics.IsEmpty)
        {
            return new GenerationAnalysisResult<ImplementGenerationContext>(null, contractDiagnostics);
        }

        return new GenerationAnalysisResult<ImplementGenerationContext>(
            Context: new ImplementGenerationContext(
                Attribute: attributeData,
                DeclaredSymbol: declaredSymbol,
                DelegationTypeSymbol: interfaceTypeSymbol,
                Mode: GetImplementationMode(attributeData)
            ),
            Diagnostics: ImmutableArray<Diagnostic>.Empty
        );
    }

    private static INamedTypeSymbol? GetInferredInterfaceType(ITypeSymbol declaredTypeSymbol)
    {
        return declaredTypeSymbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Interface } interfaceType => interfaceType,
            INamedTypeSymbol { Interfaces.Length: 1 } targetType => targetType.Interfaces[0],
            _ => null,
        };
    }

    private static ImmutableArray<Diagnostic> CreateContractDiagnostics(
        AttributeData attributeData,
        ITypeSymbol targetTypeSymbol,
        INamedTypeSymbol interfaceTypeSymbol,
        INamedTypeSymbol containingTypeSymbol,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        if (MemberComparisonHelper.ImplementsInterface(targetTypeSymbol, interfaceTypeSymbol))
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        var location = AttributeArgumentReader.GetFirstArgumentLocation(attributeData) ??
            attributeData.ApplicationSyntaxReference?.GetSyntax().GetLocation();
        var implementationChecker = MemberComparisonHelper.CreateCompatibleImplementationChecker(
            typeSymbol: targetTypeSymbol,
            interfaceSymbol: interfaceTypeSymbol,
            isAccessible: memberSymbol => compilation.IsSymbolAccessibleWithin(
                symbol: memberSymbol,
                within: containingTypeSymbol,
                throughType: targetTypeSymbol
            )
        );

        foreach (var interfaceMember in DelegationMemberProvider.GetMembersIncludingBaseTypes(interfaceTypeSymbol))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ImplementMemberRules.IsSupportedInterfaceMember(interfaceMember))
            {
                continue;
            }

            if (!implementationChecker.HasImplementation(interfaceMember))
            {
                builder.Add(Diagnostic.Create(
                    descriptor: ImplementDiagnostics.ImplementMemberNotImplementedRule,
                    location: location,
                    messageArgs: [targetTypeSymbol.ToDisplayString(), interfaceMember.ToDisplayString()]
                ));
            }
        }

        return builder.ToImmutable();
    }

    private static ImplementationMode GetImplementationMode(AttributeData attributeData)
    {
        var mode = attributeData.NamedArguments
            .FirstOrDefault(static argument => argument.Key == nameof(ImplementAttribute.Mode))
            .Value.Value;

        if (mode is null)
        {
            return ImplementationMode.Explicit;
        }

        return (ImplementationMode)mode switch
        {
            var value and ImplementationMode.Explicit => value,
            _ => ImplementationMode.Implicit,
        };
    }
}
