using TMPro;
using UnityEngine;

// 화면에 캐릭터 이름을 적을 때 쓰는 공용 표기 규칙.
//
// 부고와 결과창이 같은 모양("셰이(★★★★)")을 써야 하는데, 별을 그리는 방법이 까다로워
// 양쪽에 같은 코드를 두 벌 두면 한쪽만 고쳐지기 쉽다.
public static class HeroLabel
{
    // 한국어 폰트(NotoSansKR)에는 ★ 글리프가 없어 카드가 쓰는 Star.png를 스프라이트로 찍는다.
    // TMP 규약상 Resources 아래 "Sprite Assets" 폴더에 두면 이름으로 불러올 수 있다.
    public const string StarSpriteResourcePath = "Sprite Assets/StarSprites";

    private const string StarTag = "<sprite=0>";

    public static TMP_SpriteAsset LoadStarSprites()
    {
        return Resources.Load<TMP_SpriteAsset>(StarSpriteResourcePath);
    }

    public static string Name(CharacterSO character)
    {
        if (character == null) return "이름 없음";
        return string.IsNullOrEmpty(character.characterName) ? character.name : character.characterName;
    }

    // "셰이(★★★★)". 스프라이트 에셋이 없으면 별을 그릴 방법이 없어 숫자로 적는다.
    public static string NameWithStars(CharacterSO character, bool useStarSprites)
    {
        if (character == null) return "이름 없음";

        string name = Name(character);
        if (!useStarSprites) return name + "(" + character.starCount + "성)";

        var builder = new System.Text.StringBuilder(name.Length + character.starCount * StarTag.Length + 2);
        builder.Append(name).Append('(');
        for (int i = 0; i < character.starCount; i++) builder.Append(StarTag);
        builder.Append(')');
        return builder.ToString();
    }

    // 받침이 있으면 "이", 없으면 "가". 이름마다 조사를 손으로 고를 수는 없다.
    public static string SubjectParticle(string name)
    {
        if (string.IsNullOrEmpty(name)) return "가";

        for (int i = name.Length - 1; i >= 0; i--)
        {
            char c = name[i];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0 ? "이" : "가";
            if (char.IsLetterOrDigit(c)) break; // 한글이 아니면 판별할 수 없다.
        }
        return "가";
    }
}
