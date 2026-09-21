using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image characterImage;
    [SerializeField] private TMP_Text characterNameText;

    [Header("Stars")]
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private float starSpacing = 30f;
    [SerializeField] private float starYOffset = -80f;
    [SerializeField] private int maxStars = 7;

    public CharacterSO Character { get; private set; }

    // 한 번 만든 별은 지우지 않고 꺼 두었다가 다시 쓴다. 카드는 편성·합성 창에서 다른 캐릭터로 수없이
    // 다시 칠해지고(합성 자리는 카드를 올리고 내릴 때마다 ResetCard → Apply), 그때마다 별을 부수고 새로
    // 만들면 카드 한 장에 최대 일곱 개가 생겼다 사라졌다. 필요한 수만큼만 켜고 나머지는 꺼 둔다.
    private readonly List<GameObject> spawnedStars = new List<GameObject>();
    private int appliedStarCount = -1;

    public void ResetCard()
    {
        Character = null;
        if (characterNameText != null) characterNameText.text = "";
        ShowPortrait(null);
        ClearStars();
        appliedStarCount = -1;
    }

    public void Apply(CharacterSO so)
    {
        if (so == null) return;
        Character = so;

        if (characterNameText != null)
            characterNameText.text = string.IsNullOrEmpty(so.characterName) ? "이름 없음" : so.characterName;

        ShowPortrait(so.portrait);

        if (appliedStarCount != so.starCount)
        {
            DisplayStars(so.starCount);
            appliedStarCount = so.starCount;
        }
    }

    // 초상화 칸을 채우거나, 없으면 통째로 꺼 둔다.
    //
    // 스프라이트를 비운 Image는 사라지는 게 아니라 흰 사각형으로 그려진다. 그 사각형이
    // 카드 한가운데를 덮고 이름바 뒤까지 내려와서, 소환 직후처럼 초상화가 아직 없는 카드는
    // 이름이 흰 바탕에 묻혀 읽히지 않았다. 초상화가 없으면 카드 그림이 그대로 보이는 편이 낫다.
    private void ShowPortrait(Sprite portrait)
    {
        if (characterImage == null) return;

        characterImage.sprite = portrait;
        characterImage.preserveAspect = true;
        characterImage.enabled = portrait != null;
    }

    private void DisplayStars(int count)
    {
        count = starPrefab != null ? Mathf.Clamp(count, 0, maxStars) : 0;

        float startX = -(count - 1) * 0.5f * starSpacing;
        for (int i = 0; i < count; i++)
        {
            GameObject star = TakeStar(i);

            Vector2 pos = new Vector2(startX + i * starSpacing, starYOffset);
            if (star.transform is RectTransform rt)
            {
                rt.anchoredPosition = pos;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }
            else
            {
                star.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
                star.transform.localRotation = Quaternion.identity;
                star.transform.localScale = Vector3.one;
            }
            star.SetActive(true);
        }

        // 이번 등급보다 많이 만들어 둔 별은 꺼 둔다.
        for (int i = count; i < spawnedStars.Count; i++)
            if (spawnedStars[i] != null) spawnedStars[i].SetActive(false);
    }

    // i번째 별. 만들어 둔 것이 있으면 그것을, 없으면(또는 밖에서 지워졌으면) 새로 만든다.
    private GameObject TakeStar(int index)
    {
        GameObject star = index < spawnedStars.Count ? spawnedStars[index] : null;
        if (star != null) return star;

        star = Instantiate(starPrefab, transform);
        star.name = $"Star_{index + 1}";

        if (index < spawnedStars.Count) spawnedStars[index] = star;
        else spawnedStars.Add(star);
        return star;
    }

    private void ClearStars()
    {
        for (int i = 0; i < spawnedStars.Count; i++)
            if (spawnedStars[i] != null) spawnedStars[i].SetActive(false);
    }
}
