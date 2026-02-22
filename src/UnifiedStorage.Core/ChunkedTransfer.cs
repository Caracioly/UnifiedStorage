using System;

namespace UnifiedStorage.Core;

public static class ChunkedTransfer
{
    public static int ClampMeasuredMove(int requestedAmount, int beforeAmount, int afterAmount)
    {
        if (requestedAmount <= 0)
        {
            return 0;
        }

        var delta = afterAmount - beforeAmount;
        if (delta <= 0)
        {
            return 0;
        }

        return Math.Min(requestedAmount, delta);
    }

    public static int Move(int requestedAmount, int maxChunkSize, Func<int, int> moveChunk)
    {
        if (moveChunk == null)
        {
            throw new ArgumentNullException(nameof(moveChunk));
        }

        if (requestedAmount <= 0 || maxChunkSize <= 0)
        {
            return 0;
        }

        var remaining = requestedAmount;
        var movedTotal = 0;
        while (remaining > 0)
        {
            var chunkSize = Math.Min(maxChunkSize, remaining);
            var moved = moveChunk(chunkSize);
            if (moved <= 0)
            {
                break;
            }

            if (moved > chunkSize)
            {
                moved = chunkSize;
            }

            movedTotal += moved;
            remaining -= moved;
            if (moved < chunkSize)
            {
                break;
            }
        }

        return movedTotal;
    }
}
