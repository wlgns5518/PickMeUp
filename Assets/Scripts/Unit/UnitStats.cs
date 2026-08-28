using System;
using UnityEngine;

[Serializable]
public class UnitStats
{
    [Header("Health")]
    public int maxHp = 100;
    public int currentHp = 100;

    [Header("Mana")]
    // 전투 중에는 회복되지 않는 고정 자원. 전투를 시작할 때 가득 채워지고 그걸로 끝이라,
    // 한 판에 쓸 수 있는 스킬 횟수가 maxMana / skillManaCost로 정해진다.
    [Tooltip("스킬 자원. 전투 중 회복되지 않는다. 0이면 스킬을 아예 쓸 수 없으니 주의.")]
    public int maxMana = 50;
    public int currentMana = 50;
    // 지금 유닛이 쓰는 동작(무기 콤보, 발차기)은 전부 기본공격이라 마나를 쓰지 않는다.
    // 마나를 소비하는 진짜 스킬이 생기면 그때 이 값을 올리면 되고, 소모 로직 자체는 그대로 살아 있다.
    // 아군 스탯은 CharacterBattleSpawner.MapStats가 새 UnitStats를 만들어 쓰므로
    // 프리팹이 아니라 여기 기본값이 실제로 적용되는 값이다.
    [Tooltip("스킬 1회 소모량. 쿨다운과 별개로 이 값이 모자라면 그 전투에서는 더 쓸 수 없다. " +
             "0이면 소모 없이 쿨다운만으로 쓴다(현재 모든 공격이 기본공격이라 0).")]
    public int skillManaCost;

    [Header("Potion")]
    // HP도 마나도 저절로 차지 않는다. 전투 중 회복 수단은 회복약뿐이다.
    // 기본값은 0 — 회복약은 공짜로 주어지지 않는다. 인벤토리나 인스펙터에서 명시적으로 넣어야 한다.
    [Tooltip("전투 시작 시 들고 있는 회복약 개수. 전투 중에는 보충되지 않는다.")]
    [Min(0)] public int potionCount;
    [Tooltip("회복약 1개가 회복시키는 최대 HP 비율.")]
    [Range(0f, 1f)] public float potionHealHpRatio = 0.4f;
    [Tooltip("회복약 1개가 회복시키는 최대 마나 비율.")]
    [Range(0f, 1f)] public float potionHealManaRatio = 0.5f;
    [Tooltip("HP가 이 비율 아래로 떨어지면 회복약을 마신다.")]
    [Range(0f, 1f)] public float potionHpThreshold = 0.35f;
    [Tooltip("마나가 스킬 1회분에 못 미칠 때, HP가 이 비율 아래면 회복약을 마신다. 회복량을 통째로 버리지 않기 위한 조건.")]
    [Range(0f, 1f)] public float potionManaTriggerHpRatio = 0.8f;
    [Tooltip("회복약을 연달아 들이켜지 않도록 하는 간격.")]
    public float potionCooldown = 6f;

    [Header("Movement")]
    public float walkSpeed = 2f;
    // 달리기 클립(PlayerRun)이 실제로 표현하는 속도가 4.345m/s다(클립 4.197 x humanScale 1.0352).
    // 이 값이 그보다 낮으면 재생 배속이 1배 아래로 내려가 질주 모션이 슬로모션으로 돈다 —
    // 직업 배율이 가장 낮은 탱커(0.85)까지 1배 근처에 닿도록 잡은 값이다.
    public float runSpeed = 4.5f;
    public float jumpPower = 5f;
    public float moveStopDistance = 0.25f;

    [Header("Detection")]
    public float attackRange = 1.6f;
    public float detectRange = 8f;

    [Header("Role")]
    // 직업이 정하는 전술 성향. CharacterBattleSpawner.MapStats가 JobProfile에서 옮겨 담는다.
    // 프리팹에 직접 값을 넣는 유닛(고블린)은 전부 기본값이라 예전과 똑같이 행동한다.
    [Tooltip("전장에서 이 유닛이 서는 자리와 하는 일. None이면 성향 없이 눈앞의 적을 친다.")]
    public JobRole role = JobRole.None;
    [Tooltip("날아오는 공격을 받아내는 방식. None이면 아예 막지 않는다 — " +
             "원거리 직군과 암살자는 맞기 전에 빠지는 쪽이다.")]
    public GuardStyle guardStyle = GuardStyle.None;

