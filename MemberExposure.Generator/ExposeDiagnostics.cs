using Microsoft.CodeAnalysis;

namespace Macaron.MemberExposure;

internal static class ExposeDiagnostics
{
    public static readonly DiagnosticDescriptor ExposeMemberNameNotFoundRule = new(
        id: "MAME0001",
        title: "Expose member name was not found",
        messageFormat: "The member '{0}' was not found on '{1}' for Expose option '{2}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}
