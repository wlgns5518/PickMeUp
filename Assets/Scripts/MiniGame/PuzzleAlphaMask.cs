using System.Collections.Generic;
using UnityEngine;

// 스프라이트의 알파를 픽셀 하나당 비트 하나로 압축해 들고 있는 마스크.
//
// 퍼즐이 픽셀에서 알아내야 하는 건 두 가지뿐이다 — 그림이 실제로 차지하는 영역이 어디까지인지,
// 그리고 칸 하나에 불투명 픽셀이 몇 개나 들어 있는지. 둘 다 "이 픽셀이 임계를 넘는가"를 세는
// 일이라 색은 필요 없고 비트 하나면 된다.
//
// GetPixels32()는 1024x1536 텍스처에서 6.3MB를 잡는다. 슬라이스할 때마다 새로 잡고 통째로
// 버리면 퍼즐을 다시 시작할 때마다 그만큼이 쓰레기가 되는데, 같은 이미지를 같은 임계로 읽는 한
// 답은 늘 같다. 비트로 줄이면 같은 이미지가 192KB고, 스프라이트마다 한 번만 만들어 재사용한다.
internal sealed class PuzzleAlphaMask
{
    private const int WordBits = 64;

    // 스프라이트당 하나. 텍스처 자체가 어차피 메모리에 올라가 있고 마스크는 그 1/32이라,
    // 들고 있는 편이 매번 다시 만드는 것보다 싸다.
    private static readonly Dictionary<Sprite, PuzzleAlphaMask> Cache =
        new Dictionary<Sprite, PuzzleAlphaMask>();

    private readonly ulong[] bits;
    private readonly int originX;
    private readonly int originY;
    private readonly int width;
    private readonly int height;
    private readonly int wordsPerRow;

    // 이 마스크를 만들 때 쓴 알파 임계. 인스펙터에서 값을 바꾸면 다시 만들어야 한다.
    private readonly float threshold;

    // 알파가 있는 픽셀들의 타이트 바운딩 박스 (텍스처 좌표).
    // 검처럼 투명한 여백이 넓은 그림에서 실제로 잘라야 할 영역이다.
    public Rect ContentRect { get; private set; }

    // 텍스처를 읽지 못하면 null. 부르는 쪽은 알파를 모르는 채로 진행해야 한다.
    public static PuzzleAlphaMask Get(Sprite sprite, float alphaThreshold)
    {
        if (sprite == null) return null;

        if (Cache.TryGetValue(sprite, out PuzzleAlphaMask cached))
        {
            if (cached != null && Mathf.Approximately(cached.threshold, alphaThreshold)) return cached;
            Cache.Remove(sprite);
        }

        Texture2D tex = sprite.texture;
        if (tex == null) return null;

        Color32[] pixels;
        try { pixels = tex.GetPixels32(); }
        catch (System.Exception e)
        {
            Debug.LogError($"[Puzzle] 픽셀 읽기 실패: {e.Message}\n" +
                           $"→ 텍스처 '{tex.name}'의 Read/Write Enabled를 켜주세요.");
            return null;
        }

        var mask = new PuzzleAlphaMask(sprite.textureRect, tex.width, tex.height, pixels, alphaThreshold);
        Cache[sprite] = mask;
        return mask;
    }

    private PuzzleAlphaMask(Rect region, int texWidth, int texHeight, Color32[] pixels, float alphaThreshold)
    {
        threshold = alphaThreshold;

        originX = Mathf.Clamp(Mathf.FloorToInt(region.x), 0, Mathf.Max(0, texWidth  - 1));
        originY = Mathf.Clamp(Mathf.FloorToInt(region.y), 0, Mathf.Max(0, texHeight - 1));
        width   = Mathf.Clamp(Mathf.RoundToInt(region.width),  1, texWidth  - originX);
        height  = Mathf.Clamp(Mathf.RoundToInt(region.height), 1, texHeight - originY);

        // 행을 워드 경계에서 끊는다. 행마다 몇 비트가 남아 돌지만, 그 대가로 어떤 행이든
        // 인덱스 계산 없이 곧장 집을 수 있다.
        wordsPerRow = (width + WordBits - 1) / WordBits;
        bits = new ulong[wordsPerRow * height];

        byte cutoff = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaThreshold) * 255f);
        int minX = width, minY = height, maxX = -1, maxY = -1;

        // 비트를 채우면서 바운딩 박스도 같이 잡는다. 어차피 전체를 한 번 훑어야 한다.
        for (int y = 0; y < height; y++)
        {
            int srcRow = (originY + y) * texWidth + originX;
            int dstRow = y * wordsPerRow;
            bool rowHit = false;

            for (int x = 0; x < width; x++)
            {
                if (pixels[srcRow + x].a < cutoff) continue;

                bits[dstRow + (x >> 6)] |= 1UL << (x & 63);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                rowHit = true;
            }

            if (!rowHit) continue;
            if (y < minY) minY = y;
            maxY = y;
        }

        // 알파가 하나도 없으면 어디를 잘라야 할지 정할 수 없다. 영역을 통째로 쓴다.
        ContentRect = maxX < 0
            ? new Rect(originX, originY, width, height)
            : new Rect(originX + minX, originY + minY, maxX - minX + 1, maxY - minY + 1);
    }

    // 칸 하나 안의 불투명 픽셀 수. cap에 닿는 순간 멈추고 그 값을 돌려준다 — 부르는 쪽은
    // "임계를 넘었는가"만 보므로 넘은 뒤로는 정확한 수가 쓸모없다.
    //
    // cap에 못 미친 결과는 끝까지 센 값이라 정확하다. 0을 0으로 믿을 수 있는 이유이고,
    // 그림이 한 픽셀도 없는 칸을 걸러내는 판정이 여기에 걸려 있다.
    public int CountOpaque(Rect rect, int cap)
    {
        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.x) - originX, 0, width);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(rect.y) - originY, 0, height);
        int x1 = Mathf.Clamp(x0 + Mathf.RoundToInt(rect.width),  x0, width);
        int y1 = Mathf.Clamp(y0 + Mathf.RoundToInt(rect.height), y0, height);

        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            count += CountRow(y, x0, x1);
            if (count >= cap) return count;
        }
        return count;
    }

    // 한 행의 [x0, x1) 구간에 켜진 비트 수. 구간 밖의 비트는 양끝 워드에서 마스크로 잘라낸다.
    private int CountRow(int y, int x0, int x1)
    {
        if (x1 <= x0) return 0;

        int rowStart  = y * wordsPerRow;
        int firstWord = x0 >> 6;
        int lastWord  = (x1 - 1) >> 6;

        ulong headMask = ulong.MaxValue << (x0 & 63);
        ulong tailMask = ulong.MaxValue >> (63 - ((x1 - 1) & 63));

        if (firstWord == lastWord)
            return PopCount(bits[rowStart + firstWord] & headMask & tailMask);

        int count = PopCount(bits[rowStart + firstWord] & headMask);
        for (int w = firstWord + 1; w < lastWord; w++) count += PopCount(bits[rowStart + w]);
        return count + PopCount(bits[rowStart + lastWord] & tailMask);
    }

    // SWAR 팝카운트. System.Numerics.BitOperations는 이 런타임 프로필에 없다.
    private static int PopCount(ulong v)
    {
        v -= (v >> 1) & 0x5555555555555555UL;
        v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
        v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
        return (int)((v * 0x0101010101010101UL) >> 56);
    }
}
