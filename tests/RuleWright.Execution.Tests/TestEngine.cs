using System;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;

namespace RuleWright.Execution.Tests;

internal static class TestEngine
{
    internal static readonly RuleWrightEngine Engine = new RuleWrightBuilder()
        .UseJsonReader(new SystemTextJsonReader())
        .RegisterFunction("AlwaysTrue", (fieldValue, value) => true)
        .RegisterFunction("FieldIsFortyTwo", (fieldValue, value) =>
            fieldValue is int i ? i == 42 : fieldValue is long l && l == 42)
        .RegisterBuiltInFunctions()
        .Build();

    internal static string WrapRule(string conditionJson)
        => "{\"id\":\"test-rule\",\"condition\":" + conditionJson + "}";

    /// <summary>Loads a single-condition rule and reports whether it fires for the fact.</summary>
    internal static bool Matches<TFact>(string conditionJson, TFact fact)
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(conditionJson));
        return Engine.Evaluate(loaded, fact).FiredRules.Count == 1;
    }

    internal static OrderFact DefaultFact() => new OrderFact
    {
        Customer = new Customer
        {
            Age = 21,
            IsVip = true,
            Name = "Alice",
            Email = "alice@example.com",
            LoyaltyYears = 3,
            Tier = CustomerTier.Gold,
            JoinedOn = new DateTime(2021, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Address = new Address { City = "Amsterdam" },
        },
        Order = new Order { Total = 120.50m, Weight = 2.4, Coupon = "SPRING", ItemCount = 3 },
        Tag = "priority",
    };
}
