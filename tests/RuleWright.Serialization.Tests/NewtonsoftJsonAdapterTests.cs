using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RuleWright.Json.NewtonsoftJson;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

/// <summary>
/// The Newtonsoft.Json adapter must produce the same neutral DOM and the same dictionary facts as
/// the System.Text.Json adapter for the same JSON — the whole point of the adapter contract is
/// that the engine behaves identically whichever JSON library a consumer picks.
/// </summary>
public class NewtonsoftJsonAdapterTests
{
    private readonly NewtonsoftJsonReader _reader = new NewtonsoftJsonReader();

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
    public void Read_TrailingContent_ThrowsRuleParseException()
    {
        Assert.Throws<RuleParseException>(() => _reader.Read("{} garbage"));
    }

    [Fact]
    public void Read_CommentsAndTrailingCommas_AreTolerated()
    {
        RuleJsonValue root = _reader.Read("{\n// comment\n\"a\": 1,\n}");
        Assert.True(root.TryGetProperty("a", out _));
    }

    [Fact]
    public void Read_DateLikeString_StaysString()
    {
        // Newtonsoft's default DateParseHandling would coerce this to a DateTime; the adapter
        // disables that so it matches System.Text.Json (and the rule schema, which has no dates).
        RuleJsonValue root = _reader.Read("{\"when\": \"2021-06-15\"}");
        Assert.True(root.TryGetProperty("when", out RuleJsonValue when));
        Assert.Equal(RuleJsonValueKind.String, when.Kind);
        Assert.Equal("2021-06-15", when.GetString());
    }

    [Fact]
    public void ToClrValue_NumberPolicy_LongThenDecimalThenDouble()
    {
        Assert.IsType<long>(_reader.Read("[9223372036854775807]").Items[0].ToClrValue());
        Assert.IsType<decimal>(_reader.Read("[10.5]").Items[0].ToClrValue());
        Assert.IsType<double>(_reader.Read("[1e300]").Items[0].ToClrValue());
    }

    [Fact]
    public void IntegerValuedFloat_StaysDecimal_LikeSystemTextJson()
    {
        // "2.0" is a float token: it must not collapse to a long, exactly as System.Text.Json
        // keeps it decimal.
        Assert.IsType<decimal>(_reader.Read("[2.0]").Items[0].ToClrValue());
        Assert.Equal(2.0m, _reader.Read("[2.0]").Items[0].ToClrValue());
    }

    public static IEnumerable<object[]> ParitySamples() => new[]
    {
        new object[] { "{\"a\":1,\"b\":\"x\",\"c\":[true,false,null],\"d\":{\"e\":10.5}}" },
        new object[] { "[1, 2.0, 10.5, -3, 0, 9223372036854775807, 100.00, 0.1]" },
        new object[] { "{\"n\":null,\"t\":true,\"f\":false,\"s\":\"hello\",\"when\":\"2021-06-15\"}" },
        new object[] { "{\"nested\":{\"arr\":[{\"x\":1},{\"y\":\"z\"},[1,2,3]]}}" },
        new object[] { "1e300" },
        new object[] { "\"just a string\"" },
        new object[] { "42" },
        new object[] { "true" },
        new object[] { "null" },
    };

    [Theory]
    [MemberData(nameof(ParitySamples))]
    public void Reader_ProducesSameTreeAsSystemTextJson(string json)
    {
        RuleJsonValue systemText = new SystemTextJsonReader().Read(json);
        RuleJsonValue newtonsoft = _reader.Read(json);
        AssertTreesEqual(systemText, newtonsoft);
    }

    public static IEnumerable<object[]> ObjectParitySamples()
    {
        foreach (object[] sample in ParitySamples())
        {
            if (((string)sample[0]).TrimStart().StartsWith("{"))
            {
                yield return sample;
            }
        }
    }

    [Theory]
    [MemberData(nameof(ObjectParitySamples))]
    public void Facts_MatchSystemTextJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, object?> systemText = SystemTextJsonFacts.ToDictionary(document.RootElement);
        Dictionary<string, object?> newtonsoft = NewtonsoftJsonFacts.ToDictionary(ParseNewtonsoft(json));
        AssertClrEqual(systemText, newtonsoft);
    }

    private static JToken ParseNewtonsoft(string json)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(jsonReader);
    }

    private static void AssertTreesEqual(RuleJsonValue expected, RuleJsonValue actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        switch (expected.Kind)
        {
            case RuleJsonValueKind.Object:
                Assert.Equal(expected.Properties.Count, actual.Properties.Count);
                for (int i = 0; i < expected.Properties.Count; i++)
                {
                    Assert.Equal(expected.Properties[i].Key, actual.Properties[i].Key);
                    AssertTreesEqual(expected.Properties[i].Value, actual.Properties[i].Value);
                }

                break;

            case RuleJsonValueKind.Array:
                Assert.Equal(expected.Items.Count, actual.Items.Count);
                for (int i = 0; i < expected.Items.Count; i++)
                {
                    AssertTreesEqual(expected.Items[i], actual.Items[i]);
                }

                break;

            default:
                // Value and CLR type (Assert.Equal on boxed values is type-sensitive: 1L != 1m).
                Assert.Equal(expected.ToClrValue(), actual.ToClrValue());
                break;
        }
    }

    private static void AssertClrEqual(object? expected, object? actual)
    {
        switch (expected)
        {
            case Dictionary<string, object?> expectedMap:
                var actualMap = Assert.IsType<Dictionary<string, object?>>(actual);
                Assert.Equal(expectedMap.Count, actualMap.Count);
                foreach (KeyValuePair<string, object?> entry in expectedMap)
                {
                    Assert.True(actualMap.ContainsKey(entry.Key), $"Missing key '{entry.Key}'.");
                    AssertClrEqual(entry.Value, actualMap[entry.Key]);
                }

                break;

            case object?[] expectedItems:
                var actualItems = Assert.IsType<object?[]>(actual);
                Assert.Equal(expectedItems.Length, actualItems.Length);
                for (int i = 0; i < expectedItems.Length; i++)
                {
                    AssertClrEqual(expectedItems[i], actualItems[i]);
                }

                break;

            default:
                Assert.Equal(expected, actual);
                break;
        }
    }
}
