//
// UserLayer.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2013 Andrew Davis, GSoC 2012 and GSoC 2013
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cairo;
using Pango;

namespace Pinta.Core;

/// <summary>
/// A UserLayer is a Layer that the user interacts with directly. Each UserLayer contains special layers
/// and some other special variables that allow for re-editability of various things.
/// </summary>
public sealed class UserLayer : Layer
{
	//Special layers to be drawn on to keep things editable by drawing them separately from the UserLayers.
	internal Collection<ReEditableLayer> ReEditableLayers { get; } = [];
	public ReEditableLayer TextLayer { get; }

	//Call the base class constructor and setup the engines.
	public UserLayer (ImageSurface surface)
		: this (surface, false, 1f, "")
	{ }

	//Call the base class constructor and setup the engines.
	public UserLayer (
		ImageSurface surface,
		bool hidden,
		double opacity,
		string name
	)
		: base (surface, hidden, opacity, name)
	{
		TextEngine = new TextEngine ();
		TextLayer = new ReEditableLayer (this);
	}

public override void CreateMaskV()
{
    if (mask_surface != null)
        return;

    var width = Surface.Width;
var height = Surface.Height;
    mask_surface = new ImageSurface(Format.A8, width, height);

    using (var g = new Cairo.Context(mask_surface))
    {
	SolidPattern white = SolidPattern.CreateRgba(1.0,1.0,1.0,1.0);
    g.SetSource(white);
        g.Paint();
    }

    //test
    // Assume you have a UserLayer with a mask
UserLayer userLayer = this; // your layer
if (!HasMaskS || MaskSurface is null)
    return;

// Get the mask surface
ImageSurface maskSurface = MaskSurface;

// Lock the surface for drawing
using (var g = new Cairo.Context(maskSurface))
{
    //// Get mask width & height
    //int w = mask.Width;
    //int h = mask.Height;

    //// Clear mask first
    //ctx.SetSourceRgb(0, 0, 0); // black
    //ctx.Paint();

    //// Draw half white rectangle
    //ctx.Rectangle(0, 0, w / 2, h); // left half
    //ctx.SetSourceRgb(1, 1, 1);      // white
    //ctx.Fill();

    // Left half = fully visible
    g.Operator = Operator.Source;
    g.SetSourceRgba(1, 1, 1, 1);
    g.Rectangle(0, 0, maskSurface.Width / 2, maskSurface.Height);
    g.Fill();

    // Right half = fully transparent
    g.Operator = Operator.Source;
//    g.SetSourceRgba(1, 1, 1, 0);
    g.SetSourceRgba(1, 1, 1, 1);
    g.Rectangle(maskSurface.Width / 2, 0, maskSurface.Width / 2, maskSurface.Height);
    g.Fill();

    maskSurface.MarkDirty();
}
    //test


    OnMaskChanged();

	
}



	public void EraseMask(RectangleI area)
{
    if (!HasMaskS || MaskSurface is null)
        return;

    using var g = new Cairo.Context(MaskSurface);

    // Set operator to "Source" so we directly write the color
    g.Operator = Operator.Source;

    // Draw black (fully transparent in mask)
    g.SetSourceRgba(0, 0, 0, 0.5);

    g.Rectangle(area.X, area.Y, area.Width, area.Height);
    g.Fill();

    MaskSurface.MarkDirty();

    // Notify listeners the mask changed
    OnMaskChanged();

    // Optional: force canvas to redraw
    FirePropertyChanged(nameof(Surface));
}



	//Stores most of the editable text's data, including the text itself.
	public TextEngine TextEngine { get; internal set; }

	//Rectangular boundary surrounding the editable text.
	public RectangleI TextBounds { get; set; } = RectangleI.Zero;
	public RectangleI PreviousTextBounds { get; set; } = RectangleI.Zero;

	public override void ApplyTransform (
		Cairo.Matrix xform,
		Size old_size,
		Size new_size)
	{
		base.ApplyTransform (xform, old_size, new_size);

		foreach (ReEditableLayer rel in ReEditableLayers) {
			if (rel.IsLayerSetup)
				rel.Layer.ApplyTransform (xform, old_size, new_size);
		}
	}

	public void Rotate (
		DegreesAngle angle,
		Size old_size,
		Size new_size)
	{
		RadiansAngle radians = angle.ToRadians ();

		Cairo.Matrix xform = CairoExtensions.CreateIdentityMatrix ();
		xform.Translate (new_size.Width / 2.0, new_size.Height / 2.0);
		xform.Rotate (radians.Radians);
		xform.Translate (-old_size.Width / 2.0, -old_size.Height / 2.0);

		ApplyTransform (xform, old_size, new_size);
	}

	public override void Crop (RectangleI rect, Path? selection)
	{
		base.Crop (rect, selection);

		foreach (ReEditableLayer rel in ReEditableLayers)
			if (rel.IsLayerSetup)
				rel.Layer.Crop (rect, selection);
	}

	public override void ResizeCanvas (Size newSize, Anchor anchor)
	{
		base.ResizeCanvas (newSize, anchor);

		foreach (ReEditableLayer rel in ReEditableLayers)
			if (rel.IsLayerSetup)
				rel.Layer.ResizeCanvas (newSize, anchor);
	}

	public override void Resize (Size newSize, ResamplingMode resamplingMode)
	{
		base.Resize (newSize, resamplingMode);

		foreach (ReEditableLayer rel in ReEditableLayers)
			if (rel.IsLayerSetup)
				rel.Layer.Resize (newSize, resamplingMode);
	}

	/// <summary>
	/// Returns a list of the layers to paint for this top-level layer.
	/// This includes the primary layer and any active re-editable layers.
	/// </summary>
	public IEnumerable<Layer> GetLayersToPaint ()
	{
		yield return this;

		foreach (ReEditableLayer rel in ReEditableLayers) {
			if (rel.IsLayerSetup)
				yield return rel.Layer;
		}
	}
}
