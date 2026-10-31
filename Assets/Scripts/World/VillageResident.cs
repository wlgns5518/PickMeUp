using System.Collections.Generic;
using UnityEngine;

// 마을을 돌아다니는 로스터 캐릭터 한 명. 전투용 몸(CharacterBodyFactory)을 빌려 입되 전투 부품은 걷어 낸 채로 선다(VillageResidents).
//
// 하는 일은 셋이다.
//  - 산책: 길 그래프(VillageRoadGraph)를 따라 아무 데나 걸어가서 잠깐 서 있다가 또 걷는다. 길의 오른쪽으로 비켜 걷는다 —
//    마주 오는 주민과 몸이 겹치지 않고 엇갈려 지나간다.
//  - 허수아비 치기: 훈련소 허수아비 앞에서 무기 콤보를 몇 번 휘두르고 숨을 고르기를 되풀이한다.
//  - 대련: 둘이 마주 서서 한쪽이 몇 합 휘두르는 동안 다른 쪽이 막고, 번갈아 공수를 바꾼다.
// 무기는 훈련소 마당 안에서만 든다. 마당에 들어서는 순간 들고, 훈련을 마치고 마당을 벗어나는 순간 내려놓는다.
//
// 무엇을 할지는 이 주민이 아니라 VillageResidents가 정한다(빈 허수아비, 대련 짝). 여기서는 받은 일을 몸으로 옮긴다.
// 애니메이션은 전투와 같은 Animator 상태 이름을 그대로 부른다(Walk·Idle·CombatIdle·AttackN·Block·BlockHit·Cast).
public class VillageResident : MonoBehaviour
{
    private enum Mode { Wander, Drill, Spar }

    private const string WalkState = "Walk";
    private const string IdleState = "Idle";
    private const string CombatIdleState = "CombatIdle";
    private const string AttackStatePrefix = "Attack";
    private const string CastState = "Cast";
    private const string BlockState = "Block";
    private const string BlockHitState = "BlockHit";

    // 걷기 클립이 원래 나아가는 속도(UnitController.walkClipSpeed와 같은 값). 실제 속도를 이걸로 나눠 배속을 정한다.
    private const float WalkClipSpeed = 1.99f;
    private static readonly Vector2 MoveSpeedRange = new Vector2(0.6f, 2.2f);
    private const float TurnSpeed = 360f;
    private const float FadeTime = 0.15f;
    // 클립이 끝나지 않는 상태(루프 공격, 길이를 못 읽은 상태)에 갇히지 않게 한 번 휘두르기에 주는 상한(초).
    private const float SwingTimeout = 4f;

    public CharacterSO Character { get; private set; }
    public bool Ranged { get; private set; }
    public bool IsWandering => mode == Mode.Wander;

    private VillageResidents owner;
    private VillageRoadGraph roads;
    private VillageTrainingGround ground;

    private Animator animator;
    private WeaponEquipper equipper;
    private UnitAnimationEvents animationEvents;
    private MoveAnimationSpeed moveSpeed;

    private Mode mode = Mode.Wander;
    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex = -1;
    private float walkSpeed;
    private float scale = 1f;
    private float laneOffset;
    private float idleUntil;
    private Quaternion? faceAt;
    private int currentState;
    private bool armed;

