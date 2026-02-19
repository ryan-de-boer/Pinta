//
// OraFormat.cs
//
// Author:
//       Maia Kozheva <sikon@ubuntu.com>
//
// Copyright (c) 2010 Maia Kozheva <sikon@ubuntu.com>
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml;
using Cairo;
using GdkPixbuf;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Pinta.Core;

public sealed class OraFormat : IImageImporter, IImageExporter
{
	private const int THUMB_MAX_SIZE = 256;

	public Document Import (Gio.File file)
	{
		using GioStream stream = new (file.Read (cancellable: null));
		using ZipArchive zipfile = new (stream);

		XmlDocument stackXml = new ();

		ZipArchiveEntry stackXmlEntry = zipfile.GetEntry ("stack.xml") ?? throw new XmlException ("No 'stack.xml' found in OpenRaster file");
		stackXml.Load (stackXmlEntry.Open ());

		// NRT - This makes a lot of assumptions that the file will be perfectly
		// valid that we need to guard against.
		XmlElement imageElement = stackXml.DocumentElement!;

		Size imageSize = new (
			Width: int.Parse (imageElement.GetAttribute ("w")),
			Height: int.Parse (imageElement.GetAttribute ("h"))
		);

		Document newDocument = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			imageSize,
			file,
			"ora");

		XmlElement stackElement = (XmlElement) stackXml.GetElementsByTagName ("stack")[0]!;
		XmlNodeList layerElements = stackElement.GetElementsByTagName ("layer");

		if (layerElements.Count == 0)
			throw new XmlException ("No layers found in OpenRaster file");

		UserLayer previousLayer = null;
		for (int i = 0; i < layerElements.Count; i++) {

			XmlElement layerElement = (XmlElement) layerElements[i]!;

			PointI position = new (
				X: int.Parse (GetAttribute (layerElement, "x", "0")),
				Y: int.Parse (GetAttribute (layerElement, "y", "0"))
			);

			string name = GetAttribute (layerElement, "name", $"Layer {i}");

			try {
				// Write the file to a temporary file first
				// Fixes a bug when running on .Net
				ZipArchiveEntry? zf = zipfile.GetEntry (layerElement.GetAttribute ("src")) ?? throw new XmlException ("Missing layer in OpenRaster file");
				using Stream s = zf.Open ();
				string tmp_file = System.IO.Path.GetTempFileName ();

				using (Stream stream_out = File.Open (tmp_file, FileMode.OpenOrCreate)) {
					byte[] buffer = new byte[2048];
					while (true) {
						int len = s.Read (buffer, 0, buffer.Length);

						if (len > 0)
							stream_out.Write (buffer, 0, len);
						else
							break;
					}
				}

				UserLayer layer = null;
				string visibility = GetAttribute (layerElement, "visibility", "visible");
				bool useMask = false;
				if (name.EndsWith("_mask") && visibility == "hidden" && previousLayer!=null) {
					// Add mask to previous layer?
					layer = previousLayer;
					useMask = true;
				}
				else {
					layer = newDocument.Layers.CreateLayer (name);
					newDocument.Layers.Insert (layer, 0);

				if (visibility == "hidden") {
					layer.Hidden = true;
				}
				layer.Opacity = double.Parse (GetAttribute (layerElement, "opacity", "1"), GetFormat ());
				layer.BlendMode = StandardToBlendMode (GetAttribute (layerElement, "composite-op", "svg:src-over"));

				}



				using Pixbuf pb = Pixbuf.NewFromFile (tmp_file)!; // NRT: only nullable when an error is thrown
				Context? g = null;
				if (useMask) {
					layer.HasMask = true;
//					g = new (layer.MaskSurface);
//				g.DrawPixbuf (pb, (PointD) position);
DrawPixbufToMask(layer.MaskSurface, pb, position);

					    layer.MaskSurface.Flush();
    layer.MaskSurface.MarkDirty();

layer.MaskSurface.Flush();

										layer.OnChanged();


Debug.WriteLine(layer.MaskSurface.GetType().FullName);
Debug.WriteLine(layer.MaskSurface.Format);

					/*
int width = layer.MaskSurface.Width;
int height = layer.MaskSurface.Height;
int stride = layer.MaskSurface.Stride;

Span<byte> src = layer.MaskSurface.GetData();

using var image = new Image<Rgba32>(width, height);

for (int y = 0; y < height; y++)
{
						//image.row
    var row = image.DangerousGetPixelRowMemory(y).Span;
    int rowOffset = y * stride;

    for (int x = 0; x < width; x++)
    {
        byte alpha = src[rowOffset + x];

        row[x] = new Rgba32(255, 255, 255, alpha);
    }
}

image.Save("debug_mask.png");
*/
				}
				else {
					g = new (layer.Surface);
				g.DrawPixbuf (pb, (PointD) position);
				}

				previousLayer = layer;
				if (g!=null) {
				g.Dispose();
				}
				try {
					File.Delete (tmp_file);
				} catch { }
			} catch {
				// Translators: {0} is the name of a layer, and {1} is the path to a .ora file.
				string details = Translations.GetString ("Could not import layer \"{0}\" from {1}", name, zipfile);
				PintaCore.Chrome.ShowMessageDialog (PintaCore.Chrome.MainWindow, Translations.GetString ("Error"), details);
			}
		}

