using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal static class LiftDiagnostics
{
    public static readonly DiagnosticDescriptor LiftMemberNameNotFoundRule = new(
        id: "MAID0004",
        title: "Lift member name was not found",
        messageFormat: "The member '{0}' was not found on '{1}' for Lift option '{2}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}
