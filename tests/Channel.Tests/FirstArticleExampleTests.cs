namespace Channel.Tests;

public class FirstArticleExampleTests
{
    [Fact]
    public void PrimaryConstructorChangesFieldObservationButNotTheCapturedArgument()
    {
        var regular = new Regular(3);
        var primary = new Primary(3);
        Assert.Equal(3, regular.ObservedArgument);
        Assert.Null(regular.ObservedField);
        Assert.Equal(3, primary.ObservedArgument);
        Assert.Equal(3, primary.ObservedField);
    }

    private abstract class Probe
    {
        protected Probe(Func<int?> getArgument)
        {
            ObservedArgument = getArgument();
            ObservedField = Value;
        }
        public int? ObservedArgument { get; }
        public int? ObservedField { get; }
        protected abstract int? Value { get; }
    }

    private sealed class Regular : Probe
    {
        private readonly int? count;
        public Regular(int? count) : base(() => count) { this.count = count; }
        protected override int? Value => count;
    }

    private sealed class Primary(int? count) : Probe(() => count)
    {
        protected override int? Value => count;
    }
}
