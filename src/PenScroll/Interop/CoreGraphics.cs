// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Matsuda Tetsuya (otd-pen-scroll, the Linux original)
// Copyright (C) 2026 Nathan Suttie (macOS port)

using System;
using System.Runtime.InteropServices;

namespace PenScroll.Interop
{
    /// <summary>
    /// Posts scroll-wheel events through CoreGraphics, the same call OpenTabletDriver's own macOS
    /// pointer uses. Pixel units give smooth, trackpad-style scrolling.
    /// </summary>
    internal static class CoreGraphics
    {
        private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private const uint kCGScrollEventUnitPixel = 0;
        private const uint kCGHIDEventTap = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double X;
            public double Y;
        }

        [DllImport(CoreGraphicsLib)]
        private static extern IntPtr CGEventCreate(IntPtr source);

        [DllImport(CoreGraphicsLib)]
        private static extern CGPoint CGEventGetLocation(IntPtr @event);

        [DllImport(CoreGraphicsLib)]
        private static extern void CGEventSetLocation(IntPtr @event, CGPoint location);

        [DllImport(CoreGraphicsLib)]
        private static extern IntPtr CGEventCreateScrollWheelEvent2(
            IntPtr source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

        [DllImport(CoreGraphicsLib)]
        private static extern void CGEventPost(uint tap, IntPtr @event);

        [DllImport(CoreFoundationLib)]
        private static extern void CFRelease(IntPtr cf);

        /// <summary>
        /// Scrolls by the given pixel deltas at the current cursor position.
        /// Positive <paramref name="vertical"/> scrolls up (content moves down);
        /// positive <paramref name="horizontal"/> scrolls left (content moves right).
        /// </summary>
        public static void PostScroll(int vertical, int horizontal)
        {
            var scroll = CGEventCreateScrollWheelEvent2(IntPtr.Zero, kCGScrollEventUnitPixel, 2, vertical, horizontal, 0);
            if (scroll == IntPtr.Zero)
                throw new InvalidOperationException("CoreGraphics refused to create a scroll event.");

            try
            {
                // Deliver the scroll to whatever is under the cursor right now.
                var probe = CGEventCreate(IntPtr.Zero);
                if (probe != IntPtr.Zero)
                {
                    try { CGEventSetLocation(scroll, CGEventGetLocation(probe)); }
                    finally { CFRelease(probe); }
                }

                CGEventPost(kCGHIDEventTap, scroll);
            }
            finally
            {
                CFRelease(scroll);
            }
        }
    }
}
