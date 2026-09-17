// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Matsuda Tetsuya (otd-pen-scroll, the Linux original)
// Copyright (C) 2026 Nathan Suttie (macOS port)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using PenScroll.Interop;

namespace PenScroll
{
    /// <summary>
    /// Scrolls while a pen button is held, like two fingers on a trackpad: the tip never clicks
    /// while the button is down, and the cursor keeps following the pen unless pinned.
    /// <para/>
    /// Drag mode: the content follows the pen one-to-one, like scrolling on a touch screen.
    /// Joystick mode: how far the pen is held from where you pressed sets the scroll speed, and
    /// holding still keeps scrolling.
    /// <para/>
    /// A filter rather than a binding: a binding only sees press and release, never the movement
    /// in between. It runs before the driver's binding handler, which reads the same report, so
    /// zeroing the pressure here is what keeps the tip from clicking. macOS port of
    /// otd-pen-scroll (GPL-3.0).
    /// </summary>
    [PluginName("Pen Scroll")]
    [SupportedPlatform(PluginPlatform.MacOS)]
    public class PenScrollFilter : IPositionedPipelineElement<IDeviceReport>
    {
        private const string LogGroup = "Pen Scroll";

        /// <summary>Displacement at which <see cref="Speed"/> is quoted (joystick mode).</summary>
        private const float SpeedReferencePixels = 100f;

        /// <summary>A stalled pipeline must not discharge as one long scroll.</summary>
        private const float MaxStepSeconds = 0.05f;

        /// <summary>Post-transform, so reports arrive in screen pixels rather than tablet units.</summary>
        public PipelinePosition Position => PipelinePosition.PostTransform;

        public event Action<IDeviceReport>? Emit;

        [Property("Modifier Button")]
        [DefaultPropertyValue(1)]
        [ToolTip("Which pen button starts scrolling, counting from 1.\n\n" +
                 "Leave this button unbound in the Pen Settings tab, or whatever it is bound to " +
                 "will fire alongside the scrolling.")]
        public int ModifierButton { get; set; } = 1;

        [Property("Mode")]
        [DefaultPropertyValue("Drag")]
        [PropertyValidated(nameof(ValidModes))]
        [ToolTip("Drag: the content follows the pen one-to-one, like a touch screen.\n" +
                 "Joystick: the further the pen is held from where you pressed, the faster it " +
                 "scrolls, and holding still keeps scrolling.")]
        public string Mode { get; set; } = "Drag";

        public static IEnumerable<string> ValidModes => new[] { "Drag", "Joystick" };

        [Property("Drag Sensitivity")]
        [DefaultPropertyValue(1f)]
        [ToolTip("Drag mode: pixels scrolled per pixel of pen movement. 1 follows the pen exactly.")]
        public float DragSensitivity { get; set; } = 1f;

        [Property("Dead Zone")]
        [Unit("px")]
        [DefaultPropertyValue(12f)]
        [ToolTip("Joystick mode: how far the pen must be held from where you pressed before scrolling starts.")]
        public float DeadZone { get; set; } = 12f;

        [Property("Speed")]
        [Unit("px/s")]
        [DefaultPropertyValue(800f)]
        [ToolTip("Joystick mode: scroll speed when the pen is held 100 px past the dead zone.\n\n" +
                 "Twice that distance scrolls twice as fast.")]
        public float Speed { get; set; } = 800f;

        [BooleanProperty("Invert Direction", "Reverse the scroll direction.")]
        [DefaultPropertyValue(false)]
        public bool Invert { get; set; }

        [BooleanProperty("Horizontal Scrolling", "Also scroll sideways from horizontal pen movement.")]
        [DefaultPropertyValue(false)]
        public bool HorizontalScrolling { get; set; }

        [BooleanProperty("Pin Cursor", "Keep the cursor where you pressed while scrolling instead of following the pen.")]
        [DefaultPropertyValue(false)]
        public bool PinCursor { get; set; }

        private bool _scrolling;
        private bool _muteTipUntilLift;
        private bool _announced;
        private bool _unavailable;
        private Vector2 _anchor;
        private Vector2 _last;
        private long _lastTimestamp;

        // Sub-pixel remainders, so slow movement still scrolls instead of being rounded away.
        private float _pendingVertical;
        private float _pendingHorizontal;

        private bool IsJoystick => string.Equals(Mode, "Joystick", StringComparison.OrdinalIgnoreCase);

        public void Consume(IDeviceReport report)
        {
            try
            {
                if (report is ITabletReport tabletReport)
                    Process(tabletReport);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                StopScrolling();
            }

            // Never swallowed: dropping a report would hide the button release from the binding
            // handler and leave whatever that button is bound to held down.
            Emit?.Invoke(report);
        }

