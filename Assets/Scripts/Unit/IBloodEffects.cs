using UnityEngine;

// 맞은 자리에 피를 튀긴다. 아군(UnitController)과 적 엔티티(EnemyHorde)가 같은 것을 쓴다.
public interface IBloodEffects
{
    // 원본의 진하기와 알파는 유지되고 색조만 bloodColor로 바뀐다(고블린은 초록).
    GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Color bloodColor);
}
