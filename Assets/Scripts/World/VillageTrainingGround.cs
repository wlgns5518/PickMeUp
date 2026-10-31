using System.Collections.Generic;
using UnityEngine;

// 훈련소 마당에서 주민이 설 자리. 허수아비 앞(혼자 치기)과 대련 자리(둘이 맞붙기)를 나눠 준다.
//
// 자리는 훈련소 구역(VillageFacility, kind = Training)에서 읽는다. 허수아비는 시설 레벨에 따라 3~5개라
// 켜져 있는 것만 센다. 시설을 올리면 VillageBlockout이 구역을 통째로 다시 세우므로, 구역이 사라졌으면
// 다음에 물을 때 다시 찾는다 — 그때 이미 자리에 선 주민은 제 시간까지 마저 하고 떠난다.
public sealed class VillageTrainingGround
{
    // 프리팹 공간(VillagePrefabAssembler.BuildTraining)의 치수. 구역 배율(size / designRadius)이 곱해진다.
    private const float YardRadius = 25f;
    private const string DummyName = "training_dummy";
    // 대련 자리 두 곳. 허수아비 줄(z 15) 뒤 성벽 쪽, 허수아비 사이 틈(x ±4) 바로 뒤다.
    // 주민을 3배로 키우자(2026-10-31) 허수아비 앞 마당(z 15~25)에는 허수아비를 치는 사람만으로 꽉 차서 뒤로 옮겼다.
    private static readonly Vector3[] RingCenters = { new Vector3(-4f, 0f, 9.5f), new Vector3(4f, 0f, 9.5f) };
    // 대련 자리로 갈 때 허수아비 사이 틈을 지나는 디딤돌. 곧장 가면 가운데 허수아비를 뚫고 지나간다.
    private static readonly float[] RingApproachZ = { 19f, 12f };
    // 들어오는 길목. 마을 길에서 마당으로 들어설 때 여기서 가장 가까운 길 마디를 디딤돌로 쓴다.
    private static readonly Vector3 EntranceLocal = new Vector3(0f, 0f, 22f);

    // 아래 거리는 사람 크기(배율 1) 기준의 미터다. 주민 배율(bodyScale)을 곱해 쓴다.
    // 허수아비 중심에서 떨어져 서는 거리. 근접은 칼이 닿는 거리, 원거리는 활·마법을 쏘는 거리다.
    // 원거리를 더 멀리 세우면 3배 몸에서 마당 밖(등불 줄)으로 나간다.
    private const float MeleeStandOff = 2.2f;
    private const float RangedStandOff = 3.5f;
    // 대련하는 둘 사이 거리의 절반. 무기 리치(1.7~2.9m)에 맞춰 칼끝이 상대 앞에서 멈춘다.
    private const float SparHalfGap = 1.3f;

    private readonly float bodyScale;

    public VillageTrainingGround(float bodyScale)
    {
        this.bodyScale = Mathf.Max(0.1f, bodyScale);
    }

    public sealed class Dummy
    {
        public Transform target;
        public VillageResident occupant;
    }

    public sealed class Ring
    {
        public Vector3 center;
        public Vector3 axis;
        public float halfGap;
        public readonly List<Vector3> approach = new List<Vector3>();
        public VillageResident first, second;

        public bool Free => first == null && second == null;
        public Vector3 Spot(int side) => VillageRoadGraph.GroundAt(center + axis * (side == 0 ? -halfGap : halfGap));
    }

    private VillageFacility facility;
    private float nextSearch;
    private readonly List<Dummy> dummies = new List<Dummy>();
    private readonly List<Ring> rings = new List<Ring>();

    public bool Available => Refresh();
    public Vector3 Entrance => facility.transform.TransformPoint(EntranceLocal);

    // 마당 안인가. 무기는 이 안에서만 든다. 반원이라 성벽 쪽(-Z) 뒤편은 마당이 아니다.
    public bool Contains(Vector3 world)
    {
        if (!Refresh()) return false;
        Vector3 local = facility.transform.InverseTransformPoint(world);
        return local.z > -2f && new Vector2(local.x, local.z).sqrMagnitude < YardRadius * YardRadius;
    }

    public Dummy FreeDummy()
    {
        if (!Refresh()) return null;
        int free = 0;
        foreach (Dummy d in dummies) if (IsFree(d)) free++;
        if (free == 0) return null;

        // 늘 첫 허수아비부터 채우면 가운데만 붐빈다.
        int pick = Random.Range(0, free);
        foreach (Dummy d in dummies)
        {
            if (!IsFree(d)) continue;
            if (pick-- == 0) return d;
        }
        return null;
    }

    private static bool IsFree(Dummy d) => d.occupant == null && d.target != null && d.target.gameObject.activeInHierarchy;

    public Ring FreeRing()
    {
        if (!Refresh()) return null;
        foreach (Ring ring in rings) if (ring.Free) return ring;
        return null;
    }

    // 허수아비 앞에 설 자리와 바라볼 방향.
    public Vector3 DummySpot(Dummy dummy, bool ranged, out Quaternion facing)
    {
        Vector3 forward = Vector3.ProjectOnPlane(facility.transform.forward, Vector3.up).normalized;
        facing = Quaternion.LookRotation(-forward);
        Vector3 spot = dummy.target.position + forward * ((ranged ? RangedStandOff : MeleeStandOff) * bodyScale);
        return VillageRoadGraph.GroundAt(spot);
    }

    public void Release(VillageResident resident)
    {
        foreach (Dummy d in dummies) if (d.occupant == resident) d.occupant = null;
        foreach (Ring ring in rings)
        {
            if (ring.first == resident) ring.first = null;
            if (ring.second == resident) ring.second = null;
        }
    }

    private bool Refresh()
    {
        if (facility != null) return true;
        // 주민마다 매 프레임 묻는다(Contains). 훈련소가 없는 동안 씬을 매번 뒤지지 않게 간격을 둔다.
        if (Time.unscaledTime < nextSearch) return false;
        nextSearch = Time.unscaledTime + 2f;

        dummies.Clear();
        rings.Clear();
        foreach (VillageFacility candidate in Object.FindObjectsByType<VillageFacility>())
        {
            if (candidate.kind != VillageBlockout.Kind.Training) continue;
            facility = candidate;
            break;
        }
        if (facility == null) return false;

        // 꺼진 것(2레벨부터 나오는 허수아비)도 담아 두고 고를 때 켜져 있는지를 본다 — 레벨이 바뀌면 부품만 켜고 끄기도 한다.
        // 허수아비 모델 안에 같은 이름의 자식이 있을 수 있어 바깥 것만 센다.
        foreach (Transform child in facility.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith(DummyName) && !child.parent.name.StartsWith(DummyName))
                dummies.Add(new Dummy { target = child });

        Vector3 axis = Vector3.ProjectOnPlane(facility.transform.right, Vector3.up).normalized;
        foreach (Vector3 local in RingCenters)
        {
            var ring = new Ring
            {
                center = facility.transform.TransformPoint(local),
                axis = axis,
                halfGap = SparHalfGap * bodyScale,
            };
            foreach (float z in RingApproachZ)
                ring.approach.Add(VillageRoadGraph.GroundAt(facility.transform.TransformPoint(new Vector3(local.x, 0f, z))));
            rings.Add(ring);
        }
        return true;
    }
}
