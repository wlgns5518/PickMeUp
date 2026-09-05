using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

// 구워 놓은 적 애니메이션 한 벌. 베이커(EnemyAnimationBaker)가 만들고 셰이더가 읽는다.
//
// 엔티티에는 Animator가 없으므로 뼈를 CPU에서 돌릴 수 없다. 대신 클립을 미리 샘플링해서
// "몇 번 프레임의 몇 번 뼈가 어떤 행렬인가"를 텍스처 한 장에 굽고, 정점 셰이더가 그것을
// 읽어 스키닝한다. 1000마리가 전부 같은 텍스처를 보므로 마리 수가 늘어도 비용이 늘지 않는다.
//
// 정점을 통째로 굽는 방식(VAT)도 있는데 여기서는 뼈를 골랐다. 고블린이 정점 8,976개에
// 뼈 46개라, 정점을 구우면 프레임당 8,976줄이 필요하지만 뼈는 46개면 된다 — 30배 차이다.
[CreateAssetMenu(fileName = "EnemyAnimationLibrary", menuName = "PickMeUp/Enemy Animation Library")]
public class EnemyAnimationLibrary : ScriptableObject
{
    [Serializable]
    public struct ClipRange
    {
        public EnemyClip clip;

        // 텍스처에서 이 클립이 차지하는 구간.
        public int startFrame;
        public int frameCount;

        // 초 단위 길이. 재생 속도를 클립 길이에 맞추는 데 쓴다.
        public float length;
    }

    [Tooltip("스킨 정보(뼈 번호·가중치)를 UV2/UV3에 넣어 둔 메시. 베이커가 만든다.")]
    public Mesh skinnedMesh;

    [Tooltip("뼈 행렬을 구운 텍스처. 가로 = 뼈 수 x 3, 세로 = 전체 프레임 수.")]
    public Texture2D boneTexture;

    [Tooltip("이 메시를 그릴 머티리얼. 셰이더가 boneTexture를 읽어야 한다.")]
    public Material material;

    public int boneCount;

    public ClipRange[] clips = Array.Empty<ClipRange>();

    public bool IsBaked => skinnedMesh != null && boneTexture != null && material != null && clips.Length > 0;

    public bool TryGetClip(EnemyClip clip, out ClipRange range)
    {
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].clip != clip) continue;

            range = clips[i];
            return true;
        }

        range = default;
        return false;
    }

    // 셰이더가 읽을 수 있게 클립 구간을 배열로 펴 둔다. 매 프레임 찾지 않도록
    // 한 번 만들어 두고 렌더 시스템이 그대로 쓴다.
    public NativeArray<float4> BuildLookup(Allocator allocator)
    {
        int count = Enum.GetValues(typeof(EnemyClip)).Length;
        var lookup = new NativeArray<float4>(count, allocator);

        for (int i = 0; i < clips.Length; i++)
        {
            ClipRange range = clips[i];
            int index = (int)range.clip;
            if (index < 0 || index >= count) continue;

            lookup[index] = new float4(range.startFrame, range.frameCount, range.length, 0f);
        }

        return lookup;
    }
}

// 셰이더에 넘기는 이번 프레임의 애니메이션 상태.
//
// x = 클립 시작 프레임, y = 클립 프레임 수, z = 진행도(0~1), w = 텍스처 세로 크기.
// 정점 셰이더는 이 넷으로 읽을 줄 번호를 계산한다.
[MaterialProperty("_EnemyAnimData")]
public struct EnemyAnimationMaterial : IComponentData
{
    public float4 Value;
}
