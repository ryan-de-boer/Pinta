using System;
using Cairo;
using Pinta.Core;

namespace Pinta.Core.Layers
{
    public static class UserLayerExtensions
    {
        /// <summary>
        /// Renders the UserLayer onto a destination context, applying the mask if it exists.
        /// </summary>
        public static void RenderWithMask(this UserLayer layer, Context destination, int destX = 0, int destY = 0)
        {
            if (layer is null)
                throw new ArgumentNullException(nameof(layer));

            var surface = layer.Surface;
            var mask = layer.MaskSurface; // this is your A8 mask surface

            if (mask != null)
            {
                // Apply layer * mask
                destination.SetSourceSurface(surface, destX, destY);
                destination.MaskSurface(mask, destX, destY);
            }
            else
            {
                // No mask, just paint normally
                destination.SetSourceSurface(surface, destX, destY);
                destination.Paint();
            }
        }
    }
}