    [Tooltip("탱커 직업이거나 방패를 든 유닛. 스폰 시점의 적 배분(CharacterBattleSpawner)과 " +
             "공격을 물리고 방패를 드는 판단(AttackState)에 쓴다. 어그로 자체는 threatWeight가 맡는다.")]
    public bool isTank;

    [Tooltip("적이 나를 얼마나 우선해서 노리는가(어그로). 1이 기준이고 탱커가 3.2, 사제가 0.3이다. " +
             "0이면 아무 편향 없이 거리만으로 노려진다.")]
    [Min(0f)] public float threatWeight = 1f;

    [Tooltip("적 진영의 후방(궁수/마법사/사제)을 얼마나 우선해서 찾아 들어가는가(미터 단위 편향). " +
             "암살자만 크다. 0이면 가장 가까운 적을 친다.")]
    [Min(0f)] public float backlinePreference;

    [Tooltip("나보다 위협 가중치가 낮은 아군을 물고 있는 적을 얼마나 우선해서 노리는가(도발, 미터 단위 편향). " +
             "탱커만 크다. 이 값이 어그로를 수동적인 것에서 능동적인 것으로 바꾼다 — " +
             "탱커가 그쪽으로 걸어가 한 대 치면 위협 비교가 적을 탱커에게 넘긴다.")]
    [Min(0f)] public float peelBonus;

    [Tooltip("교전 중 서고 싶은 방위(도). 적의 정면이 0, 측면이 90, 등 뒤가 180이다. " +
             "좌우 중 어느 쪽인지는 유닛마다 스폰 시 따로 정해진다.")]
    [Range(0f, 180f)] public float engageAngle;

    [Tooltip("적이 이 거리(미터) 안으로 들어오면 물러선다. 0이면 붙어서 싸운다. " +
             "원거리 직군만의 것이 아니다 — 창수는 근접이면서도 이 값을 갖는다(리치 우위 유지).")]
    [Min(0f)] public float keepDistanceRange;

    [Tooltip("시야각(도). 0이면 TargetScanner의 기본값을 쓴다. 정찰을 겸하는 궁수만 넓다.")]
    [Range(0f, 360f)] public float viewAngle;

    [Header("Magic")]
    [Tooltip("마법사가 평생 귀속되는 속성. None이면 마법을 쓰지 않는다(마법사가 아닌 전 직군). " +
             "이 값 하나가 그 유닛이 쓸 수 있는 마법 전부를 정한다 — 속성을 바꿔 쓰는 일은 없다.")]
    public MagicAffinity affinity = MagicAffinity.None;
    [Tooltip("영창 시간에 곱해지는 값. 1보다 작으면 더 빨리 영창한다(원작 이설의 '짧은 영창' 성장). " +
             "무방비 시간이 그만큼 줄어들므로 마법사의 생존과 직결된다.")]
    [Range(0.3f, 2f)] public float castSpeedMultiplier = 1f;

    [Header("Damage")]
    public int attackDamage = 8;
    public int skillDamage = 24;
    [Tooltip("발차기 피해 배율. 무기로 베는 것보다 약하다 — 발차기의 값어치는 피해가 아니라 " +
             "가드를 여는 강인도 피해(poiseDamageKick)에 있다.")]
    [Range(0.1f, 2f)] public float kickDamageMultiplier = 0.6f;

    [Header("Debuff On Hit")]
    // 직군이 "때린다"는 같은 동작으로 서로 다른 일을 하게 만드는 자리.
    // 암살자는 급소를 그어 피를 내고, 창수는 다리를 찔러 발을 묶는다.
    [Tooltip("평타가 출혈을 남길 확률. 등 뒤를 잡으면 두 배가 된다(급소 타격). " +
             "출혈 자체의 지속시간·피해량은 맞는 쪽의 EmotionProfile이 정한다.")]
    [Range(0f, 1f)] public float bleedChanceOnHit;
    [Tooltip("평타가 상대의 발을 묶는 시간(초). 0이면 둔화를 걸지 않는다(부위 억제).")]
    [Min(0f)] public float slowOnHitDuration;
    [Tooltip("둔화가 걸린 동안의 이동 속도 배율. 1이면 사실상 둔화가 없다.")]
    [Range(0.1f, 1f)] public float slowOnHitMultiplier = 1f;

    [Header("Cooldowns")]
    public float skillCooldown = 5.0f;

