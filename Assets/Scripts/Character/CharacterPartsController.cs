using UnityEngine;

// 캐릭터 프리팹에 붙여서 CharacterSO에 맞게 파츠를 활성화한다.
// 인스펙터에 프리팹 안의 각 파츠 오브젝트를 끌어다 연결.
public class CharacterPartsController : MonoBehaviour
{
    [Header("Outfit Sets (프리팹 자식 오브젝트)")]
    [SerializeField] private GameObject setA;     // 전사 갑옷
    [SerializeField] private GameObject setC;     // 마법사 로브
    [SerializeField] private GameObject setBase;  // 꾸질꾸질한 천옷
    [SerializeField] private GameObject setNo;    // 알몸/속옷

    [Header("Hair (2종)")]
    [SerializeField] private GameObject hair0;
    [SerializeField] private GameObject hair1;

    [Header("Beard (남자만)")]
    [SerializeField] private GameObject beard;

    public CharacterSO Character { get; private set; }

    /// SO 한 번에 적용
    public void Apply(CharacterSO so)
    {
        if (so == null) return;
        Character = so;

        ApplyOutfit(so.outfit);
        ApplyHair(so.hairIndex);
        ApplyBeard(so.HasBeard);
    }

    public void ApplyOutfit(OutfitSet set)
    {
        SetActive(setA,    set == OutfitSet.A);
        SetActive(setC,    set == OutfitSet.C);
        SetActive(setBase, set == OutfitSet.Base);
        SetActive(setNo,   set == OutfitSet.No);
    }

    public void ApplyHair(int index)
    {
        SetActive(hair0, index == 0);
        SetActive(hair1, index == 1);
    }

    public void ApplyBeard(bool show)
    {
        SetActive(beard, show);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
