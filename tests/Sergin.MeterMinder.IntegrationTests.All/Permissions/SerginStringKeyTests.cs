using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

public sealed class SerginStringKeyTests
{
    [Fact]
    public void Create_ConvertsPascalCaseToKebabCase() =>
        Assert.Equal("test.device-list.read-all", TestKey.Create("test.DeviceList.readAll").Value);

    [Theory]
    [InlineData("")]
    [InlineData("test..read")]
    [InlineData("test.read.")]
    [InlineData("test.read-")]
    [InlineData("test.read--all")]
    [InlineData("test.v1")]
    [InlineData("test.read_all")]
    [InlineData("test.read all")]
    public void Create_RefusesAMalformedKey(string value) =>
        Assert.Throws<ArgumentException>(() => TestKey.Create(value));

    [Fact]
    public void Create_RefusesTheWrongFirstSegment() =>
        Assert.Throws<ArgumentException>(() => TestKey.Create("other.devices"));

    [Fact]
    public void Create_RefusesTooFewSegments() =>
        Assert.Throws<ArgumentException>(() => TestKey.Create("test"));

    [Fact]
    public void Create_RefusesATooLongKey() =>
        Assert.Throws<ArgumentException>(() => TestKey.Create("test." + new string('a', TestKey.MaxLength)));

    [Fact]
    public void TryCreate_AnswersFalseInsteadOfThrowing()
    {
        Assert.False(TestKey.TryCreate("not a key", out TestKey? malformed));
        Assert.Null(malformed);
        Assert.False(TestKey.TryCreate(null, out _));
        Assert.True(TestKey.TryCreate("test.devices", out TestKey? key));
        Assert.Equal("test.devices", key.Value);
    }

    [Fact]
    public void Segments_SplitsOnTheSeparator() =>
        Assert.Equal(["test", "devices", "read"], TestKey.Create("test.devices.read").Segments);

    [Fact]
    public void ToString_IsTheValue() =>
        Assert.Equal("test.devices", TestKey.Create("test.devices").ToString());

    [Fact]
    public void Equality_IsByValueAndKeyType()
    {
        Assert.Equal(TestKey.Create("test.devices"), TestKey.Create("test.Devices"));
        Assert.NotEqual<SerginStringKey>(OtherKey.Create("test.devices"), TestKey.Create("test.devices"));
    }

    [Fact]
    public void Permission_RefusesAMissingSchema() =>
        Assert.Throws<ArgumentException>(() => Permission.Create("permission.dm"));

    [Fact]
    public void Permission_RefusesAnotherFirstSegment() =>
        Assert.Throws<ArgumentException>(() => Permission.Create("grant.dm.devices.read"));

    [Fact]
    public void Permission_KeepsItsStoredShape()
    {
        var permission = Permission.Create("permission.dm.DeviceModels.read");

        Assert.Equal("permission.dm.device-models.read", permission.Value);
        Assert.Equal("permission.dm.device-models.read", (string)permission);
        Assert.Equal("permission.sys.platform-all", Permission.AllPlatform.Value);
    }

    private sealed record TestKey : SerginStringKey
    {
        public const int MaxLength = 40;

        private static readonly SerginStringKeyRules rules = new("test", 2, MaxLength);

        private TestKey(string value)
            : base(value)
        {
        }

        public static TestKey Create(string value) => new(Normalize(value, rules, nameof(TestKey)));

        public static bool TryCreate(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TestKey? key) =>
            TryCreate(value, Create, out key);
    }

    private sealed record OtherKey : SerginStringKey
    {
        private static readonly SerginStringKeyRules rules = new(null, 1, 40);

        private OtherKey(string value)
            : base(value)
        {
        }

        public static OtherKey Create(string value) => new(Normalize(value, rules, nameof(OtherKey)));
    }
}
