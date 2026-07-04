using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class UniversalSemanticMapperTests
{
    [Fact]
    public void Extracts_module_plus_class_methods_and_interface()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);

        var nodes = mapper.ExtractNodes("/repo/user.ts", TestFixtures.SimpleClass);

        Assert.Contains(nodes, n => n.Level == NodeLevel.L2_Module);
        Assert.Contains(nodes, n => n.Kind == UniversalNodeKind.Class && n.Name == "UserService");
        Assert.Contains(nodes, n => n.Kind == UniversalNodeKind.Method && n.Name == "create");
        Assert.Contains(nodes, n => n.Kind == UniversalNodeKind.Method && n.Name == "helper");
        Assert.Contains(nodes, n => n.Kind == UniversalNodeKind.Interface && n.Name == "IUserService");
    }

    [Fact]
    public void Builds_hierarchy_methods_under_class()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/repo/user.ts", TestFixtures.SimpleClass);

        var userService = nodes.First(n => n.Name == "UserService");
        var create = nodes.First(n => n.Name == "create");
        Assert.Equal(userService.Id, create.ParentId);
        Assert.Contains(create.Id, userService.ChildrenIds);
    }

    [Fact]
    public void Computes_fqn_with_symbol_chain()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/repo/user.ts", TestFixtures.SimpleClass);

        var create = nodes.First(n => n.Name == "create");
        Assert.Equal("/repo/user.ts::UserService::create", create.Fqn);
    }

    [Fact]
    public void Captures_modifiers_public_private()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/repo/user.ts", TestFixtures.SimpleClass);

        var create = nodes.First(n => n.Name == "create");
        Assert.Contains("public", create.Modifiers);
        var helper = nodes.First(n => n.Name == "helper");
        Assert.Contains("private", helper.Modifiers);
    }

    [Fact]
    public void Captures_decorators()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/repo/svc.ts", TestFixtures.Decorators);

        var service = nodes.First(n => n.Name == "Service");
        Assert.NotEmpty(service.Decorators);
        Assert.Contains(service.Decorators, d => d.Contains("Injectable"));
    }

    [Fact]
    public void Computes_deterministic_sha256_for_node_slice()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes1 = mapper.ExtractNodes("/repo/user.ts", TestFixtures.SimpleClass);
        var nodes2 = mapper.ExtractNodes("/repo/user.ts", TestFixtures.SimpleClass);
        var create1 = nodes1.First(n => n.Name == "create");
        var create2 = nodes2.First(n => n.Name == "create");
        Assert.Equal(create1.Sha256, create2.Sha256);
        Assert.Equal(64, create1.Sha256.Length);
    }

    [Fact]
    public void Reports_partial_parse_status_on_syntax_error()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/repo/broken.ts", TestFixtures.BrokenSyntax);
        var module = nodes.First(n => n.Level == NodeLevel.L2_Module);
        Assert.Equal(ParseStatus.Partial, module.ParseStatus);
    }

    [Fact]
    public void Empty_file_produces_only_module_node()
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new TypeScriptAdapter(), pool);
        var nodes = mapper.ExtractNodes("/repo/empty.ts", TestFixtures.Empty);
        Assert.Single(nodes);
        Assert.Equal(NodeLevel.L2_Module, nodes[0].Level);
    }
}
