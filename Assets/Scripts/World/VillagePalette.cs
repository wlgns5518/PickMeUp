using UnityEngine;

// 마을(메인 거점) 다크 판타지 팔레트 — 2026-09-25 사용자가 지정한 색(sRGB).
// "차가운 어두운 세계 + 따뜻한 건축 재질 + 청록색 마법광". 저채도 회청색 돌, 어두운 녹색 식생, 갈색 목재,
// 중요한 시설에만 청록 마법광, 아주 일부에만 금색·주황.
// 머티리얼을 굽는 도구(VillageGaiaSkin·VillageGroundTextures·VillagePartBaker·VillageMood)와 광장 블록아웃이 같이 쓴다.
// 텍스처가 있는 재질은 이 색으로 "덮지" 않고, 텍스처의 명암·결을 남긴 채 평균 색만 이 값으로 옮긴다.
public static class VillagePalette
{
    // 바닥·석재
    public static readonly Color DarkStone = Hex(0x343D40);
    public static readonly Color Stone = Hex(0x59656A);
    public static readonly Color LightStone = Hex(0x687579);

    // 목재
    public static readonly Color DarkWood = Hex(0x493B32);
    public static readonly Color Wood = Hex(0x584237);

    // 지붕
    public static readonly Color DarkRoof = Hex(0x30383A);
    public static readonly Color Roof = Hex(0x41494A);

    // 식생
    public static readonly Color DarkForest = Hex(0x1E3029);
    public static readonly Color Grass = Hex(0x354735);
    public static readonly Color LightGrass = Hex(0x526044);

    // 마법·포인트
    public static readonly Color MagicCyan = Hex(0x55BFC0);
    public static readonly Color MagicBright = Hex(0xA5FFFF);
    public static readonly Color Gold = Hex(0xC5A85A);
    public static readonly Color WarmOrange = Hex(0xB87342);

    private static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
