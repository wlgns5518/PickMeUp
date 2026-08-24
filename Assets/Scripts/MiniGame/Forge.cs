using UnityEngine;
using UnityEngine.Events;

// 장비제작소의 제작 진입점.
//
// 무엇이 나오느냐는 넣는 재료의 등급이 정한다(EquipmentCraftTable 참조).
// 자동 제작은 퍼즐 없이 바로 재료 등급 그대로 나온다.
// 수동 제작은 난이도를 고르면 PuzzleGame을 그 난이도로 띄우고, 성공하면 재료 등급을 밑변으로
// 그 난이도표만큼 위로 오른 등급이 나온다 — 어려운 난이도일수록 상위 등급이 나올 가능성이 크다.
//
// 인벤토리 시스템이 아직 없어 결과는 onCrafted/onFailed로만 알린다. EquipmentWorkshopUI가
// 여기 붙어 화면에 띄운다.
public class Forge : MonoBehaviour
{
    [Header("Puzzle 연동")]
    [Tooltip("비워두면 씬에서 찾는다.")]
    [SerializeField] private PuzzleGame puzzle;

    [Header("결과")]
    [Tooltip("만들어질 장비 이름 (지금은 placeholder, 추후 ItemSO 시스템으로 확장)")]
    [SerializeField] private string equipmentName = "강철 검";

    [System.Serializable] public class CraftedEvent : UnityEvent<CraftedEquipment> { }

    [Header("Events")]
    public CraftedEvent onCrafted;
    public UnityEvent onFailed;

    // 퍼즐은 성공/실패를 UnityEvent로만 알려 주고 어떤 시도였는지는 모른다. 여기서 기억해 둔다.
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

    // 자동 제작: 퍼즐 없이 바로 재료 등급 그대로 만든다.
    public void CraftAuto(EquipmentGrade material)
    {
        var result = new CraftedEquipment(equipmentName, EquipmentCraftTable.RollAuto(material));
        Debug.Log($"[Forge] 자동 제작(재료 {material}): {equipmentName} ({EquipmentGradeNames.NameOf(result.grade)})");
        onCrafted?.Invoke(result);
    }

    // 수동 제작: 고른 재료 등급과 난이도를 기억해 두고 퍼즐을 띄운다.
    // 결과는 퍼즐 성공/실패 콜백에서 처리한다.
    public void StartManual(EquipmentGrade material, PuzzleDifficulty difficulty)
    {
        if (puzzle == null) return;

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
        var result = new CraftedEquipment(equipmentName, grade);
        Debug.Log($"[Forge] 수동 제작 성공(재료 {pendingMaterial}, {pendingDifficulty}): {equipmentName} ({EquipmentGradeNames.NameOf(grade)})");
        onCrafted?.Invoke(result);
    }

    private void HandlePuzzleFail()
    {
        if (!awaitingPuzzle) return;
        awaitingPuzzle = false;

        Debug.Log("[Forge] 수동 제작 실패 — 재료 소모, 보상 없음");
        onFailed?.Invoke();
    }
}
