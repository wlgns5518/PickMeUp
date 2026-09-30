using UnityEngine;

// 방위(북쪽 0도에서 시계 방향으로 잰 각도)를 다루는 곳. 마을 배치는 전부 방위로 적혀 있다.
public static class Compass
{
    /// 방위 → 바닥에 누운 단위 벡터. 0도는 +Z(북), 90도는 +X(동).
    public static Vector3 Direction(float bearingDegrees)
    {
        float radians = bearingDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
    }
}
