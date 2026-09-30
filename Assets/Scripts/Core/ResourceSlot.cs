using UnityEngine;

// Resources 폴더에 한 장만 두는 설정 에셋(무기 카탈로그, 무기 애니메이션 표, 아이콘 모음, UI 스킨)을
// 처음 물을 때 한 번만 읽어 쥐고 있는 칸.
//
// 없을 때도 한 번만 찾는다. 매번 Resources.Load를 다시 부르면 에셋이 없는 동안 프레임마다 디스크를 뒤진다.
// 예전에는 이 캐시·검색 표시·리셋·경고가 네 클래스에 한 글자씩 다르게 복사돼 있었다.
public sealed class ResourceSlot<T> where T : Object
{
    private readonly string resourceName;
    private readonly string missingWarning;
    private T cached;
    private bool searched;

    /// missingWarning을 주면 에셋이 없을 때 한 번 경고를 남긴다.
    public ResourceSlot(string resourceName, string missingWarning = null)
    {
        this.resourceName = resourceName;
        this.missingWarning = missingWarning;
    }

    /// 없으면 null. 부르는 쪽이 대체 동작(글자 아이콘, 단색 UI, 맨손 컨트롤러 …)으로 떨어진다.
    public T Value
    {
        get
        {
            if (cached != null) return cached;
            if (searched) return null;

            searched = true;
            cached = Resources.Load<T>(resourceName);
            if (cached == null && missingWarning != null) Debug.LogWarning(missingWarning);
            return cached;
        }
    }

    /// 도메인 리로드를 끈 에디터에서 지난 플레이의 참조가 남지 않도록 플레이 시작마다 비운다.
    public void Reset()
    {
        cached = null;
        searched = false;
    }
}
