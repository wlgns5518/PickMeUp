using UnityEngine;

public static class UnityObjects
{
    /// 플레이 중이면 Destroy, 에디터에서 값을 고쳐 다시 세우는 중이면 DestroyImmediate.
    /// 에디터 모드에서 Destroy를 부르면 유니티가 거부하고, 플레이 중에 DestroyImmediate를 부르면
    /// 같은 프레임에 아직 그 오브젝트를 보는 코드가 터진다.
    public static void Destroy(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }
}
