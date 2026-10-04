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

internal sealed record InputModel(string Name, string MethodName) : IEquatable<InputModel>;

// Everything the emitter needs about one attributed class. Values only: an
// ISymbol or SyntaxNode here pins the compilation and compares by reference,
// which breaks incremental caching while the output stays correct.
//
// ContainingTypes: outermost first, each spelled as the partial declaration
// the emitter reopens. ClassName is the wire name. Placement is an
// EntityPlacement member name. IsPartial covers the containing types too.
// Keyvalues, Inputs and Outputs are in declaration order.
internal sealed record EntityModel(
    string Namespace,
    EquatableArray<string> ContainingTypes,
    string TypeName,
    string FullTypeName,
    string ClassName,
    string Display,
    string Group,
    string Placement,
    bool IsPartial,
    EquatableArray<KeyvalueModel> Keyvalues,
    EquatableArray<InputModel> Inputs,
    EquatableArray<string> Outputs,
    EquatableArray<DiagnosticInfo> Diagnostics,
    LocationInfo? Location) : IEquatable<EntityModel>;
