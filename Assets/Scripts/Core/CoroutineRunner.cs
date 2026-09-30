using System.Collections;
using UnityEngine;

// 씬이 갈려도 죽지 않는 코루틴 자리. MonoBehaviour가 아닌 서비스(몸 굽기 줄 등)가 코루틴을 돌릴 때 빌린다.
//
// 처음 필요할 때 숨은 오브젝트 하나를 만든다. 서비스가 스스로 MonoBehaviour가 되어 싱글턴을 들고 있으면,
// 코루틴 하나 때문에 그 서비스의 상태까지 게임오브젝트 수명에 묶인다.
public sealed class CoroutineRunner : MonoBehaviour
{
    private static CoroutineRunner host;

    public static Coroutine Run(IEnumerator routine)
    {
        if (host == null)
        {
            var go = new GameObject("[CoroutineRunner]");
            DontDestroyOnLoad(go);
            host = go.AddComponent<CoroutineRunner>();
        }

        return host.StartCoroutine(routine);
    }
}
