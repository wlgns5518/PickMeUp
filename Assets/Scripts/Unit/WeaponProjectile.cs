using System.Collections.Generic;
using UnityEngine;

// 무기가 쏘아 보낸 것 한 발 — 활의 화살이든 지팡이의 마법 탄환이든 같은 물건이다.
//
// 이 게임의 타격은 히트스캔이다. 애니메이션 이벤트가 오는 순간 거리와 각도를 다시 재서
// 맞았는지 정하고 피해를 그 자리에서 넣는다(UnitController.ApplyAttackDamage).
// 원거리 무기도 "맞았는가"는 똑같이 그 순간에 정한다. 다만 피해는 투사체가 도착해야 들어간다 —
// 9m 밖의 적이 화살보다 0.2초 먼저 비틀거리면 화살은 뒤따라가는 장식이 되기 때문이다.
//
// 그래서 이 클래스가 옮기는 것은 "이미 확정된 한 방"이지 새로운 판정이 아니다.
// 날아가는 동안 다른 적이 앞을 가로질러도 그 적이 대신 맞지는 않는다 —
// 활은 겨눈 하나를 쏘는 것이지 앞을 쓸어 베는 것이 아니다(ResolveSwingVictim 주석 참조).
public class WeaponProjectile : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("초속(m/s). 활 사거리 9m를 0.2초 안팎에 지나가는 값. 너무 느리면 다음 화살이 앞 화살을 앞지른다.")]
    [SerializeField] private float speed = 45f;
    [Tooltip("표적 중심에서 이 거리 안에 들어오면 꽂힌 것으로 보고 피해를 넣는다.")]
    [SerializeField] private float hitDistance = 0.35f;
    [Tooltip("빗나간 화살이 회수되기까지. 표적 없이 떠난 화살은 그대로 날아가다 사라진다.")]
    [SerializeField] private float maxLifetime = 2f;

    private UnitController attacker;
    private TargetRef victim;
    private int damage;
    private float poiseDamage;
    // 스킬로 나간 투사체인가. 평타와 달리 밀쳐내고, 피격 쪽도 스킬로 취급한다.
    private bool fromSkill;
    private Vector3 direction;
    private float expireTime;

    // 꼬리 자국. 화살은 사거리 9m를 0.3초에 지나가서 화면에 실제로 있는 시간이 20프레임 남짓이다 —
    // 몸통만으로는 눈에 걸리지 않으므로 지나간 자리가 대신 읽히게 한다.
    private TrailRenderer trail;

    // 투사체는 공격 한 번에 하나씩 나간다. 매번 Instantiate/Destroy 하면 교전이 몰릴 때
    // 그 빈도만큼 힙이 늘었다 줄었다 한다(BloodEffectPool이 코루틴을 걷어낸 것과 같은 이유).
    private static readonly Dictionary<GameObject, List<WeaponProjectile>> pools = new Dictionary<GameObject, List<WeaponProjectile>>();
    private static Transform holder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPools()
    {
        // 도메인 리로드를 끈 에디터에서 지난 플레이의 죽은 참조가 남지 않도록 비운다.
        pools.Clear();
        holder = null;
    }

    /// 화살을 쏜다. victim이 null이면 맞힐 상대 없이 허공으로 날아간다(빗나간 스윙).
    public static void Fire(GameObject prefab, Vector3 origin, Vector3 aimDirection,
                            UnitController attacker, TargetRef victim, int damage, float poiseDamage, bool fromSkill)
    {
        if (prefab == null) return;

        WeaponProjectile arrow = Take(prefab);
        if (arrow == null) return;

        arrow.attacker = attacker;
        arrow.victim = victim;
        arrow.damage = damage;
        arrow.poiseDamage = poiseDamage;
        arrow.fromSkill = fromSkill;
        arrow.direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.forward;
        arrow.expireTime = Time.time + arrow.maxLifetime;

        arrow.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(arrow.direction));
        arrow.gameObject.SetActive(true);

        // 풀에서 꺼낸 것은 지난번에 날아간 자리가 자국에 남아 있다. 지우지 않으면
        // 앞 화살이 떨어진 곳에서 새 화살까지 맵을 가로지르는 선이 한 줄 그어진다.
        arrow.ResetTrail();
    }

    private void Awake()
    {
        trail = GetComponent<TrailRenderer>();
    }

    private void Update()
    {
        // 쏜 사람이 그 사이 쓰러져도 화살은 날아간다. 이미 시위를 떠났다.
        if (Time.time >= expireTime)
        {
            Release();
            return;
        }

        // 표적이 화살보다 먼저 쓰러지면 실려 있던 한 방은 사라진다.
        // 시체에 꽂아도 의미가 없고, 그 자리에 온 다른 적이 대신 맞는 것도 활의 규칙이 아니다.
        if (victim.Exists && !victim.IsAlive) victim = TargetRef.None;

        float step = speed * Time.deltaTime;

        if (victim.Exists)
        {
            // 표적은 계속 움직인다. 명중은 이미 정해진 사실이므로 화살이 따라간다 —
            // 여기서 빗나가게 두면 스윙 판정과 실제 결과가 어긋나 "맞았는데 안 맞았다"가 된다.
            Vector3 toVictim = victim.AimPoint - transform.position;
            float distance = toVictim.magnitude;

            if (distance <= Mathf.Max(hitDistance, step))
            {
                transform.position = victim.AimPoint;
                Hit();
                return;
            }

            direction = toVictim / distance;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        transform.position += direction * step;
    }

    private void Hit()
    {
        if (victim.IsAlive)
            victim.TakeDamage(damage, attacker, fromSkill, fromSkill, poiseDamage);

        Release();
    }

    private void ResetTrail()
    {
        if (trail != null) trail.Clear();
    }

    private void Release()
    {
        // 참조를 붙들고 있으면 회수된 화살이 죽은 유닛을 계속 살려 둔다.
        victim = TargetRef.None;
        attacker = null;
        gameObject.SetActive(false);
    }

    private static WeaponProjectile Take(GameObject prefab)
    {
        // 씬이 바뀌면 홀더와 그 아래 화살이 통째로 파괴된다. 죽은 참조가 쌓이지 않도록 함께 비운다.
        if (holder == null)
        {
            pools.Clear();
            holder = new GameObject("Projectiles").transform;
        }

        List<WeaponProjectile> pool;
        if (!pools.TryGetValue(prefab, out pool))
        {
            pool = new List<WeaponProjectile>();
            pools[prefab] = pool;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            WeaponProjectile candidate = pool[i];
            if (candidate != null && !candidate.gameObject.activeSelf) return candidate;
        }

        GameObject instance = Instantiate(prefab, holder);
        WeaponProjectile arrow = instance.GetComponent<WeaponProjectile>();
        // 프리팹에 컴포넌트를 안 붙여 뒀어도 날아가긴 해야 한다. 붙여 두는 쪽이 정상이다.
        if (arrow == null) arrow = instance.AddComponent<WeaponProjectile>();

        // 손에 든 무기와 달리 화살은 아무것도 밀어서는 안 된다. 명중은 거리로만 판정한다.
        foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        instance.SetActive(false);
        pool.Add(arrow);
        return arrow;
    }
}
