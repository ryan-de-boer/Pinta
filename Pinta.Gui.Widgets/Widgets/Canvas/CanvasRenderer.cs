/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Transactions;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

public sealed class CanvasRenderer
{
	private static readonly Cairo.Pattern tranparent_pattern;

	private readonly ILivePreview live_preview;
	private readonly IWorkspaceService workspace;
	private readonly bool enable_live_preview;
	private readonly bool enable_background_pattern;

	private Size source_size;
	private Size destination_size;
	private Fraction<int> scale_factor;
	private double scale_ratio;

	public CanvasRenderer (
		ILivePreview livePreview,
		IWorkspaceService workspace,
		bool enableLivePreview,
		bool enableBackgroundPattern = true)
	{
		live_preview = livePreview;
		this.workspace = workspace;
		enable_live_preview = enableLivePreview;
		enable_background_pattern = enableBackgroundPattern;
	}

	static CanvasRenderer ()
	{
		tranparent_pattern = CairoExtensions.CreateTransparentBackgroundPattern (16);
	}

	public void Initialize (Size sourceSize, Size destinationSize)
	{
		if (sourceSize == source_size && destinationSize == destination_size)
			return;

		source_size = sourceSize;
		destination_size = destinationSize;

		Fraction<int> scaleFactor = ScaleFactor.CreateClamped (source_size.Width, destination_size.Width);
		scale_factor = scaleFactor;
		scale_ratio = scale_factor.ComputeRatio ();
	}

