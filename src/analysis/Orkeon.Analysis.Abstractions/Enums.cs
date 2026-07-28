using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Analysis.Abstractions;

public enum UniversalNodeKind
{
    Monorepo,
    Package,
    Module,

    Class, Interface, Trait, Struct, Enum, Record, Type, Namespace,

    Function, Method, Constructor, Destructor, Operator, Property, Field,

    Constant, Variable,

    Decorator,

    Unknown
}

[SuppressMessage("Naming", "CA1707", Justification = "The L<n>_ prefix encodes the graph stratification ordinal and is part of the public JSON serialization contract (e.g. \"L3_Symbol\"); renaming would break persisted RaggableTree data and round-tripping.")]
public enum NodeLevel
{
    L0_Monorepo,
    L1_Package,
    L2_Module,
    L3_Symbol,
    L4_Statement
}

// S2342 asks for a plural name (`EdgeKinds`) because this is a [Flags] enum. Not renamed:
// `EdgeKind` is public API of Orkeon.Analysis.Abstractions, referenced across 47 files and
// serialized into persisted RaggableTree graphs — a rename is a breaking change with no
// behavioural benefit. Revisit only alongside a deliberate major-version break.
#pragma warning disable S2342 // Public API; rename is breaking and out of proportion
[Flags]
public enum EdgeKind
{
    None = 0,
    Imports = 1 << 0,
    Calls = 1 << 1,
    Extends = 1 << 2,
    Implements = 1 << 3,
    Contains = 1 << 4
}
#pragma warning restore S2342

public enum StatementKind
{
    VariableDecl, ConstantDecl, Assignment, Destructuring,

    If, Else, ElseIf, Switch, SwitchCase, SwitchDefault,
    For, ForOf, ForIn, While, DoWhile,
    Return, Break, Continue, Throw, EarlyReturn,

    FunctionCall, MethodCall, ChainedCall, ConstructorCall, Await, Yield,

    TryCatch, Catch, Finally,

    ArrowFunction, Closure, Callback, Promise,

    Assertion, TypeGuard, NullCheck, Pattern,

    Comment,
    Unknown
}

public enum ReferenceKind { Call, Read, Write, TypeRef, Import, Instantiate }

public enum ParseStatus { Ok, Partial, Failed }

public enum DiagnosticSeverity { Info, Warning, Error }

public enum Direction { Forward, Backward, Both }

public enum SourceMode { SignatureOnly, SignatureAndDoc, SignatureAndBody, FullSpan, StatementSpan }

[Flags]
public enum ExpandModes
{
    None = 0,
    Members = 1 << 0,
    Inheritance = 1 << 1,
    Callers = 1 << 2,
    Callees = 1 << 3,
    PublicExports = 1 << 4,
    Statements = 1 << 5,
    Decorators = 1 << 6,
    ParentChain = 1 << 7,
    All = Members | Inheritance | Callers | Callees | PublicExports | Statements | Decorators | ParentChain
}

public enum CentralityMetric
{
    InDegreeCalls,
    OutDegreeCalls,
    InDegreeImports,
    Betweenness,
    PageRank
}

public enum ComplexityMetric { Cyclomatic, NestingDepth, FanOut, LoC, Callers }

public enum FileChangeKind { Created, Modified, Deleted, Renamed }