    // 남은 스킬 사용 횟수. 쿨다운과는 성격이 다르다 — 쿨다운은 "얼마나 자주"를,
    // 이쪽은 "이번 전투에 몇 번이나"를 정한다.
    //
    // 유닛은 전투가 시작될 때 새로 스폰되고 스탯도 그때 프리팹에서 복제되므로(CharacterBattleSpawner),
    // 여기 적어 둔 값이 곧 "스테이지 한 판당 몇 번"이 된다. 전투 중에는 보충되지 않는다 —
    // 회복약(potionCount)과 같은 방식이다.
    [Tooltip("전투 한 판(= 스테이지 하나) 동안 스킬을 쓸 수 있는 횟수. " +
             "음수면 제한 없이 쿨다운만으로 조절한다(기본값).")]
    public int skillUseCount = -1;

    [Header("Defense")]
    public float evadeRange = 2.5f;
    public float blockDuration = 0.8f;
    public float blockCooldown = 1.5f;
    public float knockbackDistance = 0.6f;
    public float knockbackDuration = 0.12f;
    [Tooltip("막아냈을 때 깎이는 피해 비율. 1이면 통째로 흘려내 피해가 0이 된다. " +
             "완전 무효라도 교착에 빠지지 않는 이유는 막은 타격도 강인도(maxPoise)는 그대로 깎기 때문이다 — " +
             "계속 막다 보면 강인도가 바닥나 가드가 뚫리고, 그때 크게 무너진다.")]
    [Range(0f, 1f)] public float blockDamageReduction = 1f;
    [Tooltip("상시 피해 경감. 방어 자세와 별개로 항상 적용된다. 탱커와 방패가 올려준다.")]
    [Range(0f, 0.9f)] public float damageReduction;
    [Tooltip("방어 자세로 받아낼 수 있는 좌우 각도(전체 폭). 이 밖에서 들어온 공격은 자세를 잡고 있어도 그대로 맞는다. " +
             "방패는 정면 반구를 통째로(180) 가리지만, 검신으로 쳐내는 패링은 훨씬 좁다(110). " +
             "그래서 검사는 여럿에게 둘러싸이면 방패병처럼 버티지 못한다.")]
    [Range(30f, 360f)] public float guardArcAngle = 180f;
    [Tooltip("흘려내기(퍼펙트 가드)에 성공한 직후 곧바로 반격에 들어갈 수 있는가. " +
             "검사의 패링만 참이다 — 쳐낸 그 자리에서 되받아치는 것이 패링의 값어치다.")]
    public bool counterAfterPerfectGuard;

