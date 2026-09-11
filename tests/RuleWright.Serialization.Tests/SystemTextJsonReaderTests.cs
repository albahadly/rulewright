using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

public class SystemTextJsonReaderTests
{
    private readonly SystemTextJsonReader _reader = new SystemTextJsonReader();

    [Fact]
    public void Read_Object_PreservesStructureAndKinds()
    {
        RuleJsonValue root = _reader.Read("{\"a\": 1, \"b\": \"x\", \"c\": [true, null], \"d\": {\"e\": 10.5}}");

        Assert.Equal(RuleJsonValueKind.Object, root.Kind);
        Assert.True(root.TryGetProperty("a", out RuleJsonValue a));
        Assert.Equal(1L, a.ToClrValue());
        Assert.True(root.TryGetProperty("b", out RuleJsonValue b));
        Assert.Equal("x", b.ToClrValue());
        Assert.True(root.TryGetProperty("c", out RuleJsonValue c));
        Assert.Equal(RuleJsonValueKind.Array, c.Kind);
        Assert.True((bool)c.Items[0].ToClrValue()!);
        Assert.Null(c.Items[1].ToClrValue());
        Assert.True(root.TryGetProperty("d", out RuleJsonValue d));
        Assert.True(d.TryGetProperty("e", out RuleJsonValue e));
        Assert.Equal(10.5m, e.ToClrValue());
    }

    [Fact]
    public void Read_MalformedJson_ThrowsRuleParseException()
    {
        Assert.Throws<RuleParseException>(() => _reader.Read("{ not json"));
    }

    [Fact]
    public void Read_CommentsAndTrailingCommas_AreTolerated()
    {
        RuleJsonValue root = _reader.Read("{\n// comment\n\"a\": 1,\n}");
        Assert.True(root.TryGetProperty("a", out _));
    }

    [Fact]
    public void ToClrValue_NumberPolicy_LongThenDecimalThenDouble()
    {
        Assert.IsType<long>(_reader.Read("[9223372036854775807]").Items[0].ToClrValue());
        Assert.IsType<decimal>(_reader.Read("[10.5]").Items[0].ToClrValue());
        Assert.IsType<double>(_reader.Read("[1e300]").Items[0].ToClrValue());
    }

    /// <summary>
    /// An overflowing literal parses as infinity on .NET Core but fails outright on .NET
    /// Framework, so accepting it would make the same document load on one target framework and
    /// not another. Both refuse it, as the documented parse failure rather than a raw
    /// ArgumentException from an internal factory.
    /// </summary>
    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    public void OutOfRangeNumber_IsARuleParseException_OnEveryTargetFramework(string literal)
    {
        string json = "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"LessThan\",\"value\":" + literal + "}}";

        RuleParseException error = Assert.Throws<RuleParseException>(() => new SystemTextJsonReader().Read(json));
        Assert.Contains("finite", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Numbers at the edge of what the engine represents still load.</summary>
    [Theory]
    [InlineData("1e308")]
    [InlineData("-1e308")]
    [InlineData("0.000001")]
    public void LargeButFiniteNumbers_Load(string literal)
    {
        RuleJsonValue document = new SystemTextJsonReader().Read("{\"v\":" + literal + "}");
        Assert.True(document.TryGetProperty("v", out RuleJsonValue value));
        Assert.Equal(RuleJsonValueKind.Number, value.Kind);
    }
}