		return newDocument;
	}

    /// <summary>
    /// Draw a Pixbuf onto an A8 mask surface at the given position.
    /// Copies alpha channel only; ignores color.
    /// Safe version using Span<byte>.
    /// </summary>
private unsafe void DrawPixbufToMask(Cairo.ImageSurface maskSurface, Pixbuf pb, PointI position)
{
    if (maskSurface.Format != Cairo.Format.A8)
        throw new InvalidOperationException("Mask surface must be A8 format.");

    int maskWidth = maskSurface.Width;
    int maskHeight = maskSurface.Height;

    int pbWidth = pb.Width;
    int pbHeight = pb.Height;
    int pbChannels = pb.NChannels;
    int pbRowstride = pb.Rowstride;

    Span<byte> maskData = maskSurface.GetData();

    for (int y = 0; y < pbHeight; y++)
    {
        int maskY = y + (int)position.Y;
        if (maskY < 0 || maskY >= maskHeight)
            continue;

        for (int x = 0; x < pbWidth; x++)
        {
            int maskX = x + (int)position.X;
            if (maskX < 0 || maskX >= maskWidth)
                continue;

            // Read pixel from Pixbuf
            byte alpha;
            IntPtr pixelPtr = pb.Pixels + y * pbRowstride + x * pbChannels;

            if (pb.HasAlpha)
            {
                alpha = Marshal.ReadByte(pixelPtr + 3); // alpha channel
            }
            else
            {
                // Convert RGB to luminance for alpha
                byte r = Marshal.ReadByte(pixelPtr + 0);
                byte g = Marshal.ReadByte(pixelPtr + 1);
                byte b = Marshal.ReadByte(pixelPtr + 2);
                alpha = (byte)((r + g + b) / 3); // simple brightness -> alpha
            }

            // Write alpha into mask surface (A8)
            maskData[maskY * maskSurface.Stride + maskX] = alpha;
        }
    }

    maskSurface.Flush();
    maskSurface.MarkDirty();
}


	private static CultureInfo GetFormat ()
		=> CultureInfo.CreateSpecificCulture ("en");

	private static string GetAttribute (XmlElement element, string attribute, string defValue)
	{
		string ret = element.GetAttribute (attribute);
		return string.IsNullOrEmpty (ret) ? defValue : ret;
	}

	private static Size GetThumbDimensions (int width, int height)
	{
		if (width <= THUMB_MAX_SIZE && height <= THUMB_MAX_SIZE)
			return new (width, height);
		else if (width > height)
			return new (THUMB_MAX_SIZE, (int) ((double) height / width * THUMB_MAX_SIZE));
		else
			return new ((int) ((double) width / height * THUMB_MAX_SIZE), THUMB_MAX_SIZE);
	}

	private static byte[] GetLayerXmlData (IReadOnlyList<UserLayer> layers)
	{
		using MemoryStream ms = new ();
		using XmlTextWriter writer = new (ms, System.Text.Encoding.UTF8) {
			Formatting = Formatting.Indented,
		};

		writer.WriteStartElement ("image");
		writer.WriteAttributeString ("w", layers[0].Surface.Width.ToString ());
		writer.WriteAttributeString ("h", layers[0].Surface.Height.ToString ());
		writer.WriteAttributeString ("version", "0.0.5"); // Current version of the spec.

		writer.WriteStartElement ("stack");

		// ORA stores layers top to bottom
		for (int i = layers.Count - 1; i >= 0; i--) {
			var layer = layers[i];
			writer.WriteStartElement ("layer");
			writer.WriteAttributeString ("opacity", string.Format (GetFormat (), "{0:0.00}", layer.Opacity));
			writer.WriteAttributeString ("name", layer.Name);
			writer.WriteAttributeString ("composite-op", BlendModeToStandard (layer.BlendMode));
			writer.WriteAttributeString ("src", "data/layer" + i.ToString () + ".png");

			if (layer.Hidden)
				writer.WriteAttributeString ("visibility", "hidden");

			writer.WriteEndElement ();

			if (layer.HasMask) {
			writer.WriteStartElement ("layer");
			writer.WriteAttributeString ("opacity", string.Format (GetFormat (), "{0:0.00}", 1.0));
			writer.WriteAttributeString ("name", layer.Name+"_mask");
			writer.WriteAttributeString ("composite-op", BlendModeToStandard (BlendMode.Normal));
			writer.WriteAttributeString ("src", "data/layer" + i.ToString () + "_mask.png");
			writer.WriteAttributeString ("visibility", "hidden");
			writer.WriteEndElement ();

			}
		}

		writer.WriteEndElement (); // stack
		writer.WriteEndElement (); // image

		writer.Close ();

		return ms.ToArray ();
	}

	public void Export (Document document, Gio.File file, Gtk.Window parent)
	{
		using GioStream file_stream = new (file.Replace ());
		using ZipArchive archive = new (file_stream, ZipArchiveMode.Create);
		using Pixbuf flattened = document.GetFlattenedImage ().ToPixbuf ();
		AddMimeEntry (archive);
		AddLayerEntries (archive, document);
		AddStackEntry (archive, document);
		AddMergedImage (archive, flattened);
		AddThumbnail (archive, flattened);
	}

	private static void AddMimeEntry (ZipArchive archive)
	{
		byte[] mimeBytes = System.Text.Encoding.ASCII.GetBytes ("image/openraster");
		ZipArchiveEntry mimeEntry = archive.CreateEntry ("mimetype", CompressionLevel.NoCompression);
		using Stream mimeStream = mimeEntry.Open ();
		mimeStream.Write (mimeBytes, 0, mimeBytes.Length);
	}

	private static void AddLayerEntries (ZipArchive archive, Document document)
	{
		for (int i = 0; i < document.Layers.UserLayers.Count; i++) {
			{
			using Pixbuf pb = document.Layers.UserLayers[i].Surface.ToPixbuf ();
			byte[] buf = pb.SaveToBuffer ("png");
			ZipArchiveEntry layerEntry = archive.CreateEntry ($"data/layer{i}.png");
			using Stream layerStream = layerEntry.Open ();
			layerStream.Write (buf, 0, buf.Length);
			}

			//if (document.Layers.UserLayers[i].HasMask) {
			//using Pixbuf pb = document.Layers.UserLayers[i].MaskSurface.ToPixbuf ();
			//byte[] buf = pb.SaveToBuffer ("png");
			//ZipArchiveEntry layerEntry = archive.CreateEntry ($"data/layer{i}_mask.png");
			//using Stream layerStream = layerEntry.Open ();
			//layerStream.Write (buf, 0, buf.Length);
			//}

if (document.Layers.UserLayers[i].HasMask)
{
    var maskSurface = document.Layers.UserLayers[i].MaskSurface;

    int width = maskSurface.Width;
    int height = maskSurface.Height;
    int stride = maskSurface.Stride;

    Span<byte> src = maskSurface.GetData();

    using Pixbuf pb = Pixbuf.New(
        Colorspace.Rgb,
        false,  // no alpha
        8,
        width,
        height
    );

    int pbStride = pb.Rowstride;
    IntPtr destPtr = pb.Pixels;

    for (int y = 0; y < height; y++)
    {
        int srcRow = y * stride;
        int destRow = y * pbStride;

        for (int x = 0; x < width; x++)
        {
            byte alpha = src[srcRow + x];

            int pixelOffset = destRow + x * 3;

            Marshal.WriteByte(destPtr, pixelOffset + 0, alpha); // R
            Marshal.WriteByte(destPtr, pixelOffset + 1, alpha); // G
            Marshal.WriteByte(destPtr, pixelOffset + 2, alpha); // B
        }
    }

    byte[] buf = pb.SaveToBuffer("png");

    ZipArchiveEntry layerEntry = archive.CreateEntry($"data/layer{i}_mask.png");
    using Stream layerStream = layerEntry.Open();
    layerStream.Write(buf, 0, buf.Length);
}




		}
	}

	private static void AddStackEntry (ZipArchive archive, Document document)
	{
		byte[] userLayerBytes = GetLayerXmlData (document.Layers.UserLayers);
		ZipArchiveEntry stackEntry = archive.CreateEntry ("stack.xml");
		using Stream stackStream = stackEntry.Open ();
		stackStream.Write (userLayerBytes, 0, userLayerBytes.Length);
	}

	private static void AddMergedImage (ZipArchive archive, Pixbuf flattened)
	{
		byte[] mergedImageBytes = flattened.SaveToBuffer ("png");
		ZipArchiveEntry mergedImageEntry = archive.CreateEntry ("mergedimage.png");
		using Stream mergedImageStream = mergedImageEntry.Open ();
		mergedImageStream.Write (mergedImageBytes, 0, mergedImageBytes.Length);
	}

	private static void AddThumbnail (ZipArchive archive, Pixbuf flattened)
	{
		Size newSize = GetThumbDimensions (flattened.Width, flattened.Height);
		using Pixbuf thumb = flattened.ScaleSimple (newSize.Width, newSize.Height, InterpType.Bilinear)!; // Creates new Pixbuf
		byte[] thumbnailBytes = thumb.SaveToBuffer ("png");
		ZipArchiveEntry thumbnailEntry = archive.CreateEntry ("Thumbnails/thumbnail.png");
		using Stream thumbnailStream = thumbnailEntry.Open ();
		thumbnailStream.Write (thumbnailBytes, 0, thumbnailBytes.Length);
	}

	private static string BlendModeToStandard (BlendMode mode)
		=> mode switch {
			BlendMode.Multiply => "svg:multiply",
			BlendMode.ColorBurn => "svg:color-burn",
			BlendMode.ColorDodge => "svg:color-dodge",
			BlendMode.Overlay => "svg:overlay",
			BlendMode.Difference => "svg:difference",
			BlendMode.Lighten => "svg:lighten",
			BlendMode.Darken => "svg:darken",
			BlendMode.Screen => "svg:screen",
			BlendMode.Xor => "svg:xor",
			BlendMode.HardLight => "svg:hard-light",
			BlendMode.SoftLight => "svg:soft-light",
			BlendMode.Color => "svg:color",
			BlendMode.Luminosity => "svg:luminosity",
			BlendMode.Hue => "svg:hue",
			BlendMode.Saturation => "svg:saturation",
			_ => "svg:src-over",
		};

	private static BlendMode StandardToBlendMode (string mode)
	{
		switch (mode) {
			case "svg:src-over":
				return BlendMode.Normal;
			case "svg:multiply":
				return BlendMode.Multiply;
			case "svg:color-burn":
				return BlendMode.ColorBurn;
			case "svg:color-dodge":
				return BlendMode.ColorDodge;
			case "svg:overlay":
				return BlendMode.Overlay;
			case "svg:difference":
				return BlendMode.Difference;
			case "svg:lighten":
				return BlendMode.Lighten;
			case "svg:darken":
				return BlendMode.Darken;
			case "svg:screen":
				return BlendMode.Screen;
			case "svg:xor":
				return BlendMode.Xor;
			case "svg:hard-light":
				return BlendMode.HardLight;
			case "svg:soft-light":
				return BlendMode.SoftLight;
			case "svg:color":
				return BlendMode.Color;
			case "svg:luminosity":
				return BlendMode.Luminosity;
			case "svg:hue":
				return BlendMode.Hue;
			case "svg:saturation":
				return BlendMode.Saturation;
			default:
				Console.WriteLine ("Unrecognized composite-op: {0}, using Normal.", mode);
				return BlendMode.Normal;
		}
	}
}