    // 강인도는 "얼마나 버티는가"를 재는 유일한 자원이다. 예전에는 방어 지구력(Block Stamina)이
    // 따로 있어서 막는 쪽만 별도로 닳았는데, 두 자원이 같은 일(계속 버티지 못하게 하기)을
    // 나눠 맡고 있었을 뿐이라 하나로 합쳤다. 막은 타격도 강인도는 그대로 깎으므로,
    // "몇 번은 막아내지만 계속 막을 수는 없다"는 성질은 그대로 남는다.
    [Header("Poise")]
    [Tooltip("강인도 최대치. 이게 0이 되면 진짜 경직(Hit)에 걸린다. 그 전까지는 맞아도 애니메이션이 끊기지 않는다. " +
             "막고 있다가 이게 깨지면 가드가 뚫린 것으로 보고 더 크게 무너진다(Stagger).")]
    public float maxPoise = 100f;
    public float currentPoise = 100f;
    [Tooltip("일반 공격 한 대가 깎는 강인도.")]
    public float poiseDamagePerHit = 15f;
    [Tooltip("콤보 마지막 타격에 추가로 깎이는 강인도.")]
    public float poiseDamageComboFinisherBonus = 25f;
    [Tooltip("스킬이 깎는 강인도. 그 자체로 거의 항상 강인도를 깬다.")]
    public float poiseDamageSkill = 60f;
    [Tooltip("발차기가 깎는 강인도. 발차기는 베는 수단이 아니라 가드를 여는 수단이라 " +
             "평타보다 크게 잡는다. 무기 스윙은 막히면 강인도만 깎고 끝나지만 발차기는 그 강인도를 " +
             "무너뜨려 다음 스윙이 들어갈 자리를 만든다.")]
    public float poiseDamageKick = 45f;
    [Tooltip("강인도가 깨진 뒤 다시 깨지지 않는 면역 시간(초). 무한 경직을 막는다.")]
    public float poiseBreakImmunity = 2.5f;
    [Tooltip("이 유닛의 스킬에 맞은 상대가 자세를 잃는 시간(초). 0이면 스킬은 여느 공격처럼 " +
             "강인도만 깎는다.\n\n" +
             "붙잡아 무너뜨리는 한 수에만 준다 — 고블린이 물어뜯는 동안 물린 쪽은 뿌리치지 " +
             "못하고 끌려다녀야 한다. 강인도를 깎아 깨지기를 기다리는 것과는 다르다. " +
             "다만 무한 경직을 막는 규칙(위 면역 시간)은 그대로 지킨다.")]
    public float skillStaggerDuration;
    [Tooltip("공격 거리 안에 붙어 있은 시간이 이만큼 넘어야 스킬을 쓸 수 있다(초). 0이면 붙자마자 쓸 수 있다.\n\n" +
             "전투 시작으로부터 재지 않는다는 것이 중요하다. 그렇게 재면 멀리서 달려오는 " +
             "동안에도 시간이 흘러, 정작 도착한 순간에 곧바로 물어뜯는다. 이 값은 '얼마나 " +
             "오래 붙어서 겨뤘는가'를 재므로, 달려들자마자가 아니라 한 합 주고받은 뒤에 큰 수가 나온다.\n\n" +
             "사거리 밖으로 떨어지면 0으로 돌아간다 — 붙었다 떨어졌다를 반복하는 것으로는 채워지지 않는다.")]
    public float skillEngageDelay;
    [Tooltip("이 스킬에 당한 상대가 같은 스킬에 다시 당하지 않는 시간(초). 0이면 제한 없다.\n\n" +
             "붙잡는 스킬에 반드시 필요하다 — 없으면 고블린 다섯이 같은 아군 하나의 목에 " +
             "동시에 매달린다. 상대를 '점유하는' 스킬은 한 번에 하나만 걸려야 한다.")]
    public float skillVictimImmunity;
    [Tooltip("강인도가 안 깨졌을 때 즉시 밀려나는 거리(미터). 경직 없이 타격감만 준다.")]
    public float poiseHitPushback = 0.15f;

    [Header("Melee Hit Validation")]
    // 예전에는 애니메이션 이벤트가 뜨면 타깃이 어디에 있든 무조건 맞았다. 스윙 도중 상대가
    // 등 뒤로 돌아가거나 사거리 밖으로 빠져도 그대로 피해가 들어가서, 거리와 각도가 전투에
    // 아무 의미가 없었다. 아래 값들이 "칼이 실제로 닿는 범위"를 정한다.
    [Tooltip("스윙이 닿는 좌우 각도(전체 폭). 130이면 정면 ±65도 안에 있어야 맞는다.")]
    [Range(30f, 360f)] public float attackArcAngle = 130f;
    [Tooltip("타격 시점에 사거리에 더해 주는 여유(미터). 서로 조금씩 움직이는 중이라 딱 맞게 재면 " +
             "정상적인 교전에서도 헛스윙이 지나치게 잦아진다.")]
    public float attackHitTolerance = 0.4f;
    [Tooltip("타깃이 스윙에서 빠져나갔을 때 궤적 안에 있는 다른 적을 대신 벤다. " +
             "휘두른 칼이 앞에 선 놈을 베는 건 당연한 일이라 기본으로 켜 둔다.")]
    public bool cleaveOffTarget = true;

