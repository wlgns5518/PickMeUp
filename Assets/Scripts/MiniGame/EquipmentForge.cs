using UnityEngine;

// 미니게임 성공 시 호출되어 장비를 만들거나 보상을 지급하는 진입점.
// EquipmentPuzzleGame.onSuccess 에 OnPuzzleSuccess를 연결하면 됨.
public class EquipmentForge : MonoBehaviour
{
    [Header("Puzzle 연동")]
    [SerializeField] private EquipmentPuzzleGame puzzle;

    [Header("결과")]
    [Tooltip("성공 시 만들어질 장비 (지금은 placeholder, 추후 EquipmentSO 시스템으로 확장)")]
    [SerializeField] private string equipmentName = "강철 검";

    public void OnPuzzleSuccess()
    {
        Debug.Log($"[Forge] 장비 생성 성공: {equipmentName}");
        // TODO: 실제 EquipmentSO 인스턴스 생성, 인벤토리 추가 등
    }

    public void OnPuzzleFail()
    {
        Debug.Log("[Forge] 장비 생성 실패 — 재료 소모, 보상 없음");
        // TODO: 재료 소비 처리, 실패 페널티
    }

    // 외부에서 난이도 선택 후 호출
    public void StartEasy()   { puzzle?.StartPuzzle(null, PuzzleDifficulty.Easy);   }
    public void StartNormal() { puzzle?.StartPuzzle(null, PuzzleDifficulty.Normal); }
    public void StartHard()   { puzzle?.StartPuzzle(null, PuzzleDifficulty.Hard);   }
    public void StartHell()   { puzzle?.StartPuzzle(null, PuzzleDifficulty.Hell);   }
}
