using UnityEngine;

// 큰 한 방이 화면에 남기는 진동. 전투 코드는 흔들어 달라고만 하고, 카메라는 흔들린 만큼을 읽기만 한다.
public interface ICombatShake
{
    // character는 이 한 방의 주인공이다 — 스킬을 맞힌 쪽, 흘려낸 쪽, 물린 쪽, 쓰러진 쪽.
    // 카메라가 그 캐릭터를 비추고 있을 때만 흔든다. strength는 0~1.
    void Emit(UnitController character, float strength);

    // 카메라가 LateUpdate에서 부른다. 흔들 것이 없으면 false.
    bool TrySample(Vector3 listener, out Vector3 offset, out Quaternion rotation);
}

// 카메라가 없는 씬(테스트, 마을)에서 대신 앉는 것. 아무것도 흔들지 않는다 —
// 부르는 쪽이 매번 "흔들 카메라가 있는가"를 확인하지 않아도 되게.
public sealed class NullCombatShake : ICombatShake
{
    public static readonly NullCombatShake Instance = new NullCombatShake();

    public void Emit(UnitController character, float strength) { }

    public bool TrySample(Vector3 listener, out Vector3 offset, out Quaternion rotation)
    {
        offset = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }
}
