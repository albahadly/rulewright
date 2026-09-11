using System;

namespace RuleWright.Execution.Tests;

public sealed class OrderFact
{
    public Customer Customer { get; set; } = new Customer();

    public Order? Order { get; set; }

    // Deliberately a public field (not a property) to prove field access works.
    public string? Tag;
}

public sealed class Customer
{
    public int Age { get; set; }

    public bool IsVip { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public int? LoyaltyYears { get; set; }

    public CustomerTier Tier { get; set; }

    public DateTime JoinedOn { get; set; }

    public Address? Address { get; set; }
}

public enum CustomerTier
{
    Bronze,
    Silver,
    Gold,
}

public sealed class Address
{
    public string City { get; set; } = string.Empty;
}

public sealed class Order
{
    public decimal Total { get; set; }

    public double Weight { get; set; }

    public string? Coupon { get; set; }

    public int ItemCount { get; set; }
}
