// RetroEye app API: the rule RetroEye draws its pixel fonts by, for clients that want to match it.
#nullable enable
using System;

namespace RetroEye.Api
{
    /// <summary>
    /// RetroEye's rule for the versions' pixel fonts (MS Sans Serif, unsmoothed Tahoma...), the same as RetroEye's own
    /// windows follow: a text is drawn as pixels (aliased, no smoothing) only where its total scale on the screen (the
    /// screen's DPI / 96 times every layout scale above it) is a whole number; elsewhere, as at 125% × 2 = 2.5, it is
    /// drawn in the same face smoothed, so strokes don't come out some 2 and some 3 pixels wide. A test keeps this the
    /// same as RetroEye's own rule.
    /// </summary>
    public static class PixelScale
    {
        /// <summary>Whether a total scale is a whole number of screen pixels (1, 2, 3...), within a hundredth.</summary>
        public static bool IsWhole(double total) => total >= 0.99 && Math.Abs(total - Math.Round(total)) < 0.01;
    }
}
