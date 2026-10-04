using Microsoft.CodeAnalysis;

namespace SpectraEngine.Entities.Generator;

// All errors: each is a declaration the generator cannot turn into working code.
// Ids are frozen (they sit in NoWarn lists). New ones take the next free number.
internal static class EntityDiagnostics
{
    private const string Category = "SpectraEntities";

    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "SPE001",
        title: "Entity class must be partial",
        messageFormat:
            "Entity class '{0}' must be declared partial (and so must every type containing it), because the " +
            "generator emits the other half of it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateClassName = new(
        id: "SPE002",
        title: "Duplicate entity class name",
        messageFormat:
            "Entity class name '{0}' is declared by both '{1}' and '{2}'. Two classes claiming one name is a map " +
            "that means different things in different builds, and the catalogue refuses the second at start-up.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedKeyvalueType = new(
        id: "SPE003",
        title: "Unsupported keyvalue member type",
        messageFormat:
            "Keyvalue '{0}' is declared on a member of type '{1}', which no KeyvalueType is inferred from. State " +
            "the type explicitly (Type = KeyvalueType.Color, say), or use one of: bool, int, uint, float, string, " +
            "Vector2, Vector3, Vector4, Guid.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidInputSignature = new(
        id: "SPE004",
        title: "Entity input has the wrong signature",
        messageFormat:
            "Entity input method '{0}' must be a non-generic instance method shaped " +
            "'void {0}(ref EntityInputContext context)'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReservedKeyvalueName = new(
        id: "SPE005",
        title: "Reserved keyvalue name",
        messageFormat:
            "Keyvalue '{0}' on '{1}' is reserved: targetname IS SceneNode.Name, so a keyvalue of that name forks " +
            "the identity into two fields that a rename updates one of",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KeyvalueTypeMismatch = new(
        id: "SPE006",
        title: "Keyvalue type does not match the member",
        messageFormat:
            "Keyvalue '{0}' declares KeyvalueType.{1}, which is read as '{2}', but the member's type is '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KeyvalueNotAssignable = new(
        id: "SPE007",
        title: "Keyvalue member cannot be assigned",
        messageFormat:
            "Keyvalue '{0}' must be a settable instance field or property; '{1}' is not one, so the generated " +
            "binder would have nowhere to put the parsed value",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
