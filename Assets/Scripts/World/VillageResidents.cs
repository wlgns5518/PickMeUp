using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// 거점 마을에 보유 캐릭터를 풀어 놓는 곳. 각자 길을 따라 돌아다니고(VillageResident), 몇은 훈련소로 불려 가
// 허수아비를 치거나 둘씩 대련한다.
//
// 씬에 따로 놓지 않는다. 마을(VillageBlockout)이 있는 씬이 열리면 스스로 하나 생긴다 — MainScene을 저장하면
// Gaia가 지형 캐시 값을 흔들어 놓는 잡음이 따라오므로, 씬을 건드리지 않고 붙는 편을 골랐다.
//
// 몸은 전투와 같은 것(CharacterBodyFactory가 GLB에서 세운 원형)을 복사해 입는다. 원형에는 전투 부품
// (UnitController·TargetScanner·UnitEmotion·NavMeshAgent)이 붙어 있는데, 그대로 깨우면 UnitRegistry에 아군으로
// 등록되고 마을에 없는 NavMesh를 찾다 경고를 쏟는다. 그래서 꺼진 부모 아래에서 복사해 깨어나기 전에 걷어 낸다.
// 손에 무기를 쥐는 WeaponEquipper와 양손 잡기 WeaponHandIK는 남긴다 — 훈련소에서 쓴다.
public class VillageResidents : MonoBehaviour
{
    [Tooltip("마을에 한꺼번에 나오는 주민 수의 상한. 보유 캐릭터가 이보다 많으면 명단 앞쪽부터 나온다.")]
    [SerializeField, Min(0)] private int maxResidents = 24;

    [Tooltip("주민 몸의 배율. 전투 몸(배율 1)을 그대로 세우면 마을 카메라(거리 250m)에서 점으로만 보인다 — 2026-10-31 사용자 요청으로 3배. " +
             "걷는 속도와 훈련소에서 서는 거리도 이 배율을 따른다.")]
    [SerializeField, Min(0.1f)] private float residentScale = 3f;

    [Tooltip("명단을 다시 맞춰 보는 간격(초). 새로 소환해 몸이 막 구워진 캐릭터, 합성 재료로 사라진 캐릭터를 이때 반영한다.")]
    [SerializeField, Min(0.5f)] private float rosterCheckInterval = 2f;

    [Tooltip("훈련소로 부를 사람을 고르는 간격(초).")]
    [SerializeField, Min(0.5f)] private float trainingCallInterval = 3f;

    [Tooltip("한 번 고를 때 산책 중인 주민 하나가 훈련소로 불려 갈 확률.")]
    [SerializeField, Range(0f, 1f)] private float trainingCallChance = 0.12f;

    [Tooltip("한꺼번에 훈련소에 가 있을 수 있는 주민의 비율. 자리(허수아비 3~5개 + 대련 두 쌍)가 남아도 이 이상은 부르지 않는다 — " +
             "0.2·상한 없음으로 돌려 보니 1분 만에 8명 중 7명이 훈련소로 몰려 마을이 비었다.")]
    [SerializeField, Range(0f, 1f)] private float maxTrainingShare = 0.4f;

    [Tooltip("한 번 훈련하는 시간(초). 자리에 닿은 뒤부터 잰다.")]
    [SerializeField] private Vector2 trainingDuration = new Vector2(25f, 50f);

