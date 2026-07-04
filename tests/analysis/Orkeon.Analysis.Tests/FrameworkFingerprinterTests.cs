using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Fingerprinters;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class FrameworkFingerprinterTests
{
    private static IReadOnlyList<RaggableNode> ExtractTs(string source)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        return mapper.ExtractNodes("/r/a.ts", source);
    }

    private static IReadOnlyList<RaggableNode> ExtractCs(string source)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new CSharpAdapter(), pool);
        return mapper.ExtractNodes("/r/a.cs", source);
    }

    private static IReadOnlyList<RaggableNode> ExtractPy(string source)
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new PythonAdapter(), pool);
        return mapper.ExtractNodes("/r/a.py", source);
    }

    [Fact]
    public void NestJs_Controller_tags_applied()
    {
        var nodes = ExtractTs(TestFixtures.NestJsController).ToList();
        NestJsRules.Create().Apply(nodes, []);
        var ctrl = nodes.First(n => n.Name == "UsersController");
        Assert.True(ctrl.Tags.ContainsKey("http-controller"));
        Assert.True(ctrl.Tags.ContainsKey("nestjs"));
    }

    [Fact]
    public void NestJs_Get_tags_applied()
    {
        var nodes = ExtractTs(TestFixtures.NestJsController).ToList();
        NestJsRules.Create().Apply(nodes, []);
        var list = nodes.First(n => n.Name == "list");
        Assert.True(list.Tags.ContainsKey("http-endpoint"));
        Assert.True(list.Tags.ContainsKey("http-get"));
    }

    [Fact]
    public void NestJs_Injectable_service_tag()
    {
        var nodes = ExtractTs(TestFixtures.NestJsController).ToList();
        NestJsRules.Create().Apply(nodes, []);
        var svc = nodes.First(n => n.Name == "UsersService");
        Assert.True(svc.Tags.ContainsKey("service"));
    }

    [Fact]
    public void Angular_Component_tag_applied()
    {
        var nodes = ExtractTs(TestFixtures.AngularComponent).ToList();
        AngularRules.Create().Apply(nodes, []);
        var comp = nodes.First(n => n.Name == "ItemComponent");
        Assert.True(comp.Tags.ContainsKey("ui-component"));
        Assert.True(comp.Tags.ContainsKey("angular"));
    }

    [Fact]
    public void AspNet_ApiController_and_HttpGet_tagged()
    {
        var nodes = ExtractCs(TestFixtures.CSharpController).ToList();
        AspNetRules.Create().Apply(nodes, []);
        var ctrl = nodes.First(n => n.Name == "ItemsController");
        Assert.True(ctrl.Tags.ContainsKey("api-controller"));
        var getAll = nodes.First(n => n.Name == "GetAll");
        Assert.True(getAll.Tags.ContainsKey("http-endpoint"));
        Assert.True(getAll.Tags.ContainsKey("http-get"));
    }

    [Fact]
    public void AspNet_Authorize_tagged()
    {
        var nodes = ExtractCs(TestFixtures.CSharpController).ToList();
        AspNetRules.Create().Apply(nodes, []);
        var create = nodes.First(n => n.Name == "Create");
        Assert.True(create.Tags.ContainsKey("auth"));
    }

    [Fact]
    public void FastApi_app_get_tagged()
    {
        var nodes = ExtractPy(TestFixtures.PythonFastApi).ToList();
        FastApiRules.Create().Apply(nodes, []);
        var list = nodes.First(n => n.Name == "list_items");
        Assert.True(list.Tags.ContainsKey("http-endpoint"));
        Assert.True(list.Tags.ContainsKey("fastapi"));
        Assert.True(list.Tags.ContainsKey("http-get"));
    }

    [Fact]
    public void FastApi_app_post_tagged()
    {
        var nodes = ExtractPy(TestFixtures.PythonFastApi).ToList();
        FastApiRules.Create().Apply(nodes, []);
        var create = nodes.First(n => n.Name == "create_item");
        Assert.True(create.Tags.ContainsKey("http-post"));
    }

    [Fact]
    public void Language_specific_rule_takes_priority_over_generic()
    {
        var tsRule = new FingerprintRule("MyAttr", Language: "typescript", Tags: ["ts-specific"]);
        var genericRule = new FingerprintRule("MyAttr", Language: null, Tags: ["generic"]);
        var fp = new FrameworkFingerprinter("test", [genericRule, tsRule]);

        var node = new RaggableNode
        {
            Id = "1",
            Kind = UniversalNodeKind.Class,
            Name = "X",
            VirtualFilePath = "/r/a.ts",
            Range = new NodeRange(0, 1, 1, 1, 1),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "",
            Sha256 = "",
        };
        node.DecoratorsMutable.Add("@MyAttr()");

        fp.Apply([node], []);
        Assert.True(node.Tags.ContainsKey("ts-specific"));
        Assert.False(node.Tags.ContainsKey("generic"));
    }

    [Fact]
    public void OverrideKind_sets_overridden_kind()
    {
        var rule = new FingerprintRule("Special", Tags: ["x"], OverrideKind: UniversalNodeKind.Interface);
        var fp = new FrameworkFingerprinter("test", [rule]);

        var node = new RaggableNode
        {
            Id = "1",
            Kind = UniversalNodeKind.Class,
            Name = "X",
            VirtualFilePath = "/r/a.ts",
            Range = new NodeRange(0, 1, 1, 1, 1),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "",
            Sha256 = "",
        };
        node.DecoratorsMutable.Add("@Special");

        fp.Apply([node], []);
        Assert.Equal(UniversalNodeKind.Class, node.Kind);
        Assert.Equal(UniversalNodeKind.Interface, node.OverriddenKind);
        Assert.Equal(UniversalNodeKind.Interface, node.EffectiveKind);
    }

    [Fact]
    public void Node_without_known_decorator_gets_no_tag()
    {
        var node = new RaggableNode
        {
            Id = "1",
            Kind = UniversalNodeKind.Class,
            Name = "X",
            VirtualFilePath = "/r/a.ts",
            Range = new NodeRange(0, 1, 1, 1, 1),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "",
            Sha256 = "",
        };
        node.DecoratorsMutable.Add("@Unknown");

        NestJsRules.Create().Apply([node], []);
        Assert.Empty(node.Tags);
    }
}