	public void RenderOLD (
		IReadOnlyList<Layer> layers,
		Cairo.ImageSurface dst,
		PointI offset,
		RectangleI? clipRect = null)
	{
		dst.Flush ();

		// Our rectangle of interest
		RectangleD r = new RectangleI (offset, dst.GetBounds ().Size).ToDouble ();
		bool is_one_to_one = scale_ratio == 1;

		using Cairo.Context g = new (dst);

		g.Translate (-offset.X, -offset.Y);

		if (clipRect is not null) {
			g.Rectangle (clipRect.Value.ToDouble ());
			g.Clip ();
		}

		if (enable_background_pattern) // Checkerboard background
			g.FillRectangle (r, tranparent_pattern, new PointD (offset.X, offset.Y));
		else // Clear before painting translucent layers
			g.Clear (r);

		for (int i = 0; i < layers.Count; i++) {

			Layer layer = layers[i];

			Cairo.ImageSurface surface =
				(enable_live_preview && layer == workspace.ActiveDocument.Layers.CurrentUserLayer && live_preview.IsEnabled)
				? live_preview.LivePreviewSurface // If we're in LivePreview, choose preview layer
				: layer.Surface;

			g.Save ();

			if (!is_one_to_one) {
				// Scale the source surface based on the zoom level.
				double inv_scale = 1.0 / scale_ratio;
				g.Scale (inv_scale, inv_scale);
			}

			g.Transform (layer.Transform);

			// Use nearest-neighbor interpolation when zoomed in so that there isn't any smoothing.
			ResamplingMode filter = (scale_ratio <= 1) ? ResamplingMode.NearestNeighbor : ResamplingMode.Bilinear;

			g.SetSourceSurface (surface, filter);

			g.SetBlendMode (layer.BlendMode);
			g.PaintWithAlpha (layer.Opacity);
			g.Restore ();
		}

		dst.MarkDirty ();
	}



public void Render(
    IReadOnlyList<Layer> layers,
    Cairo.ImageSurface dst,
    PointI offset,
    RectangleI? clipRect = null,
    bool renderMask=false)
{
		Cairo.ImageSurface? debug2 = null;

    dst.Flush();

    // Rectangle of interest
    RectangleD r = new RectangleI(offset, dst.GetBounds().Size).ToDouble();
    bool is_one_to_one = scale_ratio == 1;

    using var g = new Cairo.Context(dst);
    g.Translate(-offset.X, -offset.Y);

    // Apply clip if needed
    if (clipRect is not null)
    {
        g.Rectangle(clipRect.Value.ToDouble());
        g.Clip();
    }

    // Background
    if (enable_background_pattern)
        g.FillRectangle(r, tranparent_pattern, new PointD(offset.X, offset.Y));
    else
        g.Clear(r);

    foreach (var layer in layers)
    {
        Cairo.ImageSurface sourceSurface =
            (enable_live_preview &&
             layer == workspace.ActiveDocument.Layers.CurrentUserLayer &&
             live_preview.IsEnabled)
            ? live_preview.LivePreviewSurface
            : layer.Surface;

        //// If the layer has a mask, multiply it first
        //if (layer is UserLayer userLayer && userLayer.HasMask)
        //{
        //    using var maskedSurface = CairoExtensions.CreateImageSurface(
        //        Cairo.Format.Argb32,
        //        sourceSurface.Width,
        //        sourceSurface.Height);

        //    using var gTemp = new Cairo.Context(maskedSurface);

        //    // Apply layer surface
        //    gTemp.SetSourceSurface(sourceSurface, 0, 0);

        //    // Multiply by mask alpha
        //    gTemp.MaskSurface(userLayer.MaskSurface, 0, 0);

        //    // Commit to maskedSurface
        //    gTemp.Paint();

        //    sourceSurface = maskedSurface;
        //}


			bool skip = false;
	Cairo.ImageSurface finalSurface = sourceSurface;
			UserLayer uul = layer as UserLayer;
			if (renderMask && layer is UserLayer ul && uul.HasMask)
{



var mask = ul.MaskSurface;
				int widthRequest =60;
				int heightRequest = 40;

// Create a debug surface to draw the mask preview
//debug2 = new Cairo.ImageSurface(Cairo.Format.Argb32, widthRequest, heightRequest);
debug2 = new Cairo.ImageSurface(Cairo.Format.Argb32, mask.Width, mask.Height);

using (var ctx = new Cairo.Context(debug2))
{
    // Calculate scaling factors
//    double scaleX = (double)widthRequest / mask.Width;
//    double scaleY = (double)heightRequest / mask.Height;
    // Scale based on the layer surface, not mask size

//    double scaleX = (double)widthRequest / ul.Surface.Width;
//    double scaleY = (double)heightRequest / ul.Surface.Height;
//    ctx.Scale(scaleX, scaleY);


    // Fill black
    ctx.SetSourceRgb(0, 0, 0);
    ctx.Paint();

    // Set operator for mask drawing
    ctx.Operator = Cairo.Operator.Over;

    // Paint white where the mask alpha > 0
    ctx.SetSourceRgb(1, 1, 1);
    ctx.MaskSurface(mask, 0, 0); // mask alpha is used
}

// Draw scaled debug surface onto main context
g.SetSourceSurface(debug2, 0, 0);
g.Paint();
finalSurface = debug2;
skip = true;
}

Cairo.ImageSurface? maskedSurface = null;

if (!skip && layer is UserLayer userLayer && userLayer.HasMask/* && userLayer==workspace.ActiveDocument.Layers.CurrentUserLayer*/)
{
    maskedSurface = CairoExtensions.CreateImageSurface(
        Cairo.Format.Argb32,
        sourceSurface.Width,
        sourceSurface.Height);


				/* Good show alpha

				var debug = new Cairo.ImageSurface(Cairo.Format.Argb32,
    userLayer.MaskSurface.Width,
    userLayer.MaskSurface.Height);

using (var ctx = new Cairo.Context(debug))
{
    // Fill white
    ctx.SetSourceRgb(1, 1, 1);
    ctx.Paint();

    // Multiply by mask
    ctx.Operator = Cairo.Operator.DestIn;
    ctx.SetSourceSurface(userLayer.MaskSurface, 0, 0);
    ctx.Paint();
}

g.SetSourceSurface(debug, 0, 0);
g.Paint();
return;
*/

bool showAlpha = userLayer.ShowMask;
bool showMaskPreview = renderMask;

if (!skip && showMaskPreview)
{
    using var debug = new Cairo.ImageSurface(Cairo.Format.Argb32,
        userLayer.MaskSurface.Width,
        userLayer.MaskSurface.Height);

    using (var ctx = new Cairo.Context(debug))
    {
        ctx.SetSourceRgb(0, 0, 0);
        ctx.Paint();

        ctx.SetSourceRgb(1, 1, 1);
        ctx.MaskSurface(userLayer.MaskSurface, 0, 0);
    }

    finalSurface = debug;
}
else if (!skip && showAlpha) {

var mask = userLayer.MaskSurface;

// Create a debug surface to draw the mask preview
using var debug = new Cairo.ImageSurface(Cairo.Format.Argb32, mask.Width, mask.Height);

using (var ctx = new Cairo.Context(debug))
{
    // Fill the entire surface black
    ctx.SetSourceRgb(0, 0, 0);
    ctx.Paint();

    // Paint white where the mask alpha > 0
    ctx.Operator = Cairo.Operator.Over;        // normal drawing
    ctx.SetSourceRgb(1, 1, 1);                 // white for opaque
    ctx.MaskSurface(mask, 0, 0);               // use mask alpha to apply white
}

// Draw the debug surface onto the main context
g.SetSourceSurface(debug, 0, 0);
g.Paint();
					finalSurface = debug;
//					return;
if (userLayer!=workspace.ActiveDocument.Layers.CurrentUserLayer)
continue;
else
						return;

				}

				

if (!skip && !showAlpha && !showMaskPreview)
    using (var gTemp = new Cairo.Context(maskedSurface))
    {
        // Step 1: Copy the source into maskedSurface
        gTemp.SetSourceSurface(sourceSurface, 0, 0);
        gTemp.Paint();

        // Step 2: Multiply alpha by mask using DestIn
        gTemp.Operator = Cairo.Operator.DestIn;
        gTemp.SetSourceSurface(userLayer.MaskSurface, 0, 0);
        gTemp.Paint();
    }

if (!skip && !showAlpha && !showMaskPreview)
    finalSurface = maskedSurface;
}

        g.Save();

        if (!is_one_to_one)
        {
            double invScale = 1.0 / scale_ratio;
            g.Scale(invScale, invScale);
        }

        g.Transform(layer.Transform);

        // Use nearest-neighbor when zoomed out, bilinear otherwise
        ResamplingMode filter = (scale_ratio <= 1) ? ResamplingMode.NearestNeighbor : ResamplingMode.Bilinear;

        g.SetSourceSurface(finalSurface, filter);
        g.SetBlendMode(layer.BlendMode);
        g.PaintWithAlpha(layer.Opacity);

        g.Restore();

			// Dispose after painting
maskedSurface?.Dispose();
			if (debug2!=null)
				debug2.Dispose();
    }

    dst.MarkDirty();
}



}
