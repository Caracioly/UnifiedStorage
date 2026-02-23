using System.Collections.Generic;
using System.Linq;
using UnifiedStorage.Core;
using Xunit;

namespace UnifiedStorage.Core.Tests;

public class StorageLogicTests
{
    [Fact]
    public void Aggregate_GroupsByItemKey_QualityAndVariantAware()
    {
        var keyQ1 = new ItemKey("SwordIron", 1, 0);
        var keyQ2 = new ItemKey("SwordIron", 2, 0);
        var source = new List<SourceStack>
        {
            new() { Key = keyQ1, DisplayName = "Iron Sword", Amount = 3, SourceId = "A", StackSize = 1 },
            new() { Key = keyQ1, DisplayName = "Iron Sword", Amount = 2, SourceId = "B", StackSize = 1 },
            new() { Key = keyQ2, DisplayName = "Iron Sword", Amount = 1, SourceId = "A", StackSize = 1 }
        };

        var aggregated = AggregationService.Aggregate(source);

        Assert.Equal(2, aggregated.Count);
        var q1 = aggregated.Single(i => i.Key.Equals(keyQ1));
        var q2 = aggregated.Single(i => i.Key.Equals(keyQ2));
        Assert.Equal(5, q1.TotalAmount);
        Assert.Equal(2, q1.SourceCount);
        Assert.Equal(1, q2.TotalAmount);
    }

    [Fact]
    public void Aggregate_UsesResolveMaxStackSize_WhenProvided()
    {
        var key = new ItemKey("Wood", 1, 0);
        var source = new List<SourceStack>
        {
            new() { Key = key, DisplayName = "Wood", Amount = 30, SourceId = "A", StackSize = 10 },
            new() { Key = key, DisplayName = "Wood", Amount = 20, SourceId = "B", StackSize = 10 }
        };

        var aggregated = AggregationService.Aggregate(source, prefab => prefab == "Wood" ? 50 : 1);

        Assert.Single(aggregated);
        Assert.Equal(50, aggregated[0].TotalAmount);
        Assert.Equal(50, aggregated[0].StackSize);
    }

    [Fact]
    public void Aggregate_FallsBackToSourceStackSize_WhenNoResolver()
    {
        var key = new ItemKey("Stone", 1, 0);
        var source = new List<SourceStack>
        {
            new() { Key = key, DisplayName = "Stone", Amount = 100, SourceId = "A", StackSize = 50 },
            new() { Key = key, DisplayName = "Stone", Amount = 60, SourceId = "B", StackSize = 30 }
        };

        var aggregated = AggregationService.Aggregate(source);

        Assert.Single(aggregated);
        Assert.Equal(160, aggregated[0].TotalAmount);
        Assert.Equal(50, aggregated[0].StackSize);
    }

    [Fact]
    public void CalculateVirtualSlots_SumsUpCorrectly()
    {
        var items = new List<AggregatedItem>
        {
            new() { Key = new ItemKey("Wood", 1, 0), TotalAmount = 100, StackSize = 50 },
            new() { Key = new ItemKey("Stone", 1, 0), TotalAmount = 75, StackSize = 50 },
            new() { Key = new ItemKey("Iron", 1, 0), TotalAmount = 1, StackSize = 30 }
        };

        var slots = AggregationService.CalculateVirtualSlots(items);

        Assert.Equal(5, slots);
    }

    [Fact]
    public void Search_FiltersCaseInsensitive()
    {
        var items = new List<AggregatedItem>
        {
            new() { DisplayName = "Iron Nails", TotalAmount = 100 },
            new() { DisplayName = "Fine Wood", TotalAmount = 50 }
        };

        var result = SearchService.Filter(items, "iron");

        Assert.Single(result.Items);
        Assert.Equal("Iron Nails", result.Items[0].DisplayName);
    }

    [Fact]
    public void WithdrawPlanner_UsesNearestContainersDeterministically()
    {
        var key = new ItemKey("Wood", 1, 0);
        var source = new List<SourceStack>
        {
            new() { Key = key, Amount = 5, Distance = 7.0f, SourceId = "ChestB" },
            new() { Key = key, Amount = 3, Distance = 2.0f, SourceId = "ChestA" },
            new() { Key = key, Amount = 4, Distance = 7.0f, SourceId = "ChestC" }
        };

        var plan = WithdrawPlanner.Plan(source, key, requestedAmount: 9, maxReceivable: 99);

        Assert.Equal(9, plan.PlannedAmount);
        Assert.Collection(plan.Takes,
            take =>
            {
                Assert.Equal("ChestA", take.SourceId);
                Assert.Equal(3, take.Amount);
            },
            take =>
            {
                Assert.Equal("ChestB", take.SourceId);
                Assert.Equal(5, take.Amount);
            },
            take =>
            {
                Assert.Equal("ChestC", take.SourceId);
                Assert.Equal(1, take.Amount);
            });
    }

    [Fact]
    public void WithdrawPlanner_RespectsInventoryCapacity()
    {
        var key = new ItemKey("Stone", 1, 0);
        var source = new List<SourceStack>
        {
            new() { Key = key, Amount = 20, Distance = 1f, SourceId = "ChestA" }
        };

        var plan = WithdrawPlanner.Plan(source, key, requestedAmount: 10, maxReceivable: 4);

        Assert.Equal(4, plan.PlannedAmount);
        Assert.Single(plan.Takes);
        Assert.Equal(4, plan.Takes[0].Amount);
    }

    [Fact]
    public void DepositPlanner_DistributesAcrossContainers()
    {
        var containers = new List<ContainerSlot>
        {
            new() { SourceId = "A", FreeSpace = 10, Distance = 5f },
            new() { SourceId = "B", FreeSpace = 15, Distance = 2f },
            new() { SourceId = "C", FreeSpace = 5, Distance = 8f }
        };

        var plan = DepositPlanner.Plan(containers, 20);

        Assert.Equal(20, plan.PlannedAmount);
        Assert.Equal(2, plan.Deposits.Count);
        Assert.Equal("B", plan.Deposits[0].SourceId);
        Assert.Equal(15, plan.Deposits[0].Amount);
        Assert.Equal("A", plan.Deposits[1].SourceId);
        Assert.Equal(5, plan.Deposits[1].Amount);
    }

    [Fact]
    public void DepositPlanner_ClampsToAvailableSpace()
    {
        var containers = new List<ContainerSlot>
        {
            new() { SourceId = "A", FreeSpace = 3, Distance = 1f }
        };

        var plan = DepositPlanner.Plan(containers, 10);

        Assert.Equal(3, plan.PlannedAmount);
        Assert.Single(plan.Deposits);
        Assert.Equal(3, plan.Deposits[0].Amount);
    }

    [Fact]
    public void DepositPlanner_ReturnsEmpty_WhenNoContainers()
    {
        var plan = DepositPlanner.Plan(new List<ContainerSlot>(), 10);

        Assert.Equal(0, plan.PlannedAmount);
        Assert.Empty(plan.Deposits);
    }
}
