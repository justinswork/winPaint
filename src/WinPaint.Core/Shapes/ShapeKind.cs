namespace WinPaint.Core.Shapes;

/// <summary>The 23 shapes of the Shapes gallery.</summary>
public enum ShapeKind
{
    /// <summary>Straight line.</summary>
    Line,

    /// <summary>Bezier curve bent by up to two extra clicks.</summary>
    Curve,

    /// <summary>Ellipse.</summary>
    Oval,

    /// <summary>Rectangle.</summary>
    Rectangle,

    /// <summary>Rounded rectangle.</summary>
    RoundedRectangle,

    /// <summary>Click-by-click polygon.</summary>
    Polygon,

    /// <summary>Isosceles triangle.</summary>
    Triangle,

    /// <summary>Right triangle.</summary>
    RightTriangle,

    /// <summary>Diamond.</summary>
    Diamond,

    /// <summary>Pentagon.</summary>
    Pentagon,

    /// <summary>Hexagon.</summary>
    Hexagon,

    /// <summary>Right arrow.</summary>
    RightArrow,

    /// <summary>Left arrow.</summary>
    LeftArrow,

    /// <summary>Up arrow.</summary>
    UpArrow,

    /// <summary>Down arrow.</summary>
    DownArrow,

    /// <summary>Four-point star.</summary>
    FourPointStar,

    /// <summary>Five-point star.</summary>
    FivePointStar,

    /// <summary>Six-point star.</summary>
    SixPointStar,

    /// <summary>Rounded rectangular callout.</summary>
    RoundedRectCallout,

    /// <summary>Oval callout.</summary>
    OvalCallout,

    /// <summary>Cloud callout.</summary>
    CloudCallout,

    /// <summary>Heart.</summary>
    Heart,

    /// <summary>Lightning bolt.</summary>
    Lightning,
}

/// <summary>Outline and fill styles.</summary>
public enum ShapeStyle
{
    /// <summary>Not drawn.</summary>
    None,

    /// <summary>Solid color.</summary>
    Solid,

    /// <summary>Crayon texture.</summary>
    Crayon,

    /// <summary>Marker texture.</summary>
    Marker,

    /// <summary>Oil texture.</summary>
    Oil,

    /// <summary>Natural pencil texture.</summary>
    NaturalPencil,

    /// <summary>Watercolor texture.</summary>
    Watercolor,
}
