using Microsoft.CodeAnalysis;

namespace Macaron.InterfaceDelegation;

internal static class ImplementDiagnostics
{
    public static readonly DiagnosticDescriptor InvalidImplementationTargetRule = new(
        id: "MAID0001",
        title: "Implement attribute requires a valid interface type",
        messageFormat: "'{0}' is not a valid type for the Implement attribute. Only non-generic or constructed generic interfaces are allowed.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ValueTypePropertyCannotBeDelegatedRule = new(
        id: "MAID0002",
        title: "Value type property cannot be delegated",
        messageFormat: "Property '{0}' is of a value type and cannot be delegated using Implement",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateDelegationTargetRule = new(
        id: "MAID0003",
        title: "Duplicate Implement target",
        messageFormat: "The interface '{0}' is delegated more than once in the same type",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ImplementMemberNotImplementedRule = new(
        id: "MAID0005",
        title: "Implement target does not implement an interface member",
        messageFormat: "The target type '{0}' does not implement interface member '{1}' required by Implement",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
