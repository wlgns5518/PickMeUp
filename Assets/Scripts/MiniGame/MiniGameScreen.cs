using UnityEngine;

// 미니게임 화면 하나를 표시하는 표식.
//
// 미니게임은 저마다 화면 루트를 켜고 끄는 식으로 열고 닫는다(PuzzleGame.puzzleRoot,
// RhythmGame.rhythmRoot). 그 루트에 이걸 붙여 두면 여닫는 코드를 건드리지 않아도
// 열릴 때 뒤쪽 3D 장면이 멈추고 닫힐 때 되돌아온다.
//
// 새 미니게임을 만들 때는 화면 루트에 이 컴포넌트를 붙이거나 Ensure를 한 번 부르면 된다.
// 실제로 무엇을 멈추는지는 MiniGameWorldView가 안다 — 미니게임 쪽은 알 필요가 없다.
[DisallowMultipleComponent]
public class MiniGameScreen : MonoBehaviour
{
    [Tooltip("멈출 월드 카메라. 비워두면 Camera.main을 쓴다.")]
    [SerializeField] private Camera worldCamera;

    [Tooltip("이 화면이 떠 있는 동안 뒤쪽 3D 장면 그리기를 멈춘다. 지형·나무·그림자 계산이 통째로 빠진다.")]
    [SerializeField] private bool hideWorld = true;

    /// 화면 루트에 이 컴포넌트가 있는지 보장한다. 미니게임이 열릴 때 한 번 부르면
    /// 씬에 손으로 붙여두지 않아도 동작한다.
    public static MiniGameScreen Ensure(GameObject root)
    {
        if (root == null) return null;

        var screen = root.GetComponent<MiniGameScreen>();
        if (screen == null) screen = root.AddComponent<MiniGameScreen>();
        return screen;
    }

    private void OnEnable()
    {
        if (hideWorld) MiniGameWorldView.Hide(this, worldCamera);
    }

    // 되돌리는 쪽은 설정을 따지지 않는다. 화면이 떠 있는 도중에 hideWorld를 꺼 버렸을 때
    // 비워둔 카메라를 되살릴 길이 없어지면 화면이 검은 채로 굳는다.
    private void OnDisable()
    {
        MiniGameWorldView.Show(this);
    }
}
