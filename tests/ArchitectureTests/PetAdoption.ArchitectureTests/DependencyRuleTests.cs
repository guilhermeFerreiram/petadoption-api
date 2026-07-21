using System.Reflection;
using NetArchTest.Rules;

namespace PetAdoption.ArchitectureTests;

/// <summary>
/// Enforce the module/layer dependency rules documented in CLAUDE.md.
/// Modules currently have no domain types, so these checks pass vacuously today
/// and start enforcing as soon as real classes land in future cards.
/// </summary>
public class DependencyRuleTests
{
    private const string BuildingBlocksDomain = "BuildingBlocks.Domain";
    private const string BuildingBlocksMessaging = "BuildingBlocks.Messaging";

    private static readonly string[] Modules = ["Identity", "PetPublishing", "InterestMatching"];

    private static readonly Dictionary<string, string[]> ContractsByModule = new()
    {
        ["PetPublishing"] = ["PetPublishing.Contracts"],
        ["InterestMatching"] = ["InterestMatching.Contracts"],
    };

    public static IEnumerable<object[]> AllModules => Modules.Select(m => new object[] { m });

    private static IEnumerable<string> AllProjectNames()
    {
        yield return BuildingBlocksDomain;
        yield return BuildingBlocksMessaging;

        foreach (var module in Modules)
        {
            yield return $"{module}.Domain";
            yield return $"{module}.Application";
            yield return $"{module}.Infrastructure";

            if (ContractsByModule.TryGetValue(module, out var contracts))
            {
                foreach (var contract in contracts)
                {
                    yield return contract;
                }
            }
        }
    }

    private static IEnumerable<string> ContractsOfOtherModules(string module) =>
        ContractsByModule.Where(kv => kv.Key != module).SelectMany(kv => kv.Value);

    private static Assembly LoadAssembly(string name) => Assembly.Load(name);

    private static string[] Forbidden(IEnumerable<string> allowed) =>
        AllProjectNames().Except(allowed).ToArray();

    private static void AssertNoDependency(Assembly assembly, string[] forbidden)
    {
        var result = Types
            .InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} has a forbidden dependency: "
                + string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? [])
        );
    }

    [Theory]
    [Trait("Category", "Architecture")]
    [MemberData(nameof(AllModules))]
    public void Domain_ShouldNotReferenceAnythingBesidesBuildingBlocksDomain(string module)
    {
        var domainAssembly = LoadAssembly($"{module}.Domain");
        var allowed = new[] { BuildingBlocksDomain, $"{module}.Domain" };

        AssertNoDependency(domainAssembly, Forbidden(allowed));
    }

    [Theory]
    [Trait("Category", "Architecture")]
    [MemberData(nameof(AllModules))]
    public void Application_ShouldOnlyDependOnOwnModuleDomain(string module)
    {
        var applicationAssembly = LoadAssembly($"{module}.Application");
        var allowed = new[] { $"{module}.Domain", $"{module}.Application" };

        AssertNoDependency(applicationAssembly, Forbidden(allowed));
    }

    [Theory]
    [Trait("Category", "Architecture")]
    [MemberData(nameof(AllModules))]
    public void Infrastructure_ShouldOnlyDependOnOwnModuleAndContractsOfOtherModules(string module)
    {
        var infrastructureAssembly = LoadAssembly($"{module}.Infrastructure");
        var allowed = new[]
        {
            $"{module}.Domain",
            $"{module}.Application",
            $"{module}.Infrastructure",
        }
            .Concat(ContractsOfOtherModules(module))
            .ToArray();

        AssertNoDependency(infrastructureAssembly, Forbidden(allowed));
    }

    [Theory]
    [Trait("Category", "Architecture")]
    [MemberData(nameof(AllModules))]
    public void Module_ShouldNeverDependOnDomainApplicationOrInfrastructureOfAnotherModule(
        string module
    )
    {
        var otherModulesLayers = Modules
            .Where(m => m != module)
            .SelectMany(m => new[] { $"{m}.Domain", $"{m}.Application", $"{m}.Infrastructure" })
            .ToArray();

        foreach (var layer in new[] { "Domain", "Application", "Infrastructure" })
        {
            var assembly = LoadAssembly($"{module}.{layer}");
            AssertNoDependency(assembly, otherModulesLayers);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void OnlyHost_ShouldDependOnInfrastructureOfAllModules()
    {
        var infrastructureAssemblies = Modules.Select(m => $"{m}.Infrastructure").ToArray();

        var nonHostProjects = AllProjectNames().Except(infrastructureAssemblies);

        foreach (var projectName in nonHostProjects)
        {
            var assembly = LoadAssembly(projectName);
            AssertNoDependency(assembly, infrastructureAssemblies);
        }
    }
}
