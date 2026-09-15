using UnityEditor;
using UnityEngine;

// 층을 깨지 않고도 제작소를 시험해 볼 수 있게 재료를 넣어 주는 메뉴. 빌드에는 들어가지 않는다.
//
// 실제 세이브 파일에 쓴다. 플레이 중에 누르면 열려 있는 제작소 창에 바로 반영되고,
// 플레이 밖에서 누르면 다음 플레이에서 읽힌다.
public static class MaterialTestMenu
{
    private const int PerStack = 3;

    [MenuItem("PickMeUp/Equipment/테스트 재료 넣기 (종류x등급마다 3개)")]
    private static void GrantAll()
    {
        foreach (MaterialKind kind in MaterialNames.AllKinds)
        {
            for (var grade = EquipmentGrade.E; grade <= EquipmentGrade.S; grade++)
                MaterialInventory.Add(new CraftMaterial(kind, grade), PerStack);
        }

        Debug.Log($"[MaterialTestMenu] 재료를 넣었습니다. 지금 보유: {MaterialInventory.TotalCount}개\n경로: {SaveSystem.SavePath}");
    }
}
