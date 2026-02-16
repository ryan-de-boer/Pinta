using Cairo;

namespace Pinta.Core;

public sealed class BrushStrokeArgs
{
	public Color StrokeColor { get; }
	public PointI CurrentPosition { get; }
	public PointI LastPosition { get; }

	public bool WriteToMask {get;set;}
	public Document g_document = null;

	public BrushStrokeArgs (
		Color strokeColor,
		PointI currentPosition,
		PointI lastPosition
	)
	{
		StrokeColor = strokeColor;
		CurrentPosition = currentPosition;
		LastPosition = lastPosition;
	}
}
