namespace WinPaint.Core.Imaging;

/// <summary>Integer pixel rectangle (half-open: covers X..X+Width-1, Y..Y+Height-1).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>The empty rectangle.</summary>
    public static PixelRect Empty => default;

    /// <summary>Right edge (exclusive).</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge (exclusive).</summary>
    public int Bottom => Y + Height;

    /// <summary>True when the rectangle covers no pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Number of pixels covered.</summary>
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    /// <summary>Creates a rectangle from edges.</summary>
    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        right <= left || bottom <= top ? Empty : new PixelRect(left, top, right - left, bottom - top);

    /// <summary>Intersection of two rectangles.</summary>
    public PixelRect Intersect(PixelRect o) =>
        IsEmpty || o.IsEmpty ? Empty : FromEdges(Math.Max(X, o.X), Math.Max(Y, o.Y), Math.Min(Right, o.Right), Math.Min(Bottom, o.Bottom));

    /// <summary>Smallest rectangle containing both.</summary>
    public PixelRect Union(PixelRect o)
    {
        if (IsEmpty)
        {
            return o;
        }

        if (o.IsEmpty)
        {
            return this;
        }

        return FromEdges(Math.Min(X, o.X), Math.Min(Y, o.Y), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));
    }

    /// <summary>True when the point lies inside.</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    /// <summary>True when the rectangles overlap.</summary>
    public bool IntersectsWith(PixelRect o) => !Intersect(o).IsEmpty;

    /// <summary>Inflates all edges by d.</summary>
    public PixelRect Inflate(int d) => FromEdges(X - d, Y - d, Right + d, Bottom + d);

    /// <summary>Offsets the rectangle.</summary>
    public PixelRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Smallest integer rectangle covering a floating-point rect.</summary>
    public static PixelRect FromRect(System.Windows.Rect r)
    {
        if (r.IsEmpty)
        {
            return Empty;
        }

        return FromEdges((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
    }

    /// <summary>Converts to a WPF rect.</summary>
    public System.Windows.Rect ToRect() => IsEmpty ? System.Windows.Rect.Empty : new System.Windows.Rect(X, Y, Width, Height);
}
