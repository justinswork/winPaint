namespace WinPaint.Core.Imaging;

/// <summary>A horizontal run of filled pixels.</summary>
/// <param name="Y">Row.</param>
/// <param name="X0">First x (inclusive).</param>
/// <param name="X1">Last x (inclusive).</param>
public readonly record struct Span(int Y, int X0, int X1);

/// <summary>Scanline flood fill: 4-connected, exact color match (tolerance 0, like Paint).</summary>
public static class FloodFill
{
    /// <summary>
    /// Finds the 4-connected region of pixels equal to the seed pixel. Returns the spans and their bounds.
    /// </summary>
    public static (List<Span> Spans, PixelRect Bounds) Region(uint[] pixels, int width, int height, int seedX, int seedY)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var spans = new List<Span>();
        if ((uint)seedX >= (uint)width || (uint)seedY >= (uint)height)
        {
            return (spans, PixelRect.Empty);
        }

        var target = pixels[(seedY * width) + seedX];
        var visited = new bool[pixels.Length];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((seedX, seedY));
        int minX = seedX, maxX = seedX, minY = seedY, maxY = seedY;
        while (stack.Count > 0)
        {
            var (sx, y) = stack.Pop();
            var row = y * width;
            if (visited[row + sx] || pixels[row + sx] != target)
            {
                continue;
            }

            var x0 = sx;
            while (x0 > 0 && !visited[row + x0 - 1] && pixels[row + x0 - 1] == target)
            {
                x0--;
            }

            var x1 = sx;
            while (x1 < width - 1 && !visited[row + x1 + 1] && pixels[row + x1 + 1] == target)
            {
                x1++;
            }

            visited.AsSpan(row + x0, x1 - x0 + 1).Fill(true);
            spans.Add(new Span(y, x0, x1));
            minX = Math.Min(minX, x0);
            maxX = Math.Max(maxX, x1);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            if (y > 0)
            {
                PushRuns(pixels, visited, target, (y - 1) * width, x0, x1, y - 1, stack);
            }

            if (y < height - 1)
            {
                PushRuns(pixels, visited, target, (y + 1) * width, x0, x1, y + 1, stack);
            }
        }

        return (spans, PixelRect.FromEdges(minX, minY, maxX + 1, maxY + 1));
    }

    private static void PushRuns(uint[] pixels, bool[] visited, uint target, int row, int x0, int x1, int y, Stack<(int, int)> stack)
    {
        var inRun = false;
        for (var x = x0; x <= x1; x++)
        {
            var match = !visited[row + x] && pixels[row + x] == target;
            if (match && !inRun)
            {
                stack.Push((x, y));
                inRun = true;
            }
            else if (!match)
            {
                inRun = false;
            }
        }
    }
}
