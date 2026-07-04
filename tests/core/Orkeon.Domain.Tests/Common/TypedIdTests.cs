using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Common;

public sealed class TypedIdTests
{
    // ── Test doubles ──────────────────────────────────────────────────

    private sealed class UlidIdA : TypedId
    {
        public UlidIdA(Ulid value) : base(value) { }
    }

    private sealed class UlidIdB : TypedId
    {
        public UlidIdB(Ulid value) : base(value) { }
    }

    // ── Specs ─────────────────────────────────────────────────────────

    [Fact]
    public void AsString_drives_ToString_and_implicit_string()
    {
        var u = Ulid.NewUlid();
        var id = new UlidIdA(u);
        Assert.Equal(u.ToString(), id.ToString());
        Assert.Equal(u.ToString(), id.AsString());
        string implicitConversion = id;
        Assert.Equal(u.ToString(), implicitConversion);
    }

    [Fact]
    public void Equality_holds_for_same_type_and_same_value()
    {
        var u = Ulid.NewUlid();
        var a = new UlidIdA(u);
        var b = new UlidIdA(u);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_fails_for_same_value_but_different_runtime_types()
    {
        var u = Ulid.NewUlid();
        var a = new UlidIdA(u);
        var b = new UlidIdB(u);
        Assert.NotEqual<TypedId>(a, b);
        Assert.False(a == (TypedId)b);
        Assert.True(a != (TypedId)b);
    }

    [Fact]
    public void Implicit_string_conversion_returns_empty_for_null()
    {
        TypedId? id = null;
        string s = id!;
        Assert.Equal(string.Empty, s);
    }

    [Fact]
    public void Value_is_a_Ulid_and_round_trips_through_AsString()
    {
        var u = Ulid.NewUlid();
        var id = new UlidIdA(u);
        Assert.Equal(u, id.Value);
        Assert.Equal(u.ToString(), id.AsString());
    }

    [Fact]
    public void EntityId_subclasses_inherit_TypedId_contract()
    {
        // CrewId is an EntityId<CrewId> -> TypedId. Same-value equality + cross-type inequality.
        var c1 = CrewId.Create();
        var c2 = CrewId.From(c1.Value);
        Assert.True(c1 == c2);

        // CrewId.System is the reserved sentinel.
        Assert.True(CrewId.IsSystem(CrewId.System));
        Assert.False(CrewId.IsSystem(c1));

        // Cross-type: a CrewId and an AgentId carrying the same ULID must NOT be equal.
        var sharedUlid = Ulid.NewUlid();
        var asCrew = CrewId.From(sharedUlid);
        var asAgent = AgentId.From(sharedUlid);
        Assert.False(((TypedId)asCrew).Equals((TypedId)asAgent));
    }

    [Fact]
    public void HashCode_distinguishes_runtime_types()
    {
        // Two TypedIds with identical Value but different runtime types should ideally
        // produce different hash codes to avoid hash collisions in heterogeneous dictionaries.
        var u = Ulid.NewUlid();
        var a = new UlidIdA(u);
        var b = new UlidIdB(u);
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }
}
