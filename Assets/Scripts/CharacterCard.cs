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

    private readonly List<GameObject> spawnedStars = new List<GameObject>();
    private int appliedStarCount = -1;

    public void ResetCard()
    {
        Character = null;
        if (characterNameText != null) characterNameText.text = "";
        ClearStars();
        appliedStarCount = -1;
    }

    public void Apply(CharacterSO so)
    {
        if (so == null) return;
        Character = so;

        if (characterNameText != null)
            characterNameText.text = string.IsNullOrEmpty(so.characterName) ? "이름 없음" : so.characterName;

        if (so.portrait != null && characterImage != null)
        {
            characterImage.sprite = so.portrait;
            characterImage.preserveAspect = true;
        }

        if (appliedStarCount != so.starCount)
        {
            DisplayStars(so.starCount);
            appliedStarCount = so.starCount;
        }
    }

    private void DisplayStars(int count)
    {
        ClearStars();
        count = Mathf.Clamp(count, 0, maxStars);
        if (count == 0 || starPrefab == null) return;

        float startX = -(count - 1) * 0.5f * starSpacing;
        for (int i = 0; i < count; i++)
        {
            GameObject star = Instantiate(starPrefab, transform);
            star.name = $"Star_{i + 1}";

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
            spawnedStars.Add(star);
        }
    }

    private void ClearStars()
    {
        for (int i = 0; i < spawnedStars.Count; i++)
            if (spawnedStars[i] != null) Destroy(spawnedStars[i]);
        spawnedStars.Clear();
    }
}
