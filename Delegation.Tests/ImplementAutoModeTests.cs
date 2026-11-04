using Macaron.InterfaceDelegation;

using static Macaron.Delegation.Tests.Helper;

namespace Macaron.Delegation.Tests;

[TestFixture]
public sealed class ImplementAutoModeTests
{
    [Test]
    public void UsesAutoAsAttributeDefault()
    {
        Assert.That(new ImplementAttribute().Mode, Is.EqualTo(ImplementationMode.Auto));
    }

    [TestCase(ImplementationMode.Auto, 0)]
    [TestCase(ImplementationMode.Implicit, 1)]
    [TestCase(ImplementationMode.Explicit, 2)]
    public void ImplementationMode_PreservesNumericValue(ImplementationMode mode, int expectedValue)
    {
        Assert.That((int)mode, Is.EqualTo(expectedValue));
    }

    [TestCase("Implement")]
    [TestCase("Implement(Mode = ImplementationMode.Auto)")]
    public void OverridesAbstractBaseMembers_AndExplicitlyImplementsOtherMembers(string attribute)
    {
        var result = RunGenerator(
            $$"""
            using System;
            using Macaron.InterfaceDelegation;

            public interface IContract
            {
                int GetValue();

                int Value { get; }

                event EventHandler? Changed;

                void Run();
            }

            public sealed class Implementation : IContract
            {
                public int GetValue() => 42;

                public int Value => 42;

                public event EventHandler? Changed;

                public void Run() { }
            }

            public abstract class Base
            {
                public abstract int GetValue();

                public abstract int Value { get; }

                public abstract event EventHandler? Changed;
            }

            public partial class Wrapper : Base, IContract
            {
                [{{attribute}}]
                private readonly IContract _impl = new Implementation();
            }
            """
        );

        AssertSuccessfulGeneration(result);
        Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));

        var generated = result.GeneratedSources[0].SourceText.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("public override int GetValue()"));
            Assert.That(generated, Does.Contain("public override int Value"));
            Assert.That(generated, Does.Contain("public override event global::System.EventHandler? Changed"));
            Assert.That(generated, Does.Contain("void global::IContract.Run()"));
        });
    }
}
