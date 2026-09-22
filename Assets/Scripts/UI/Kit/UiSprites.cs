using System.Collections.Generic;
using UnityEngine;

// 부품이 쓰는 모양(둥근 판, 테두리, 그림자, 원, 작은 기호)을 코드로 그려 만든다.
//
// 모양이 반지름·두께 몇 가지뿐이라 PNG로 두면 크기마다 파일이 늘고, 모서리를 한 번 바꾸려면 전부 다시 그려야 한다.
// 여기서는 필요한 모양을 처음 쓰일 때 한 번 그려 캐시한다. 흰색으로 그리므로 색은 Image.color로 칠한다.
//
// 텍스처 1px = 캔버스 1단위(스프라이트 100ppu, 캔버스 기준 100ppu)라 9-슬라이스 테두리가 곧 반지름이다.
// 가장자리는 부호 거리로 1px 안티에일리어싱한다.
public static class UiSprites
{
    private const float PixelsPerUnit = 100f;

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public enum Glyph { ChevronLeft, ChevronRight, ChevronDown, Plus, Check, Close, Lock, Info }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        // 도메인 리로드를 끈 에디터에서 지난 플레이의 텍스처(이미 파괴됨)를 붙들지 않게 비운다.
        Cache.Clear();
    }

    // ---- 판 -----------------------------------------------------------------

    /// 속이 찬 둥근 사각형(9-슬라이스). 원으로 쓰려면 칸을 정사각으로 두고 radius를 칸의 절반으로.
    public static Sprite Rounded(int radius)
    {
        radius = Mathf.Max(1, radius);
        return Get($"fill{radius}", () =>
        {
            int size = radius * 2 + 4;
            return Build(size, size, radius + 2, (x, y) => Coverage(RoundedSdf(x, y, size, size, radius)));
        });
    }

    /// 둥근 사각형의 테두리만(9-슬라이스).
    public static Sprite Outline(int radius, int thickness)
    {
        radius = Mathf.Max(1, radius);
        thickness = Mathf.Max(1, thickness);
        return Get($"line{radius}_{thickness}", () =>
        {
            int size = radius * 2 + 4;
            return Build(size, size, radius + 2, (x, y) =>
            {
                float d = RoundedSdf(x, y, size, size, radius);
                return Coverage(d) * Mathf.Clamp01(d + thickness + 0.5f);
            });
        });
    }

    /// 부드러운 그림자(9-슬라이스). 판보다 blur만큼 넓게 깔아 쓴다(UiKit.Surface).
    public static Sprite Shadow(int radius, int blur)
    {
        radius = Mathf.Max(1, radius);
        blur = Mathf.Max(1, blur);
        return Get($"shadow{radius}_{blur}", () =>
        {
            int inner = radius + blur;
            int size = inner * 2 + 4;
            return Build(size, size, inner + 2, (x, y) =>
            {
                // 판 모양에서 바깥으로 blur만큼 옅어진다. 제곱해 가장자리를 더 부드럽게 흘린다.
                float d = RoundedSdf(x, y, size, size, inner) + blur;
                float t = Mathf.Clamp01(1f - d / blur);
                return t * t;
            });
        });
    }

    // ---- 기호 ---------------------------------------------------------------
    //
    // 글꼴(NotoSansKR)에 없거나 굵기가 들쭉날쭉한 기호는 선분으로 직접 그린다. 64px 한 장을 늘려 쓴다.

    public static Sprite Icon(Glyph glyph)
    {
        return Get("glyph" + glyph, () =>
        {
            const int size = 64;
            return Build(size, size, 0, (x, y) => GlyphCoverage(glyph, x, y, size));
        });
    }

    private static float GlyphCoverage(Glyph glyph, float x, float y, int size)
    {
        // 0~1 좌표(아래가 0). 두께는 한 변의 1/9.
        float u = x / size, v = y / size;
        const float T = 0.11f;

        switch (glyph)
        {
            case Glyph.ChevronLeft:
                return Stroke(u, v, T, 0.62f, 0.2f, 0.34f, 0.5f, 0.62f, 0.8f, size);
            case Glyph.ChevronRight:
                return Stroke(u, v, T, 0.38f, 0.2f, 0.66f, 0.5f, 0.38f, 0.8f, size);
            case Glyph.ChevronDown:
                return Stroke(u, v, T, 0.2f, 0.62f, 0.5f, 0.34f, 0.8f, 0.62f, size);
            case Glyph.Plus:
                return Mathf.Max(Segment(u, v, 0.5f, 0.18f, 0.5f, 0.82f, T, size), Segment(u, v, 0.18f, 0.5f, 0.82f, 0.5f, T, size));
            case Glyph.Check:
                return Stroke(u, v, T * 1.1f, 0.2f, 0.52f, 0.42f, 0.3f, 0.82f, 0.72f, size);
            case Glyph.Close:
                return Mathf.Max(Segment(u, v, 0.24f, 0.24f, 0.76f, 0.76f, T, size), Segment(u, v, 0.24f, 0.76f, 0.76f, 0.24f, T, size));
            case Glyph.Info:
                return Mathf.Max(Segment(u, v, 0.5f, 0.2f, 0.5f, 0.56f, T, size), Dot(u, v, 0.5f, 0.76f, T * 0.75f, size));
            case Glyph.Lock:
                return LockCoverage(u, v, size);
            default:
                return 0f;
        }
    }

    // 자물쇠: 아래는 둥근 몸통, 위는 고리(반원 테두리 + 양쪽 기둥).
    private static float LockCoverage(float u, float v, int size)
    {
        float px = 1f / size;
        // 몸통: 가로 0.2~0.8, 세로 0.12~0.56
        float bx = Mathf.Abs(u - 0.5f) - 0.3f + 0.08f;
        float by = Mathf.Abs(v - 0.34f) - 0.22f + 0.08f;
        float body = Mathf.Sqrt(Mathf.Max(bx, 0f) * Mathf.Max(bx, 0f) + Mathf.Max(by, 0f) * Mathf.Max(by, 0f))
                     + Mathf.Min(Mathf.Max(bx, by), 0f) - 0.08f;
        float bodyCov = Mathf.Clamp01(0.5f - body / px);

        // 고리: 중심 (0.5, 0.6), 반지름 0.18, 두께 0.09. 위쪽 반원 + 몸통까지 내려오는 기둥.
        const float R = 0.18f, T = 0.09f;
        float dx = u - 0.5f, dy = v - 0.6f;
        float ring = dy >= 0f
            ? Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - R) - T * 0.5f
            : Mathf.Abs(Mathf.Abs(dx) - R) - T * 0.5f;
        float ringCov = v > 0.5f ? Mathf.Clamp01(0.5f - ring / px) : 0f;

        return Mathf.Max(bodyCov, ringCov);
    }

    // 꺾인 선 하나(두 선분).
    private static float Stroke(float u, float v, float t, float ax, float ay, float bx, float by, float cx, float cy, int size) =>
        Mathf.Max(Segment(u, v, ax, ay, bx, by, t, size), Segment(u, v, bx, by, cx, cy, t, size));

    private static float Segment(float u, float v, float ax, float ay, float bx, float by, float thickness, int size)
    {
        float abx = bx - ax, aby = by - ay;
        float t = Mathf.Clamp01(((u - ax) * abx + (v - ay) * aby) / (abx * abx + aby * aby));
        float dx = u - (ax + abx * t), dy = v - (ay + aby * t);
        float d = Mathf.Sqrt(dx * dx + dy * dy) - thickness * 0.5f;
        return Mathf.Clamp01(0.5f - d * size);
    }

    private static float Dot(float u, float v, float cx, float cy, float radius, int size)
    {
        float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - radius;
        return Mathf.Clamp01(0.5f - d * size);
    }

    // ---- 그리기 ---------------------------------------------------------------

    private delegate float Shader(float x, float y);

    private static Sprite Get(string key, System.Func<Sprite> make)
    {
        if (Cache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;

        sprite = make();
        Cache[key] = sprite;
        return sprite;
    }

    private static Sprite Build(int width, int height, int border, Shader alphaAt)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "UiSprite",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float a = Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit,
            0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.name = "UiSprite";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // 둥근 사각형까지의 부호 거리(안쪽이 음수). 칸 안쪽 1px 여백을 두고 그린다.
    private static float RoundedSdf(float x, float y, int width, int height, int radius)
    {
        float hx = width * 0.5f - 1f, hy = height * 0.5f - 1f;
        float qx = Mathf.Abs(x - width * 0.5f) - (hx - radius);
        float qy = Mathf.Abs(y - height * 0.5f) - (hy - radius);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static float Coverage(float signedDistance) => Mathf.Clamp01(0.5f - signedDistance);
}