    [Header("Attack Rhythm")]
    // 둘 다 0 = 쉼 없이 싸운다. 클립이 끝나는 프레임에 곧바로 다음 스윙이 나간다.
    //
    // 한때 여기에 호흡을 넣어 봤다(평타 사이 0.22초, 콤보 완주 뒤 0.85초). 재고 파고드는
    // 그림은 나왔지만, 그 틈마다 유닛이 제자리에 서기 때문에 공격이 끝날 때마다 멈칫하는
    // 것처럼 보였다. 지금은 멈추지 않고 계속 몰아치는 쪽을 택했다.
    //
    // 되돌리고 싶으면 이 값만 올리면 된다 — 관련 동작(발놀림, 헛스윙 벌칙)은 전부 이 틈에
    // 얹혀 있어서 자동으로 같이 살아난다. 다만 그 대가로 위의 멈칫이 함께 돌아온다.
    [Tooltip("스윙 하나가 끝난 뒤 다음 스윙까지의 간격(초). 0이면 쉼 없이 이어 휘두른다.")]
    public float attackRecoveryTime;
    [Tooltip("위 간격에 곱해지는 무작위 폭. 0.4면 0.6~1.4배 사이에서 흔들린다 — 유닛끼리 " +
             "타이밍이 딱 맞아떨어져 합창하듯 휘두르는 것을 막는다. 간격이 0이면 의미 없다.")]
    [Range(0f, 0.9f)] public float attackRecoveryRandomness = 0.4f;
    [Tooltip("콤보 마지막 타격 뒤의 간격(초). 0이면 콤보를 완주해도 쉬지 않고 다시 1단부터 이어간다.")]
    public float comboFinisherRecoveryTime;

    [Header("Lunge")]
    [Tooltip("공격 준비 동작 중 타깃 쪽으로 파고드는 속도(m/s). 0이면 제자리에서 휘두른다.")]
    public float lungeSpeed = 2.4f;
    [Tooltip("스윙 한 번에 파고들 수 있는 최대 거리(미터).")]
    public float lungeMaxDistance = 0.8f;

    [Header("Leap Attack")]
    // 파고들기(위)와는 다른 동작이다. 파고들기는 이미 사거리 근처에서 스윙과 함께 반 발
    // 들어가는 것이고, 이쪽은 아직 닿지 않는 거리에서 아예 몸을 던져 붙는 것이다.
    //
    // 짐승처럼 싸우는 적에게만 준다. 사람 직군은 사거리까지 걸어 들어가 자세를 잡고 겨루는
    // 쪽이라(ChaseState의 방위 잡기 참조) 덤벼드는 그림이 맞지 않는다 — 기본값 0으로 꺼 둔다.
    [Tooltip("이 거리 안에 들어오면 도약해 덤벼든다(미터). 사거리보다 커야 의미가 있다. " +
             "0이면 도약하지 않는다.")]
    public float leapAttackRange;
    [Tooltip("도약 공격의 재사용 대기(초). 매번 덤비면 접근이라는 것이 없어진다.")]
    public float leapAttackCooldown = 7f;

    [Header("Footwork")]
    // 사거리에 들어가면 그 자리에 못 박혀 마주 보고 때리기만 했다. 다음 스윙을 기다리는
    // 동안 간격을 재고 옆으로 도는 것이 실제 칼싸움의 모습이다.
    [Tooltip("교전 중 유지하려는 간격 = 사거리 x 이 비율. 이보다 붙으면 물러서고 멀면 파고든다.")]
    [Range(0.3f, 1f)] public float combatSpacingRatio = 0.82f;
    [Tooltip("발놀림 이동 속도 = 걷기 속도 x 이 비율.")]
    [Range(0f, 1.5f)] public float footworkSpeedRatio = 0.65f;
    [Tooltip("옆으로 도는 방향을 뒤집는 주기(초). 유닛마다 이 값 근처에서 무작위로 흔들린다. " +
             "0이면 옆으로 돌지 않고 간격만 맞춘다.")]
    public float strafeFlipInterval = 1.8f;

    [Header("Directional Damage")]
    [Tooltip("등 뒤에서 들어온 공격의 피해 배율. 뒤를 잡히면 아프다 — 아군끼리 서로 등을 " +
             "지켜야 하는 이유가 여기서 생긴다.")]
    public float backstabDamageMultiplier = 1.6f;
    [Tooltip("등 뒤 공격이 강인도를 깎는 배율. 피해보다 경직 쪽이 더 크게 벌어져야 " +
             "'뒤를 잡히면 무너진다'가 읽힌다.")]
    public float backstabPoiseMultiplier = 2f;
    [Tooltip("공격을 내지른 직후(회수 동작 중)에 맞으면 늘어나는 피해 배율. 선공을 " +
             "헛치거나 흘려보내면 그만큼 벌을 받는다.")]
    public float recoveryVulnerabilityMultiplier = 1.35f;
    [Tooltip("영창(치유·스킬 시전) 중에 맞으면 늘어나는 피해 배율. 마법사 2.2, 사제 1.8. " +
             "원작의 '시전 시간 동안 완전히 무방비'가 이 숫자 하나로 성립한다 — " +
             "그래서 후방 시전자는 탱커가 만들어 준 시간 안에서만 영창을 끝낼 수 있다.")]
    public float castVulnerabilityMultiplier = 1f;

