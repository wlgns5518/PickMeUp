using System.Collections.Generic;
using UnityEngine;

// 유니티 기본 도형(상자, 공, 원기둥 …)의 메시를 한 번만 꺼내 나눠 쓰는 곳.
//
// GameObject.CreatePrimitive는 늘 콜라이더를 붙여서 나온다. 도형 수백 개를 세우는 마을 블록아웃과
// 조립기에서 매번 그걸 만들어 컴포넌트를 붙였다 떼면 값을 조금 고칠 때마다 다시 만드는 에디터 작업이
// 체감될 만큼 느려진다. 메시는 어차피 기본 도형 몇 개뿐이므로 한 번 꺼내 캐시해 두고 공유한다.
//
// 여기 담기는 것은 유니티 내장 에셋이다. 만든 메시를 정리하는 쪽(VillageBlockout.ClearGenerated 등)이
// 이것까지 지우면 안 된다.
public static class BuiltinMeshes
{
    private static readonly Dictionary<PrimitiveType, Mesh> Cache = new Dictionary<PrimitiveType, Mesh>();

    public static Mesh Get(PrimitiveType type)
    {
        if (Cache.TryGetValue(type, out Mesh cached) && cached != null) return cached;

        GameObject sample = GameObject.CreatePrimitive(type);
        Mesh mesh = sample.GetComponent<MeshFilter>().sharedMesh;
        UnityObjects.Destroy(sample);

        Cache[type] = mesh;
        return mesh;
    }

    public static Mesh Cube => Get(PrimitiveType.Cube);
}