        private void Process(ITabletReport report)
        {
            var held = !_unavailable && IsModifierHeld(report.PenButtons);

            if (!held)
            {
                if (_scrolling)
                {
                    StopScrolling();
                    // Letting go of the button before lifting the pen must not turn into a click
                    // on whatever just scrolled under the tip. Stay quiet until the pen lifts.
                    _muteTipUntilLift = report.Pressure > 0;
                }

                if (_muteTipUntilLift)
                {
                    if (report.Pressure == 0)
                        _muteTipUntilLift = false;
                    else
                        report.Pressure = 0;
                }

                return;
            }

            var now = Stopwatch.GetTimestamp();

            if (!_scrolling)
            {
                _scrolling = true;
                _anchor = report.Position;
                _last = report.Position;
                _pendingVertical = 0f;
                _pendingHorizontal = 0f;

                if (!_announced)
                {
                    _announced = true;
                    Log.Write(LogGroup, $"Scrolling with pen button {ModifierButton} in {(IsJoystick ? "Joystick" : "Drag")} mode.", LogLevel.Info);
                }
            }
            else
            {
                var seconds = (float)(now - _lastTimestamp) / Stopwatch.Frequency;
                if (IsJoystick)
                    ScrollJoystick(report.Position - _anchor, Math.Min(seconds, MaxStepSeconds));
                else
                    ScrollDrag(report.Position - _last);
            }

            _last = report.Position;
            _lastTimestamp = now;

            // Two fingers on a trackpad never click: silence the tip while the button is held.
            report.Pressure = 0;

            if (PinCursor)
                report.Position = _anchor;
        }

        private bool IsModifierHeld(bool[]? penButtons)
        {
            var index = ModifierButton - 1;
            return penButtons is not null
                && index >= 0
                && index < penButtons.Length
                && penButtons[index];
        }

        /// <summary>Content follows the pen: moving the pen down scrolls the page up.</summary>
        private void ScrollDrag(Vector2 delta)
        {
            var sign = Invert ? -1f : 1f;
            var sensitivity = Math.Max(DragSensitivity, 0f);

            var vertical = TakeWhole(ref _pendingVertical, delta.Y * sensitivity * sign);
            var horizontal = HorizontalScrolling
                ? TakeWhole(ref _pendingHorizontal, delta.X * sensitivity * sign)
                : 0;

            Post(vertical, horizontal);
        }

        /// <summary>Holding the pen below where you pressed scrolls down, like pushing a joystick.</summary>
        private void ScrollJoystick(Vector2 displacement, float seconds)
        {
            var vertical = TakeWhole(ref _pendingVertical, -Rate(displacement.Y) * seconds);
            var horizontal = HorizontalScrolling
                ? TakeWhole(ref _pendingHorizontal, -Rate(displacement.X) * seconds)
                : 0;

            Post(vertical, horizontal);
        }

        /// <summary>
        /// Pixels per second for one axis. The dead zone is applied per axis rather than to the
        /// distance, so drifting a little sideways during a vertical scroll does not start
        /// scrolling sideways.
        /// </summary>
        private float Rate(float offset)
        {
            var past = Math.Abs(offset) - Math.Max(DeadZone, 0f);
            if (past <= 0f)
                return 0f;

            var pixelsPerSecond = past / SpeedReferencePixels * Math.Max(Speed, 0f);
            var direction = offset < 0f ? -1f : 1f;
            return direction * pixelsPerSecond * (Invert ? -1f : 1f);
        }

        private void Post(int vertical, int horizontal)
        {
            if (vertical == 0 && horizontal == 0)
                return;

            try
            {
                CoreGraphics.PostScroll(vertical, horizontal);
            }
            catch (Exception ex)
            {
                _unavailable = true;
                Log.Write(LogGroup, $"Scrolling is disabled: {ex.Message}", LogLevel.Error, notify: true);
            }
        }

        /// <summary>
        /// Returns the whole part of <paramref name="pending"/> plus <paramref name="amount"/>,
        /// leaving the fraction for the next report. Truncating toward zero keeps the remainder on
        /// the same side as the movement, so reversing direction owes nothing from the other side.
        /// </summary>
        private static int TakeWhole(ref float pending, float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
                return 0;

            pending += amount;
            var whole = (int)pending;
            pending -= whole;
            return whole;
        }

        private void StopScrolling()
        {
            _scrolling = false;
            _pendingVertical = 0f;
            _pendingHorizontal = 0f;
        }
    }
}
