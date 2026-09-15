using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// 장비제작소의 제작 진입점.
//
// 재료 세 개를 넣으면 무기가 랜덤으로 나온다. 무엇을 만들지는 고르지 않는다 —
// 가장 많이 넣은 재료 종류가 계열(검·도끼… / 활·창… / 방패)을, 세 재료의 평균 등급이 결과 등급의
// 밑변을 정하고, 계열 안의 무기는 운으로 굴린다(CraftRecipe).
// 자동 제작은 퍼즐 없이 바로 밑변 등급 그대로 나온다.
// 수동 제작은 난이도를 고르면 PuzzleGame을 그 난이도로 띄우고, 성공하면 밑변에서 그 난이도표만큼
// 위로 오른 등급이 나온다 — 어려운 난이도일수록 상위 등급이 나올 가능성이 크다(EquipmentCraftTable).
//
// 재료는 제작을 시작하는 순간 창고(MaterialInventory)에서 빠진다. 수동 제작도 퍼즐을 띄울 때 뺀다 —
// 결과가 나올 때 빼면 퍼즐 도중 게임을 끄는 것으로 실패를 없던 일로 만들 수 있다.
//
// 만들 수 있는 무기는 WeaponCatalog에 든 무기 중 모델이 있는 것 전부다(CollectCraftable).
// 새 무기 에셋을 카탈로그에 넣으면 제작 후보에도 그대로 들어간다. 맨손 시전처럼 손에 드는 것이
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
    private WeaponFamily pendingFamily;
    private EquipmentGrade pendingBaseGrade;
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

    // 제작 후보가 되는 무기 전부. 카탈로그 순서를 그대로 따른다.
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

    // 자동 제작: 재료를 빼고 퍼즐 없이 바로 밑변 등급 그대로 만든다.
    // 만들지 못하면 false와 그 이유를 돌려준다 — 그때는 재료도 빠지지 않는다.
    public bool CraftAuto(IReadOnlyList<CraftMaterial> materials, out string reason)
    {
        if (!CanStart(materials, out reason)) return false;
        if (!MaterialInventory.TryConsume(materials))
        {
            reason = "넣은 재료가 창고에 모자랍니다.";
            return false;
        }

        WeaponFamily family = CraftRecipe.FamilyOf(materials);
        EquipmentGrade grade = EquipmentCraftTable.RollAuto(CraftRecipe.BaseGradeOf(materials));
        var result = new CraftedEquipment(CraftRecipe.RollWeapon(family), grade);
        Deliver(result);
        return true;
    }

    // 수동 제작: 재료를 빼고, 계열·밑변 등급·난이도를 기억해 둔 채 퍼즐을 띄운다.
    // 결과는 퍼즐 성공/실패 콜백에서 처리한다.
    public bool StartManual(IReadOnlyList<CraftMaterial> materials, PuzzleDifficulty difficulty, out string reason)
    {
        if (puzzle == null)
        {
            reason = "퍼즐을 찾지 못해 수동 제작을 시작할 수 없습니다.";
            return false;
        }
        if (awaitingPuzzle)
        {
            reason = "이미 제작 중입니다.";
            return false;
        }
        if (!CanStart(materials, out reason)) return false;
        if (!MaterialInventory.TryConsume(materials))
        {
            reason = "넣은 재료가 창고에 모자랍니다.";
            return false;
        }

        pendingFamily = CraftRecipe.FamilyOf(materials);
        pendingBaseGrade = CraftRecipe.BaseGradeOf(materials);
        pendingDifficulty = difficulty;
        awaitingPuzzle = true;
        puzzle.StartPuzzle(null, difficulty);
        return true;
    }

    private static bool CanStart(IReadOnlyList<CraftMaterial> materials, out string reason)
    {
        reason = null;
        if (!CraftRecipe.IsComplete(materials))
        {
            reason = $"재료 {CraftRecipe.SlotCount}개를 모두 넣으세요.";
            return false;
        }

        // 재료를 태우기 전에 나올 무기가 있는지부터 본다. 카탈로그가 비었으면 재료만 사라진다.
        if (CraftRecipe.RollWeapon(WeaponFamily.Any) == null)
        {
            reason = "만들 수 있는 무기가 없습니다. WeaponCatalog를 확인하세요.";
            return false;
        }
        return true;
    }

    private void HandlePuzzleSuccess()
    {
        // 이 퍼즐이 우리가 띄운 시도가 아니면(다른 기능이 같은 PuzzleGame을 쓰게 되는 경우) 무시한다.
        if (!awaitingPuzzle) return;
        awaitingPuzzle = false;

        EquipmentGrade grade = EquipmentCraftTable.RollManual(pendingBaseGrade, pendingDifficulty);
        var result = new CraftedEquipment(CraftRecipe.RollWeapon(pendingFamily), grade);
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

        onFailed?.Invoke();
    }
}
