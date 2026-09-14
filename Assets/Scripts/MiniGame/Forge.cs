using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// 장비제작소의 제작 진입점.
//
// 무엇을 만드느냐는 고른 무기가, 얼마나 좋게 나오느냐는 넣는 재료의 등급이 정한다(EquipmentCraftTable 참조).
// 자동 제작은 퍼즐 없이 바로 재료 등급 그대로 나온다.
// 수동 제작은 난이도를 고르면 PuzzleGame을 그 난이도로 띄우고, 성공하면 재료 등급을 밑변으로
// 그 난이도표만큼 위로 오른 등급이 나온다 — 어려운 난이도일수록 상위 등급이 나올 가능성이 크다.
//
// 만들 수 있는 무기는 WeaponCatalog에 든 무기 중 모델이 있는 것 전부다(CollectCraftable).
// 새 무기 에셋을 카탈로그에 넣으면 제작소 목록에도 그대로 따라 뜬다. 맨손 시전처럼 손에 드는 것이
// 없는 항목은 만들 물건이 아니라 빠진다.
//
// 만든 장비는 곧바로 무기창고(EquipmentInventory)에 들어가고, 무기창고 창에서 영웅에게 들린다.
// 결과는 onCrafted/onFailed로도 알린다. EquipmentWorkshopUI가 여기 붙어 화면에 띄운다.
public class Forge : MonoBehaviour
{
    [Header("Puzzle 연동")]
    [Tooltip("비워두면 씬에서 찾는다.")]
    [SerializeField] private PuzzleGame puzzle;

    [System.Serializable] public class CraftedEvent : UnityEvent<CraftedEquipment> { }

    [Header("Events")]
    public CraftedEvent onCrafted;
    public UnityEvent onFailed;

    // 퍼즐은 성공/실패를 UnityEvent로만 알려 주고 어떤 시도였는지는 모른다. 여기서 기억해 둔다.
    private WeaponDefinition pendingWeapon;
    private EquipmentGrade pendingMaterial;
    private PuzzleDifficulty pendingDifficulty;
    private bool awaitingPuzzle;

    private void Awake()
    {
        if (puzzle == null) puzzle = FindAnyObjectByType<PuzzleGame>(FindObjectsInactive.Include);
        if (puzzle == null)
        {
            Debug.LogWarning("[Forge] PuzzleGame을 찾지 못해 수동 제작을 시작할 수 없습니다.", this);
            return;
        }

        puzzle.onSuccess.AddListener(HandlePuzzleSuccess);
        puzzle.onFail.AddListener(HandlePuzzleFail);
    }

    public static bool IsCraftable(WeaponDefinition weapon) => weapon != null && weapon.model != null;

    // 제작소 창이 늘어놓을 무기 목록. 카탈로그 순서를 그대로 따른다.
    public static void CollectCraftable(List<WeaponDefinition> results)
    {
        if (results == null) return;
        results.Clear();

        WeaponCatalog catalog = WeaponCatalog.Instance;
        if (catalog == null) return;

        for (int i = 0; i < catalog.weapons.Count; i++)
        {
            WeaponDefinition w = catalog.weapons[i];
            if (IsCraftable(w)) results.Add(w);
        }
    }

    // 자동 제작: 퍼즐 없이 바로 재료 등급 그대로 만든다.
    public void CraftAuto(WeaponDefinition weapon, EquipmentGrade material)
    {
        if (!IsCraftable(weapon))
        {
            Debug.LogWarning($"[Forge] 만들 수 없는 무기입니다: {(weapon != null ? weapon.name : "null")}", this);
            return;
        }

        var result = new CraftedEquipment(weapon, EquipmentCraftTable.RollAuto(material));
        Debug.Log($"[Forge] 자동 제작(재료 {material}): {result.name} ({EquipmentGradeNames.NameOf(result.grade)})");
        Deliver(result);
    }

    // 수동 제작: 고른 무기·재료 등급·난이도를 기억해 두고 퍼즐을 띄운다.
    // 결과는 퍼즐 성공/실패 콜백에서 처리한다.
    public void StartManual(WeaponDefinition weapon, EquipmentGrade material, PuzzleDifficulty difficulty)
    {
        if (puzzle == null) return;
        if (!IsCraftable(weapon))
        {
            Debug.LogWarning($"[Forge] 만들 수 없는 무기입니다: {(weapon != null ? weapon.name : "null")}", this);
            return;
        }

        pendingWeapon = weapon;
        pendingMaterial = material;
        pendingDifficulty = difficulty;
        awaitingPuzzle = true;
        puzzle.StartPuzzle(null, difficulty);
    }

    private void HandlePuzzleSuccess()
    {
        // 이 퍼즐이 우리가 띄운 시도가 아니면(다른 기능이 같은 PuzzleGame을 쓰게 되는 경우) 무시한다.
        if (!awaitingPuzzle) return;
        awaitingPuzzle = false;

        EquipmentGrade grade = EquipmentCraftTable.RollManual(pendingMaterial, pendingDifficulty);
        var result = new CraftedEquipment(pendingWeapon, grade);
        Debug.Log($"[Forge] 수동 제작 성공(재료 {pendingMaterial}, {pendingDifficulty}): {result.name} ({EquipmentGradeNames.NameOf(grade)})");
        Deliver(result);
    }

    // 창고에 먼저 넣고 알린다. 알림을 받은 쪽이 창고를 읽었을 때 방금 만든 장비가 이미 있어야 한다.
    private void Deliver(CraftedEquipment result)
    {
        EquipmentInventory.Add(result.weapon, result.grade);
        onCrafted?.Invoke(result);
    }

    private void HandlePuzzleFail()
    {
        if (!awaitingPuzzle) return;
        awaitingPuzzle = false;

        Debug.Log($"[Forge] 수동 제작 실패({(pendingWeapon != null ? pendingWeapon.DisplayName : "?")}) — 재료 소모, 보상 없음");
        onFailed?.Invoke();
    }
}
