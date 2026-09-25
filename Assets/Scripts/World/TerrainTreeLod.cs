using UnityEngine;

// 지형 나무 LOD 치우침(Terrain.treeLODBiasMultiplier)을 켤 때마다 적는다 — 이 값은 씬에 저장되지 않는다(에디터에서 바꿔도
// 플레이하면 1로 돌아왔다). 메인 거점의 성벽 밖 숲은 카메라에서 150m 넘게 떨어져 있어 낮은 LOD로 충분하다:
// 0.2와 1은 화면에서 구별이 안 됐고, 삼각형 176만→100만, 드로우콜 2,811→1,741이었다(VillageDecor가 심는 숲).
[ExecuteAlways]
[RequireComponent(typeof(Terrain))]
public class TerrainTreeLod : MonoBehaviour
{
    [SerializeField, Range(0.05f, 2f)] private float lodBias = 0.2f;

    private void OnEnable() => Apply();

    private void OnValidate() => Apply();

    private void Apply()
    {
        var terrain = GetComponent<Terrain>();
        if (terrain != null) terrain.treeLODBiasMultiplier = lodBias;
    }
}
