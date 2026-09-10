namespace Whisperkey;

/// Draws the pill into a premultiplied BGRA buffer.
///
/// Everything is drawn from signed distance functions rather than with GDI+ or
/// Direct2D. That means antialiasing and a soft shadow come out of the maths for
/// free, the mic glyph is geometry rather than a font lookup, and the app needs no
/// drawing dependency at all - which matters when the whole exe is 5 MB and starts
/// in 16 ms.
static class Pill {
    // A circle, not a lozenge: the whole UI is one small microphone and nothing else.
    public const int W = 48, H = 48;           // logical size at 96 DPI
    const float Pad = 10f;                     // room for the shadow

    /// <param name="level">0..1 microphone level.</param>
    /// <param name="busy">true while transcribing rather than listening.</param>
    public static void Render(Span<uint> px, int w, int h, float scale, uint accent, float level, bool busy, bool highContrast, uint fg, uint bg, bool shadowOn = true, float opacity = 1f) {
        px.Clear();

        float pad = Pad * scale;
        float rx = pad, ry = pad, rw = w - pad * 2, rh = h - pad * 2;
        float radius = rh / 2f;

        (float r, float g, float b) acc = Unpack(highContrast ? bg : accent);
        (float r, float g, float b) ink = Unpack(highContrast ? fg : 0xFFFFFFFF);

        float cx = rx + rw / 2f;                   // mic glyph centre
        float cy = ry + rh / 2f;
        float ringR = radius - 3.0f * scale;

        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                float fx = x + 0.5f, fy = y + 0.5f;

                // Soft shadow: the pill's own distance field, offset downward.
                float ds = RoundRect(fx, fy - 2f * scale, rx, ry, rw, rh, radius);
                // The "Transparency effects" accessibility setting turns the soft
                // shadow off; it is exactly the sort of effect that setting means.
                float shadow = shadowOn ? Smooth(6f * scale, 0f, ds) * 0.30f : 0f;

                float d = RoundRect(fx, fy, rx, ry, rw, rh, radius);
                float body = Smooth(0.75f, -0.75f, d);

                float a = shadow * (1 - body) + body;
                if (a <= 0.002f) continue;

                float r = acc.r, g = acc.g, bb = acc.b;
                if (body > 0.002f) {
                    // Level ring: grows with the microphone level.
                    float dr = MathF.Abs(Dist(fx, fy, cx, cy) - ringR);
                    float ring = Smooth(1.6f * scale, 0.4f * scale, dr) * (0.25f + 0.75f * Clamp01(level * 3f));
                    // Mic glyph.
                    float glyph = Mic(fx, fy, cx, cy, scale);
                    float mark = Clamp01(ring * (busy ? 0.35f : 1f) + glyph);
                    r = Mix(r, ink.r, mark); g = Mix(g, ink.g, mark); bb = Mix(bb, ink.b, mark);

                }

                // Shadow is black; only the pill body carries colour.
                float sa = shadow * (1 - body);
                r *= body / MathF.Max(a, 1e-4f); g *= body / MathF.Max(a, 1e-4f); bb *= body / MathF.Max(a, 1e-4f);

                px[y * w + x] = Premultiplied(r, g, bb, a * opacity);
            }
        }
    }

    /// Mic capsule + cradle arc + stem, all as distance fields.
    static float Mic(float x, float y, float cx, float cy, float s) {
        float top = cy - 8f * s, bot = cy - 1f * s;
        float capsule = Segment(x, y, cx, top, cx, bot) - 3.2f * s;
        float body = Smooth(0.8f, -0.8f, capsule);

        float dr = MathF.Abs(Dist(x, y, cx, cy - 1.5f * s) - 6.2f * s);
        float arc = y > cy - 1.5f * s ? Smooth(1.6f * s, 0.5f * s, dr) : 0f;

        float stem = Segment(x, y, cx, cy + 4.7f * s, cx, cy + 8f * s) - 1.1f * s;
        float st = Smooth(0.8f, -0.8f, stem);

        return Clamp01(body + arc + st);
    }

    static float RoundRect(float x, float y, float rx, float ry, float rw, float rh, float rad) {
        float qx = MathF.Abs(x - (rx + rw / 2)) - (rw / 2 - rad);
        float qy = MathF.Abs(y - (ry + rh / 2)) - (rh / 2 - rad);
        float ox = MathF.Max(qx, 0), oy = MathF.Max(qy, 0);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0) - rad;
    }

    static float Segment(float px, float py, float ax, float ay, float bx, float by) {
        float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
        float t = Clamp01((wx * vx + wy * vy) / MathF.Max(vx * vx + vy * vy, 1e-5f));
        return Dist(px, py, ax + vx * t, ay + vy * t);
    }

    static float Dist(float x, float y, float ax, float ay) { float dx = x - ax, dy = y - ay; return MathF.Sqrt(dx * dx + dy * dy); }
    static float Smooth(float a, float b, float x) => Clamp01((x - a) / (b - a));
    static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    static float Mix(float a, float b, float t) => a + (b - a) * t;
    static (float, float, float) Unpack(uint c) => (((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);

    static uint Premultiplied(float r, float g, float b, float a) {
        a = Clamp01(a);
        byte A = (byte)(a * 255 + 0.5f);
        byte R = (byte)(Clamp01(r) * a * 255 + 0.5f);
        byte G = (byte)(Clamp01(g) * a * 255 + 0.5f);
        byte B = (byte)(Clamp01(b) * a * 255 + 0.5f);
        return ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
    }
}
