using Microsoft.CodeAnalysis;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class ImplementDispatchTests
{
    [TestCase("int Value { get; }", "public int Value { get; private set; }")]
    [TestCase("int Value { set; }", "public int Value { private get; set; }")]
    public void RequiresAccessibilityOnlyForContractAccessors(string contract, string implementation)
    {
        var result = RunGenerator(
            $$"""
            using Macaron.InterfaceDelegation;

            public interface ITarget
            {
                {{contract}}
            }

            public class Target
            {
                {{implementation}}
            }

            public partial class Wrapper : ITarget
            {
                [Implement(typeof(ITarget))]
                private readonly Target _target = new();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.Diagnostics, Has.None.Matches<Diagnostic>(static diagnostic => diagnostic.Id == "MAID0005"));
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
    }

    [Test]
    public void UsesNominalContractFastPath_ForDefaultInterfaceImplementation()
    {
        const string sourceCode =
            """
            using Macaron.InterfaceDelegation;

            namespace Macaron.Delegation.Tests;

            public interface IFoo
            {
                void Run() { }
            }

            public sealed class Foo : IFoo
            {
            }

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo), Mode = ImplementationMode.Implicit)]
                private readonly Foo _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("where __T : global::Macaron.Delegation.Tests.IFoo"));
        });
    }

    [Test]
    public void UsesInterfaceDispatch_WhenBaseTypeImplementsInterfaceExplicitly()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;

            public interface IFoo
            {
                void Run();
            }

            public class FooBase : IFoo
            {
                void IFoo.Run() { }
            }

            public sealed class Foo : FooBase
            {
            }

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo), Mode = ImplementationMode.Implicit)]
                private readonly Foo _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("__Run(in _impl)"));
        });
    }

    [Test]
    public void UsesIndexedContractValidation_ForDuckTypedTarget()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;

            public interface IFoo
            {
                void Run(int value);

                int Value { get; }

                int Map(int value);

                string Map(string value);
            }

            public sealed class DuckFoo
            {
                public void Run(int value) { }

                public int Value => 42;

                public int Map(int value) => value;

                public string Map(string value) => value;
            }

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo), Mode = ImplementationMode.Implicit)]
                private readonly DuckFoo _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("=> _impl.Run(value);"));
        });
    }

    [Test]
    public void IgnoresPrivateDefaultInterfaceHelpers()
    {
        const string sourceCode =
            """
            namespace Macaron.Delegation.Tests;
            using Macaron.InterfaceDelegation;

            public interface IFoo
            {
                void Run();

                private void Helper() { }
            }

            public sealed class Foo : IFoo
            {
                public void Run() { }
            }

            public partial class Wrapper : IFoo
            {
                [Implement(typeof(IFoo), Mode = ImplementationMode.Implicit)]
                private readonly Foo _impl = new();
            }
            """;

        var result = RunGenerator(sourceCode);

        AssertSuccessfulGeneration(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Contain("__Run(in _impl)"));
            Assert.That(result.GeneratedSources[0].SourceText.ToString(), Does.Not.Contain("Helper"));
        });
    }
}