    private VillageBlockout village;
    private VillageRoadGraph roads;
    private VillageTrainingGround ground;
    private readonly Dictionary<string, VillageResident> residents = new Dictionary<string, VillageResident>();
    private readonly HashSet<string> requested = new HashSet<string>();
    private readonly List<CharacterSO> wanted = new List<CharacterSO>();
    private readonly List<VillageResident> idle = new List<VillageResident>();
    private Transform staging;
    private float nextRosterCheck;
    private float nextTrainingCall;
    private bool rosterDirty = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindAnyObjectByType<VillageBlockout>() == null) return;
        if (FindAnyObjectByType<VillageResidents>() != null) return;
        new GameObject("[VillageResidents]").AddComponent<VillageResidents>();
    }

    private void OnEnable() => OwnedRoster.Changed += OnRosterChanged;
    private void OnDisable() => OwnedRoster.Changed -= OnRosterChanged;

    // 합성 재료로 사라진 캐릭터는 그 자리에서 들여보낸다. 원형의 몸이 같은 순간 재질·메시째 내려지므로(CharacterBodyFactory.Forget)
    // 다음 확인까지 미루면 그 몸을 빌려 입은 주민이 한동안 재질 없이 그려진다.
    private void OnRosterChanged()
    {
        if (roads != null && roads.Count > 0) SyncRoster();
        else rosterDirty = true;
    }

    private void Start()
    {
        village = FindAnyObjectByType<VillageBlockout>();
        ground = new VillageTrainingGround(residentScale);
        roads = VillageRoadGraph.Build(village);
        if (roads.Count == 0)
        {
            Debug.LogWarning("[VillageResidents] 마을에 걸을 길이 없어 주민을 내보내지 않는다.", this);
            enabled = false;
            return;
        }

        // 복사한 몸이 깨어나기 전에 전투 부품을 걷어 낼 자리.
        var holder = new GameObject("Staging");
        holder.SetActive(false);
        holder.transform.SetParent(transform, false);
        staging = holder.transform;

        nextTrainingCall = Time.time + trainingCallInterval;
    }

    private void Update()
    {
        if (Time.time >= nextRosterCheck || rosterDirty)
        {
            rosterDirty = false;
            nextRosterCheck = Time.time + rosterCheckInterval;
            SyncRoster();
        }

        if (Time.time >= nextTrainingCall)
        {
            nextTrainingCall = Time.time + trainingCallInterval;
            CallToTraining();
        }
    }

    // ------------------------------------------------------------------
    // 명단
    // ------------------------------------------------------------------

    private void SyncRoster()
    {
        wanted.Clear();
        IReadOnlyList<CharacterSO> owned = OwnedRoster.Members;
        for (int i = 0; i < owned.Count && wanted.Count < maxResidents; i++)
        {
            CharacterSO c = owned[i];
            if (c != null && !PartyRoster.IsFallen(c)) wanted.Add(c);
        }

        // 명단에서 빠진 사람은 들여보낸다.
        var gone = new List<string>();
        foreach (KeyValuePair<string, VillageResident> pair in residents)
            if (pair.Value == null || !wanted.Contains(pair.Value.Character)) gone.Add(pair.Key);
        foreach (string id in gone)
        {
            if (residents[id] != null) Destroy(residents[id].gameObject);
            residents.Remove(id);
        }

        foreach (CharacterSO c in wanted)
        {
            if (residents.ContainsKey(c.Id)) continue;

            GameObject body = CharacterBodyFactory.Ready(c);
            if (body == null)
            {
                // 디스크에 받아 둔 GLB가 있으면 세워 달라고 한 번만 부탁한다(크레딧 안 씀). 다 서면 다음 확인 때 나온다.
                // 아직 몸이 없는 캐릭터는 공용 몸으로 내보내지 않는다 — 모두 같은 마네킹이면 누가 누군지 알 수 없다.
                if (requested.Add(c.Id)) GameServices.Bodies.RebuildIfDownloaded(c);
                continue;
            }

            VillageResident resident = Spawn(c, body);
            if (resident != null) residents[c.Id] = resident;
        }
    }

    private VillageResident Spawn(CharacterSO character, GameObject body)
    {
        int node = roads.RandomNode();
        Vector3 position = roads.Node(node);
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject copy = Instantiate(body, staging);
        copy.name = character.characterName;
        StripCombatParts(copy);

        var resident = copy.AddComponent<VillageResident>();
        copy.transform.SetPositionAndRotation(position, rotation);
        copy.transform.SetParent(transform, true);   // 여기서 처음 깨어난다(Awake)
        resident.Init(character, this, roads, ground, residentScale);
        return resident;
    }

    // 깨어나기 전에(꺼진 부모 아래에서) 지운다. Destroy는 프레임 끝에 지우므로 그 전에 깨어나는 UnitController.Awake를 막지 못한다.
    // UnitController가 TargetScanner를 요구(RequireComponent)하므로 순서를 지킨다.
    private static void StripCombatParts(GameObject body)
    {
        DestroyAll<UnitController>(body);
        DestroyAll<TargetScanner>(body);
        DestroyAll<UnitEmotion>(body);
        DestroyAll<NavMeshAgent>(body);
        DestroyAll<Rigidbody>(body);
        // 시설을 누르는 광선(PhysicsRaycaster)을 주민 몸이 가로막지 않게 한다.
        DestroyAll<Collider>(body);
    }

    private static void DestroyAll<T>(GameObject body) where T : Component
    {
        foreach (T component in body.GetComponentsInChildren<T>(true)) DestroyImmediate(component);
    }

    // ------------------------------------------------------------------
    // 훈련소
    // ------------------------------------------------------------------

    // 산책 중인 주민을 훈련소로 부른다. 근접 둘이 한가하고 대련 자리가 비었으면 대련부터, 아니면 빈 허수아비로.
    private void CallToTraining()
    {
        if (!ground.Available) return;

        idle.Clear();
        foreach (VillageResident r in residents.Values)
            if (r != null && r.IsWandering) idle.Add(r);
        OrderByNearness(idle, ground.Entrance);

        int room = Mathf.FloorToInt(residents.Count * maxTrainingShare) - (residents.Count - idle.Count);
        if (room <= 0) return;

        VillageTrainingGround.Ring ring = ground.FreeRing();
        if (room >= 2 && ring != null && Random.value < trainingCallChance * 1.5f)
        {
            VillageResident first = TakeIdle(melee: true);
            VillageResident second = first != null ? TakeIdle(melee: true) : null;
            if (second != null)
            {
                ring.first = first;
                ring.second = second;
                float duration = Random.Range(trainingDuration.x, trainingDuration.y);
                first.GoSpar(ring, 0, second, duration, true);
                second.GoSpar(ring, 1, first, duration, false);
                room -= 2;
            }
            else if (first != null) idle.Add(first);
        }

        foreach (VillageResident r in idle)
        {
            if (room <= 0) break;
            if (Random.value >= trainingCallChance) continue;
            room--;
            VillageTrainingGround.Dummy dummy = ground.FreeDummy();
            if (dummy == null) break;
            dummy.occupant = r;
            r.GoDrill(dummy, Random.Range(trainingDuration.x, trainingDuration.y));
        }
    }

    private VillageResident TakeIdle(bool melee)
    {
        for (int i = 0; i < idle.Count; i++)
        {
            if (idle[i].Ranged == melee) continue;
            VillageResident r = idle[i];
            idle.RemoveAt(i);
            return r;
        }
        return null;
    }

    public void ReleaseTraining(VillageResident resident) => ground.Release(resident);

    // 훈련소 가까이 있는 사람부터 부른다. 북쪽 끝에서 불려 가면 마을을 가로질러 2분을 걷는다.
    // 거리만 보면 늘 남쪽 사람만 훈련하므로 60m까지는 흔들어 섞는다.
    private static void OrderByNearness(List<VillageResident> list, Vector3 point)
    {
        var keys = new Dictionary<VillageResident, float>();
        foreach (VillageResident r in list)
            keys[r] = Vector3.Distance(r.transform.position, point) + Random.Range(0f, 60f);
        list.Sort((a, b) => keys[a].CompareTo(keys[b]));
    }
}
