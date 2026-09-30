using UnityEngine;

// 색을 숫자로 다루는 도구. UI 테마, 마을 팔레트, 에디터의 텍스처 굽기가 같이 쓴다.
public static class ColorMath
{
    /// 0xRRGGBB → 불투명한 색. 디자인 문서의 색 코드를 그대로 옮겨 적을 수 있게.
    public static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    /// 사람 눈이 느끼는 밝기(Rec.601 가중치). 재질·텍스처를 밝기로 맞출 때 쓴다.
    public static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
}