    // 훈련
    private float trainUntil;
    private float trainDuration;
    private Vector3 trainSpot;
    private Quaternion trainFacing;
    private bool arrived;
    private VillageResident partner;
    private bool attacking;          // 대련에서 지금 공격하는 쪽인가
    private int swingHash;
    private float swingStarted = -1f;
    private int swingsLeft;
    private int comboStep;
    private float restUntil;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        equipper = GetComponent<WeaponEquipper>();
        animationEvents = animator != null ? animator.GetComponent<UnitAnimationEvents>() : null;
        if (animator != null && animationEvents == null) animationEvents = animator.gameObject.AddComponent<UnitAnimationEvents>();
    }

    private void OnEnable() { if (animationEvents != null) animationEvents.AttackHit += OnAttackHit; }
    private void OnDisable() { if (animationEvents != null) animationEvents.AttackHit -= OnAttackHit; }

    public void Init(CharacterSO character, VillageResidents residents, VillageRoadGraph graph, VillageTrainingGround training, float bodyScale)
    {
        Character = character;
        owner = residents;
        roads = graph;
        ground = training;

        // 활과 마법은 마주 서서 칼을 섞을 수 없다. 허수아비만 쏜다.
        WeaponType weapon = CharacterLoadout.MainHandTypeOf(character);
        Ranged = character.job == JobType.Archer || character.job == JobType.Mage ||
                 weapon == WeaponType.Bow || weapon == WeaponType.Magic;

        // 몸을 키우면 보폭도 그만큼 커진다. 걷는 속도를 같이 키워야 걷기 클립 배속(실제 속도 ÷ 클립 속도 × 배율)이 그대로라 발이 미끄러지지 않는다.
        bodyScale = Mathf.Max(0.1f, bodyScale);
        transform.localScale *= bodyScale;
        scale = bodyScale;

        // 사람마다 걸음이 조금씩 다르다. 오른쪽으로 비켜 걷는 폭도 — 같은 길을 같은 쪽으로 가도 한 줄로 겹치지 않는다.
        // 비켜 걷는 폭은 몸만큼 키우지 않는다. 좁은 길(폭 4m)에서 길 밖으로 나간다.
        walkSpeed = Random.Range(1.25f, 1.65f) * bodyScale;
        laneOffset = Random.Range(0.6f, 1.6f) * Mathf.Min(bodyScale, 1.6f);

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            moveSpeed = new MoveAnimationSpeed(animator, "MoveSpeedMultiplier");
        }

        // 빈손으로 시작한다. WeaponEquipper.Start보다 먼저 불러야 몸의 기본 무기(낡은 철검)를 쥐지 않는다.
        if (equipper != null) equipper.Equip(null, null);

        idleUntil = Time.time + Random.Range(0f, 4f);
        Play(IdleState);
    }

    // ------------------------------------------------------------------
    // 일 받기 (VillageResidents가 부른다)
    // ------------------------------------------------------------------

    public void GoDrill(VillageTrainingGround.Dummy dummy, float duration)
    {
        trainSpot = ground.DummySpot(dummy, Ranged, out trainFacing);
        BeginTraining(Mode.Drill, duration, null, false, null);
    }

    public void GoSpar(VillageTrainingGround.Ring ring, int side, VillageResident other, float duration, bool attacksFirst)
    {
        trainSpot = ring.Spot(side);
        Vector3 across = ring.Spot(1 - side) - trainSpot;
        across.y = 0f;
        trainFacing = Quaternion.LookRotation(across);
        BeginTraining(Mode.Spar, duration, other, attacksFirst, ring.approach);
    }

    private void BeginTraining(Mode next, float duration, VillageResident other, bool attacksFirst, List<Vector3> approach)
    {
        mode = next;
        partner = other;
        attacking = attacksFirst;
        arrived = false;
        trainUntil = float.MaxValue; // 시간은 자리에 닿은 뒤부터 잰다
        trainDuration = duration;
        swingsLeft = 0;
        swingStarted = -1f;
        restUntil = 0f;

        // 마당 길목까지는 길로, 거기서 자리까지는 마당을 가로지른다(대련 자리는 허수아비 사이 틈을 지나서).
        var yard = new List<Vector3>();
        if (approach != null) yard.AddRange(approach);
        yard.Add(trainSpot);
        if (!PlanPath(roads.Nearest(ground.Entrance), yard)) StartWalking(yard);
    }

    // 훈련을 마치고 다시 산책하러 간다. 짝이 있으면 짝도 같이 끝낸다.
    public void EndTraining()
    {
        if (mode == Mode.Wander) return;

        VillageResident other = partner;
        mode = Mode.Wander;
        partner = null;
        swingStarted = -1f;
        owner.ReleaseTraining(this);
        if (equipper != null && equipper.BowGrip != null) equipper.ReleaseArrow();

        idleUntil = Time.time + Random.Range(0.5f, 2f);
        pathIndex = -1;
        Play(CombatIdleState);

        if (other != null && other.partner == this) other.EndTraining();
    }

    // ------------------------------------------------------------------
    // 매 프레임
    // ------------------------------------------------------------------

    private void Update()
    {
        if (animator == null) return;

        if (pathIndex >= 0) StepAlongPath();
        else
        {
            switch (mode)
            {
                case Mode.Wander: TickWander(); break;
                case Mode.Drill: TickDrill(); break;
                case Mode.Spar: TickSpar(); break;
            }
            TurnToFace();
        }

        UpdateArmed();
    }

    private void TickWander()
    {
        if (Time.time < idleUntil) return;

        if (Random.value < 0.25f && !armed)
        {
            // 걷기 전에 잠깐 두리번거린다.
            faceAt = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            idleUntil = Time.time + Random.Range(2f, 4f);
            Play(IdleState);
            return;
        }

        int destination = PickWanderDestination();
        if (destination < 0 || !PlanPath(destination, null))
        {
            idleUntil = Time.time + 3f;
            Play(IdleState);
        }
    }

    // 너무 가까우면 걷다 마는 것 같고, 너무 멀면 마을 끝에서 끝까지 2분을 걷는다. 적당한 거리를 몇 번 골라 본다.
    private int PickWanderDestination()
    {
        int fallback = -1;
        for (int i = 0; i < 8; i++)
        {
            int node = roads.RandomNode();
            if (node < 0) return -1;
            fallback = node;
            // 3배 몸은 세 배 빨리 걷는다. 가까운 곳만 고르면 몇 초 걷고 서기를 되풀이한다.
            float distance = Vector3.Distance(roads.Node(node), transform.position);
            if (distance > 12f * Mathf.Min(scale, 2f) && distance < 70f * Mathf.Max(1f, scale * 0.6f)) return node;
        }
        return fallback;
    }

    // ------------------------------------------------------------------
    // 걷기
    // ------------------------------------------------------------------

    // 지금 자리에서 가장 가까운 길 마디부터 target 마디까지 길을 따라가고, 길 밖 점(finalPoints)이 있으면 그 점들을 차례로 곧장 간다.
    private bool PlanPath(int targetNode, List<Vector3> finalPoints)
    {
        var nodes = new List<Vector3>();
        if (!roads.FindPath(roads.Nearest(transform.position), targetNode, nodes)) return false;

        // 길 가운데 선을 오른쪽으로 비켜 간다. 처음과 끝 마디는 그대로 둬야 길 끝(건물 입구, 마당 길목)에서 벗어나지 않는다.
        for (int i = 1; i < nodes.Count - 1; i++)
        {
            Vector3 along = nodes[i + 1] - nodes[i - 1];
            along.y = 0f;
            if (along.sqrMagnitude < 0.0001f) continue;
            along.Normalize();
            nodes[i] += new Vector3(along.z, 0f, -along.x) * laneOffset;
        }
        if (finalPoints != null) nodes.AddRange(finalPoints);

        StartWalking(nodes);
        return true;
    }

    private void StartWalking(List<Vector3> points)
    {
        path.Clear();
        path.AddRange(points);
        pathIndex = 0;
        faceAt = null;
    }

    private void StepAlongPath()
    {
        Vector3 position = transform.position;
        Vector3 target = path[pathIndex];
        Vector3 to = target - position;
        to.y = 0f;
        float distance = to.magnitude;
        float step = walkSpeed * Time.deltaTime;

        if (distance <= step)
        {
            position.x = target.x;
            position.z = target.z;
            if (++pathIndex >= path.Count) { pathIndex = -1; Arrive(); }
        }
        else
        {
            position += to / distance * step;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), TurnSpeed * Time.deltaTime);
        }

        // 길·마당 단의 높이 차이(0.4m 남짓)를 한 프레임에 뛰어오르지 않게 천천히 따라간다.
        float groundY = VillageRoadGraph.GroundAt(position).y;
        position.y = Mathf.MoveTowards(position.y, groundY, 2.5f * Time.deltaTime);
        transform.position = position;

        Play(WalkState);
        // 클립이 나아가는 거리도 몸 배율만큼 커진다. 그대로 나누면 3배 몸이 배속 상한(2.2)에 걸려 다리가 헛돈다.
        moveSpeed?.Apply(walkSpeed, WalkClipSpeed * scale, MoveSpeedRange, 0.15f);
    }

    private void Arrive()
    {
        if (mode == Mode.Wander)
        {
            idleUntil = Time.time + Random.Range(3f, 9f);
            Play(IdleState);
            return;
        }

        arrived = true;
        trainUntil = Time.time + trainDuration;
        faceAt = trainFacing;
        restUntil = Time.time + Random.Range(0.3f, 1.2f);
        Play(CombatIdleState);
    }

    private void TurnToFace()
    {
        if (!faceAt.HasValue) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, faceAt.Value, TurnSpeed * 0.5f * Time.deltaTime);
    }

    // ------------------------------------------------------------------
    // 훈련
    // ------------------------------------------------------------------

    private void TickDrill()
    {
        if (TickSwing() != SwingPhase.Swinging && Time.time >= trainUntil) EndTraining();
    }

    // 대련: 공격하는 쪽이 박자를 쥔다. 막는 쪽은 상대가 휘두를 때 막고(OnPartnerSwing), 칼이 닿으면 받아낸다(OnAttackHit).
    private void TickSpar()
    {
        if (partner == null) { EndTraining(); return; }
        if (!partner.arrived || partner.pathIndex >= 0)
        {
            // 짝이 아직 오는 중이다. 자세를 잡고 기다린다.
            Play(CombatIdleState);
            return;
        }
        if (!attacking) return;

        SwingPhase phase = TickSwing();
        if (phase == SwingPhase.Swinging) return;
        if (Time.time >= trainUntil) { EndTraining(); return; }

        // 한 콤보를 다 휘둘렀으면 공수를 바꾼다. 이쪽이 숨을 고르는 동안 상대는 막던 자세를 풀고, 그 뒤에 치고 들어온다.
        if (phase == SwingPhase.ComboEnded)
        {
            attacking = false;
            partner.attacking = true;
            partner.restUntil = restUntil + 0.3f;
            partner.Play(CombatIdleState);
        }
    }

    private enum SwingPhase { Swinging, Resting, ComboEnded }

    // 콤보 하나(2~4번 휘두르기)를 굴리고, 콤보 사이에는 숨을 고른다. 훈련 시간이 다 됐으면 새 콤보를 시작하지 않는다.
    private SwingPhase TickSwing()
    {
        if (swingStarted >= 0f)
        {
            if (!SwingFinished()) return SwingPhase.Swinging;
            swingStarted = -1f;
            if (--swingsLeft > 0) { StartSwing(); return SwingPhase.Swinging; }

            restUntil = Time.time + Random.Range(0.8f, 2.2f);
            Play(CombatIdleState);
            return SwingPhase.ComboEnded;
        }

        if (Time.time < restUntil || Time.time >= trainUntil) return SwingPhase.Resting;

        swingsLeft = Random.Range(2, 5);
        StartSwing();
        return SwingPhase.Swinging;
    }

    private void StartSwing()
    {
        string state = SwingState();
        swingHash = Animator.StringToHash(state);
        swingStarted = Time.time;
        Play(swingHash, 0.1f, true);

        if (equipper != null && equipper.BowGrip != null) equipper.BeginDraw();
        if (mode == Mode.Spar && partner != null) partner.OnPartnerSwing();
    }

    // 무기 컨트롤러가 가진 공격 단계를 차례로 돈다. 마법사는 손을 뻗어 영창한다.
    private string SwingState()
    {
        if (Character.job == JobType.Mage && HasState(CastState)) return CastState;

        int count = equipper != null && equipper.WeaponAttackCount > 0 ? equipper.WeaponAttackCount : 3;
        comboStep = comboStep % count + 1;
        string state = AttackStatePrefix + comboStep;
        return HasState(state) ? state : AttackStatePrefix + "1";
    }

    private bool SwingFinished()
    {
        if (Time.time - swingStarted > SwingTimeout) return true;
        if (animator.IsInTransition(0)) return false;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash == swingHash && info.normalizedTime >= 0.92f;
    }

    private void OnPartnerSwing()
    {
        if (mode != Mode.Spar || !arrived) return;
        Play(BlockState);
    }

    // 휘두른 칼이 닿는 프레임(공격 클립의 애니메이션 이벤트). 활은 여기서 시위를 놓고, 대련 상대는 받아낸다.
    private void OnAttackHit()
    {
        if (swingStarted < 0f) return;
        if (equipper != null && equipper.BowGrip != null) equipper.ReleaseArrow();
        if (mode == Mode.Spar && partner != null && partner.arrived && !partner.attacking)
            partner.Play(Animator.StringToHash(BlockHitState), 0.05f, true);
    }

    // ------------------------------------------------------------------
    // 무기
    // ------------------------------------------------------------------

    // 훈련하러 가는 길에 마당에 들어서면 들고, 훈련을 마치고 마당을 벗어나면 내려놓는다.
    private void UpdateArmed()
    {
        if (equipper == null) return;

        // 자리에 닿은 뒤에는 마당 밖이어도 든다 — 3배 몸의 원거리 자리는 바깥 허수아비(2레벨) 앞에서 마당 테두리를 넘는다.
        bool inYard = ground.Contains(transform.position);
        bool want = mode != Mode.Wander ? inYard || arrived : armed && inYard;
        if (want == armed) return;

        armed = want;
        if (armed) equipper.Equip(Character);
        else equipper.Equip(null, null);

        // 컨트롤러를 갈아 끼우면 Animator가 기본 상태로 돌아간다. 지금 하던 동작을 다시 건다.
        int state = currentState;
        currentState = 0;
        if (swingStarted >= 0f) swingStarted = -1f;
        Play(state, FadeTime, true);
    }

    // ------------------------------------------------------------------
    // 애니메이터
    // ------------------------------------------------------------------

    private void Play(string state) => Play(Animator.StringToHash(state), FadeTime, false);

    private void Play(int hash, float fade, bool restart)
    {
        if (hash == 0 || animator == null) return;
        if (!restart && hash == currentState) return;
        if (!animator.HasState(0, hash)) return;

        currentState = hash;
        animator.CrossFadeInFixedTime(hash, fade);
    }

    private bool HasState(string state) => animator.HasState(0, Animator.StringToHash(state));

    private void OnDestroy()
    {
        if (owner != null && mode != Mode.Wander) owner.ReleaseTraining(this);
    }
}
