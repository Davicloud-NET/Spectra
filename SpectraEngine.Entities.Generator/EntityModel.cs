using System;

namespace SpectraEngine.Entities.Generator;

// Name is the wire name. Type is the KeyvalueType wire byte, Widget a
// KeyvalueWidget value. Min and Max are NaN when unbounded.
internal sealed record KeyvalueModel(
    string MemberName,
    string Name,
    string Display,
    string Tooltip,
    string Default,
    byte Type,
    byte Widget,
    float Min,
    float Max) : IEquatable<KeyvalueModel>;

// Parameter is the KeyvalueType wire byte of the value the input takes, or
// null for none. CanOmit says the input also works with no parameter.
internal sealed record InputModel(
    string Name,
    string MethodName,
    byte? Parameter,
    bool CanOmit) : IEquatable<InputModel>;

// Name is the member's own. Parameter is the KeyvalueType wire byte of the
// value the output sends, or null for none.
internal sealed record OutputModel(string Name, byte? Parameter) : IEquatable<OutputModel>;

// Everything the emitter needs about one attributed class. Values only: an
// ISymbol or SyntaxNode here pins the compilation and compares by reference,
// which breaks incremental caching while the output stays correct.
//
// ContainingTypes: outermost first, each spelled as the partial declaration
// the emitter reopens. ClassName is the wire name. Placement is an
// EntityPlacement member name. Icon is the icon's name, or empty. HasFacing
// is false unless the class sets it. IsPartial covers the containing types
// too. Keyvalues, Inputs and Outputs are in declaration order.
internal sealed record EntityModel(
    string Namespace,
    EquatableArray<string> ContainingTypes,
    string TypeName,
    string FullTypeName,
    string ClassName,
    string Display,
    string Group,
    string Placement,
    string Icon,
    bool HasFacing,
    bool IsPartial,
    EquatableArray<KeyvalueModel> Keyvalues,
    EquatableArray<InputModel> Inputs,
    EquatableArray<OutputModel> Outputs,
    EquatableArray<DiagnosticInfo> Diagnostics,
    LocationInfo? Location) : IEquatable<EntityModel>;