    [Header("Reaction")]
    // 예전에는 적이 칼을 들어올린 그 프레임에 곧바로 방패가 올라갔다. 반응이라기보다 예지에
    // 가까웠고, 그래서 방어가 늘 여유롭게 성공했다(퍼펙트 가드가 성립할 여지도 없었다).
    [Tooltip("적의 준비 동작을 알아채고 실제로 방패를 올리기까지 걸리는 시간(초). " +
             "유닛마다 이 값의 0.6~1.4배 사이에서 무작위로 정해진다.")]
    public float blockReactionTime = 0.18f;

    [Header("Perfect Guard")]
    // 방패를 미리 올려 둔 것과 날아오는 칼에 맞춰 올린 것은 달라야 한다.
    [Tooltip("방어 자세를 잡은 직후 이 시간(초) 안에 들어온 공격은 완전히 흘려낼 수 있다 — " +
             "피해도 강인도 소모도 없고 공격자가 무너진다. 0이면 퍼펙트 가드를 끈다.")]
    public float perfectGuardWindow = 0.2f;
    // 창 안에 들어왔다고 무조건 흘러가면 안 된다. 반응 시간(blockReactionTime)이 준비 동작
    // 길이와 비슷하기 때문에, 타이밍만 조건으로 두면 방패를 올리는 거의 모든 경우가
    // 퍼펙트 가드가 되어 버린다(칼 기준 대략 4번 중 3번). 그래서 "제대로 읽었는가"를 한 번 더 본다.
    [Tooltip("방패를 올릴 때 굴리는 판정. 성공한 자세만 창 안의 공격을 흘려내고, 실패하면 " +
             "평범하게 막는다. 1이면 타이밍만 맞으면 항상 흘려낸다.")]
    [Range(0f, 1f)] public float perfectGuardChance = 0.35f;
    [Tooltip("퍼펙트 가드에 흘려진 공격자가 무너져 있는 시간(초).")]
    public float perfectGuardStaggerDuration = 0.9f;

    [Header("Stagger")]
    [Tooltip("가드 브레이크나 퍼펙트 가드로 자세가 무너졌을 때 아무것도 못 하는 시간(초).")]
    public float staggerDuration = 1.2f;
    [Tooltip("무너져 있는 동안 받는 피해 배율.")]
    public float staggerDamageMultiplier = 1.4f;

    [Header("Impact")]
    [Tooltip("타격이 들어간 순간 공격자와 피격자의 애니메이션을 눌러 붙이는 시간(초). " +
             "0이면 히트스톱을 끈다. 무거운 무기일수록 길게.")]
    public float hitStopDuration = 0.06f;
    [Tooltip("히트스톱 동안의 애니메이션 재생 배속. 0에 가까울수록 완전히 멈춘다.")]
    [Range(0f, 1f)] public float hitStopScale = 0.12f;

    [Header("Retreat")]
    [Tooltip("HP가 이 비율 이하로 떨어지면 거리를 벌리려 한다. 회복 수단이 없는 적도 이걸로 무작정 맞아 죽지 않는다.")]
    [Range(0f, 1f)] public float retreatHpThreshold = 0.25f;

    [Header("Heal (Support)")]
    [Tooltip("서포터 직업만 켜진다. 부상당한 아군을 회복시킬 수 있는지 여부.")]
    public bool canHealAllies;
    public int healAmount = 25;
    public float healRange = 8f;
    public float healCooldown = 6f;
    public int healManaCost = 15;
    [Tooltip("HP가 이 비율 이하인 아군을 치료 대상으로 본다.")]
    [Range(0f, 1f)] public float healTargetHpRatio = 0.7f;
    [Tooltip("치유가 끝날 때 대상의 출혈과 공포를 함께 걷어낸다(디스펠). " +
             "원작에서 상태이상 해제는 치유만큼이나 사제의 일이다 — 제때 못 풀면 전열이 통째로 무력화된다.")]
    public bool dispelOnHeal = true;
    [Tooltip("상태이상에 걸린 아군을 치료 대상으로 고를 때 HP 비율에서 빼 주는 값. " +
             "덜 다쳤어도 출혈 중이면 먼저 손이 가게 만든다. 0이면 순수하게 HP만 본다.")]
    [Range(0f, 0.5f)] public float dispelPriorityBonus = 0.2f;

