namespace Caustikon.Site.Components;

/// <summary>One line of a chart: a label, its points, and the CSS colour it is drawn in.</summary>
public sealed record ChartSeries(string Label, IReadOnlyList<(double X, double Y)> Points, string Colour = "var(--chart-1)");
