using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// 미니게임이 화면을 덮는 동안 뒤쪽 3D 장면을 그리지 않게 한다.
//
// 미니게임 화면은 불투명한 배경으로 화면을 통째로 가린다. 그래도 카메라는 그대로 마을을 향하고
// 있어서, 프러스텀 컬링은 마을을 "시야 안"으로 판정해 살려 둔다 — UI가 앞을 가린다는 사실은
// 카메라가 알 방법이 없다(ScreenSpaceOverlay 캔버스는 카메라 렌더링이 끝난 뒤에 따로 그려진다).
// 그래서 아무도 볼 수 없는 그림에 지형 컬링, 나무, 그림자 캐스터 계산이 매 프레임 그대로 들어간다.
//
// 카메라를 통째로 끄지는 않는다. 그러면 화면을 지우는 주체가 사라져 에디터는
// "No cameras rendering"을 띄우고, 빌드에서는 UI가 덮지 않은 자리에 이전 프레임 찌꺼기가
// 남을 수 있다. 대신 그릴 레이어를 비우고 단색으로 지우게 한다.
//
// 미니게임이 둘 이상 열릴 수도 있으므로 열려 있는 화면을 세어 둔다. 마지막 하나가 닫힐 때만
// 원래 값으로 되돌린다.
public static class MiniGameWorldView
{
    private static readonly List<MiniGameScreen> screens = new List<MiniGameScreen>();

    private static Camera worldCamera;
    // 되돌릴 기본값은 "전부 그리기"와 스카이박스. 도메인 리로드로 저장값이 날아가도
    // 화면이 텅 빈 채로 굳지 않고 정상 쪽으로 실패한다.
    private static int savedCullingMask = -1;
    private static CameraClearFlags savedClearFlags = CameraClearFlags.Skybox;

    public static bool IsHidden => screens.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 에디터에서 지난 플레이의 화면 목록이 남지 않도록 비운다.
        screens.Clear();
        worldCamera = null;
        savedCullingMask = -1;
        savedClearFlags = CameraClearFlags.Skybox;
    }

    public static void Hide(MiniGameScreen screen, Camera camera)
    {
        if (screen == null) return;

        Prune();
        if (screens.Contains(screen)) return;

        screens.Add(screen);
        // 이미 다른 미니게임이 가려 두었다면 지금 값은 "비워둔 값"이다. 그걸 원본으로 저장하면 안 된다.
        if (screens.Count > 1) return;

        Camera cam = Resolve(camera);
        if (cam == null) return;

        savedCullingMask = cam.cullingMask;
        savedClearFlags = cam.clearFlags;

        cam.cullingMask = 0;
        cam.clearFlags = CameraClearFlags.SolidColor;
        SetPhysicsRaycaster(cam, false);
    }

    public static void Show(MiniGameScreen screen)
    {
        if (screen != null) screens.Remove(screen);

        Prune();
        if (screens.Count > 0) return;   // 아직 열려 있는 미니게임이 있다

        Camera cam = Resolve(null);
        if (cam == null) return;

        cam.cullingMask = savedCullingMask;
        cam.clearFlags = savedClearFlags;
        SetPhysicsRaycaster(cam, true);
    }

    // 파괴된 화면이 목록에 남으면 마지막 하나가 닫혀도 영영 되돌아오지 않는다.
    private static void Prune()
    {
        for (int i = screens.Count - 1; i >= 0; i--)
            if (screens[i] == null) screens.RemoveAt(i);
    }

    private static Camera Resolve(Camera preferred)
    {
        if (preferred != null) worldCamera = preferred;
        if (worldCamera != null) return worldCamera;

        worldCamera = Camera.main;
        if (worldCamera != null) return worldCamera;

        // Camera.main은 꺼져 있거나 태그만 붙은 카메라를 놓칠 때가 있다. 여기서 못 찾으면
        // 미니게임을 닫아도 화면을 되돌릴 수 없으므로 태그로 한 번 더 훑는다.
        Camera[] all = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (!all[i].CompareTag("MainCamera")) continue;

            worldCamera = all[i];
            break;
        }

        return worldCamera;
    }

    // 3D 클릭 판정도 함께 멈춘다. 켜 둔 채로는 미니게임 위에서 포인터를 움직일 때마다
    // 가려진 지형과 마을 건물에 대고 레이를 쏜다.
    private static void SetPhysicsRaycaster(Camera cam, bool on)
    {
        var raycaster = cam.GetComponent<PhysicsRaycaster>();
        if (raycaster != null) raycaster.enabled = on;
    }
}