    public bool IsDead => currentHp <= 0;
    public bool HasPotion => potionCount > 0;
    // 음수 = 제한 없음, 0 = 이번 전투에는 다 썼다.
    public bool HasSkillUse => skillUseCount != 0;

    // 한 번 썼다고 표시한다. 제한이 없는 유닛(음수)은 줄어들 것이 없으므로 그대로 둔다.
    public bool ConsumeSkillUse()
    {
        if (skillUseCount == 0) return false;
        if (skillUseCount > 0) skillUseCount--;
        return true;
    }

    // 최대치를 넘지 않게 회복하고 실제로 회복된 양을 돌려준다.
    public int Heal(int amount)
    {
        if (amount <= 0 || IsDead) return 0;

        int before = currentHp;
        currentHp = Mathf.Min(maxHp, currentHp + amount);
        return currentHp - before;
    }
    public float HpRatio => currentHp / Mathf.Max(1f, maxHp);

    // 프리팹의 stats는 모든 인스턴스가 공유하는 하나의 객체다.
    // 층별로 값을 키우려면 반드시 복사본을 만들어 써야 원본이 오염되지 않는다.
    public UnitStats Clone()
    {
        return (UnitStats)MemberwiseClone();
    }

    public void ResetHp()
    {
        currentHp = Mathf.Max(1, maxHp);
    }

    // 전투 시작 시점에만 채워진다.
    public void ResetMana()
    {
        currentMana = Mathf.Max(0, maxMana);
    }

    public void ResetPoise()
    {
        currentPoise = Mathf.Max(0f, maxPoise);
    }

    public bool HasMana(int cost) => currentMana >= cost;

    public void SpendMana(int cost)
    {
        currentMana = Mathf.Clamp(currentMana - Mathf.Max(0, cost), 0, maxMana);
    }

    // 회복약 1개를 소모하고 실제로 회복된 양을 돌려준다.
    // 최대치를 넘는 분은 그냥 버려진다 - 가득 찬 상태에서 마시면 낭비라는 뜻이라,
    // 언제 마실지 판단하는 쪽(UnitController.CanUsePotion)이 그걸 피하도록 되어 있다.
    public bool ConsumePotion(out int healedHp, out int healedMana)
    {
        healedHp = 0;
        healedMana = 0;
        if (potionCount <= 0 || IsDead) return false;

        potionCount--;

        int hpBefore = currentHp;
        currentHp = Mathf.Min(maxHp, currentHp + Mathf.RoundToInt(maxHp * potionHealHpRatio));
        healedHp = currentHp - hpBefore;

        int manaBefore = currentMana;
        currentMana = Mathf.Min(maxMana, currentMana + Mathf.RoundToInt(maxMana * potionHealManaRatio));
        healedMana = currentMana - manaBefore;
        return true;
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, false);
    }

    public void TakeDamage(int damage, bool isBlocking)
    {
        if (IsDead) return;

        int finalDamage = Mathf.Max(0, damage);
        if (isBlocking)
        {
            finalDamage = Mathf.RoundToInt(finalDamage * (1f - blockDamageReduction));
        }

        if (damageReduction > 0f)
        {
            finalDamage = Mathf.RoundToInt(finalDamage * (1f - damageReduction));
        }

        // 경감이 겹쳐 0이 되면 단단한 유닛끼리 만났을 때 전투가 영원히 끝나지 않는다.
        // 유효타는 최소 1은 들어가게 한다.
        //
        // 막아낸 타격은 여기서 빠진다. 이 하한이 막으려던 것은 "상시 경감(damageReduction)만으로
        // 무적이 되는 것"인데, 방어는 성격이 다르다 — 막아도 강인도(poiseDamagePerHit)는 그대로
        // 깎이므로 계속 이어갈 수 없다. 몇 번 막다 보면 강인도가 바닥나 가드가 뚫리고,
        // 그러면 몇 초를 통째로 무너진 채(Stagger) 서 있게 된다.
        // 즉 완전히 흘려내도 교착이 아니라 "강인도를 쓰고 시간을 번다"가 된다.
        if (damage > 0 && !isBlocking) finalDamage = Mathf.Max(1, finalDamage);

        currentHp = Mathf.Max(0, currentHp - finalDamage);
    }
}
