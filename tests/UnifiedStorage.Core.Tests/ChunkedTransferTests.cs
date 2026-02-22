using System;
using System.Collections.Generic;
using UnifiedStorage.Core;
using Xunit;

namespace UnifiedStorage.Core.Tests;

public class ChunkedTransferTests
{
    [Theory]
    [InlineData(-1, 10, 20, 0)]
    [InlineData(0, 10, 20, 0)]
    [InlineData(5, 10, 10, 0)]
    [InlineData(5, 10, 9, 0)]
    [InlineData(5, 10, 12, 2)]
    [InlineData(5, 10, 20, 5)]
    [InlineData(1, 3, 5, 1)]
    public void ClampMeasuredMove_HandlesAllBoundaries(int requested, int before, int after, int expected)
    {
        var moved = ChunkedTransfer.ClampMeasuredMove(requested, before, after);
        Assert.Equal(expected, moved);
    }

    [Fact]
    public void ClampMeasuredMove_ExhaustiveSmallRange()
    {
        for (var requested = -2; requested <= 8; requested++)
        {
            for (var before = -2; before <= 8; before++)
            {
                for (var after = -2; after <= 8; after++)
                {
                    var expected = requested <= 0 ? 0 : Math.Max(0, Math.Min(requested, after - before));
                    var moved = ChunkedTransfer.ClampMeasuredMove(requested, before, after);
                    Assert.Equal(expected, moved);
                }
            }
        }
    }

    [Fact]
    public void Move_ReturnsZero_WhenRequestedIsNotPositive()
    {
        var called = false;
        var moved = ChunkedTransfer.Move(0, 999, _ =>
        {
            called = true;
            return 1;
        });

        Assert.Equal(0, moved);
        Assert.False(called);
    }

    [Fact]
    public void Move_ReturnsZero_WhenMaxChunkIsNotPositive()
    {
        var called = false;
        var moved = ChunkedTransfer.Move(10, 0, _ =>
        {
            called = true;
            return 1;
        });

        Assert.Equal(0, moved);
        Assert.False(called);
    }

    [Fact]
    public void Move_RequestsExpectedChunks_WhenAllCallsAreFull()
    {
        var requested = new List<int>();
        var moved = ChunkedTransfer.Move(2500, 999, chunk =>
        {
            requested.Add(chunk);
            return chunk;
        });

        Assert.Equal(2500, moved);
        Assert.Equal(new[] { 999, 999, 502 }, requested);
    }

    [Fact]
    public void Move_StopsWhenCallbackReturnsZero()
    {
        var requested = new List<int>();
        var index = 0;
        var responses = new[] { 999, 0 };
        var moved = ChunkedTransfer.Move(1500, 999, chunk =>
        {
            requested.Add(chunk);
            return responses[index++];
        });

        Assert.Equal(999, moved);
        Assert.Equal(new[] { 999, 501 }, requested);
    }

    [Fact]
    public void Move_StopsWhenCallbackReturnsPartial()
    {
        var requested = new List<int>();
        var moved = ChunkedTransfer.Move(1400, 999, chunk =>
        {
            requested.Add(chunk);
            return 700;
        });

        Assert.Equal(700, moved);
        Assert.Equal(new[] { 999 }, requested);
    }

    [Fact]
    public void Move_ClampsOverReportedMove()
    {
        var requested = new List<int>();
        var moved = ChunkedTransfer.Move(120, 50, chunk =>
        {
            requested.Add(chunk);
            return 500;
        });

        Assert.Equal(120, moved);
        Assert.Equal(new[] { 50, 50, 20 }, requested);
    }

    [Fact]
    public void Move_ExhaustiveSmallRanges_MatchesReferenceModel()
    {
        const int responseCount = 5;
        var responses = new int[responseCount];

        for (var requested = 0; requested <= 8; requested++)
        {
            for (var maxChunk = 1; maxChunk <= 4; maxChunk++)
            {
                var responseRange = maxChunk + 2; // Includes "over-reported move" cases.
                var combinations = IntPow(responseRange, responseCount);
                for (var mask = 0; mask < combinations; mask++)
                {
                    FillResponses(mask, responseRange, responses);

                    var responseIndex = 0;
                    var actualRequested = new List<int>();
                    var actualMoved = ChunkedTransfer.Move(requested, maxChunk, chunk =>
                    {
                        actualRequested.Add(chunk);
                        var value = responseIndex < responses.Length ? responses[responseIndex] : 0;
                        responseIndex++;
                        return value;
                    });

                    var expectedMoved = ReferenceMove(requested, maxChunk, responses, out var expectedRequested);
                    Assert.Equal(expectedMoved, actualMoved);
                    Assert.Equal(expectedRequested, actualRequested);
                }
            }
        }
    }

    private static int ReferenceMove(int requestedAmount, int maxChunkSize, IReadOnlyList<int> responses, out List<int> requestedChunks)
    {
        requestedChunks = new List<int>();
        if (requestedAmount <= 0 || maxChunkSize <= 0)
        {
            return 0;
        }

        var movedTotal = 0;
        var remaining = requestedAmount;
        var responseIndex = 0;
        while (remaining > 0)
        {
            var chunk = Math.Min(maxChunkSize, remaining);
            requestedChunks.Add(chunk);

            var moved = responseIndex < responses.Count ? responses[responseIndex] : 0;
            responseIndex++;
            if (moved <= 0)
            {
                break;
            }

            if (moved > chunk)
            {
                moved = chunk;
            }

            movedTotal += moved;
            remaining -= moved;
            if (moved < chunk)
            {
                break;
            }
        }

        return movedTotal;
    }

    private static void FillResponses(int value, int baseValue, int[] responses)
    {
        for (var i = 0; i < responses.Length; i++)
        {
            responses[i] = value % baseValue;
            value /= baseValue;
        }
    }

    private static int IntPow(int baseValue, int exponent)
    {
        var result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= baseValue;
        }

        return result;
    }
}
