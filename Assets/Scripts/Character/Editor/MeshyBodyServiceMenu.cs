using UnityEditor;
using UnityEngine;

// 소환할 때 몸까지 굽는 것을 켜고 끄는 손잡이.
//
// 켜 두는 것이 기본이다 — 소환한 캐릭터는 제 몸으로 싸워야 한다. 다만 소환 한 번에
// 44크레딧이 나가므로, 소환 흐름 자체를 반복해서 시험할 때는 꺼 두는 편이 낫다.
// 꺼 두어도 이미 받아 둔 몸은 그대로 쓰인다(디스크의 GLB를 읽는 길은 크레딧과 무관하다).
public static class MeshyBodyServiceMenu
{
    private const string MenuPath = "PickMeUp/Character/소환할 때 3D 몸도 굽기";

    [MenuItem(MenuPath, priority = 10)]
    private static void Toggle()
    {
        // 켜졌는지는 메뉴의 체크 표시가 보여 준다(ToggleValidate).
        MeshyBodyService.Enabled = !MeshyBodyService.Enabled;
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, MeshyBodyService.Enabled);
        return true;
    }

    [MenuItem("PickMeUp/Character/받아 둔 3D 몸 폴더 열기", priority = 11)]
    private static void RevealStore()
    {
        System.IO.Directory.CreateDirectory(CharacterModelStore.Root);
        EditorUtility.RevealInFinder(CharacterModelStore.Root);
    }
}
