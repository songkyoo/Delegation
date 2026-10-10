using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Macaron.Delegation;

internal static class PartialTypeValidator
{
    public static INamedTypeSymbol? GetNonPartialType(INamedTypeSymbol? typeSymbol, CancellationToken cancellationToken)
    {
        for (var current = typeSymbol; current != null; current = current.ContainingType)
        {
            foreach (var syntaxReference in current.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (syntaxReference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration &&
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    return current;
                }
            }
        }

        return null;
    }
}
