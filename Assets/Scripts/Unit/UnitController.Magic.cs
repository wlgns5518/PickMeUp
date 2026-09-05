using System.Collections.Generic;
using UnityEngine;

// UnitController의 마법 쪽.
//
// 스킬(SkillBehavior)과 완전히 분리해 둔 것이 이 파일의 요점이다. 원작에서 마법은 버튼 하나로
// 나가는 정해진 액티브가 아니라, 마법사가 마력을 연산하고 영창해 현상으로 구현하는 것이다.
// 그래서 skillStateName을 채우는 방식을 쓰지 않는다 — 그건 "정해진 액티브 하나"를 뜻한다.
//
// 대신 이렇게 굴러간다:
//   1) 무엇을 쓸지 연산한다          SelectSpell()  — 자기 속성 안에서만 고른다
//   2) 마력을 모은다(무방비)          CastBehavior + BeginSpellCast()
//   3) 현상으로 구현한다              ExecuteSpell()
//   4) 끊기면 마력만 날아간다          CancelSpellCast()
//
// 단일 속성 귀속이 이 구조의 핵심 제약이다. 마법사는 평생 하나의 속성만 다루므로
// SelectSpell은 "어느 속성이 잘 먹히나"를 묻지 않는다. 물을 수 있는 것은
// "내 속성 안에서 지금 제어인가 광역인가" 뿐이고, 그래서 어느 속성의 마법사를 데려갔는지가
// 파티 운영을 바꾼다 — 빙결 마법사만 있는 파티는 적을 묶을 수는 있어도 태울 수 없다.
//
// 단일/기본(Bolt)은 여기 없다. 그건 영창이 없는 즉발이고, 마법사의 평타가 이미 그것이다
// (맨손 시전 + 투사체 — WeaponType.Magic / Casting.asset).
public partial class UnitController
{
    [Header("Magic")]
    [Tooltip("영창 모션 상태 이름. 사제의 치유 시전과 같은 클립을 빌려 쓴다. " +
             "비어 있거나 애니메이터에 없으면 대기 자세로 대체된다 — 마법 자체는 그대로 나간다.")]
    [SerializeField] private string castStateName = "Cast";

    [Tooltip("광역 마법이 노리는 자리를 고를 때, 표적 후보로 훑을 최대 인원. " +
             "난전에서 전 적을 다 훑지 않도록 상한을 둔다.")]
    [SerializeField, Min(1)] private int spellAimSampleLimit = 12;

    [Tooltip("영창이 끊긴 뒤 다시 마력을 모으기까지의 시간(초). 0이면 끊기자마자 다시 시작한다.\n\n" +
             "이 값이 없으면 붙잡힌 마법사가 시작과 중단을 초당 서너 번 반복하고, " +
             "영창 모션이 그때마다 처음부터 되감겨 짧은 평타를 내지르는 것처럼 보인다.")]
    [SerializeField, Min(0f)] private float spellRetryDelay = 0.8f;

    // 흩어진 영창을 다시 모을 수 있게 되는 시각.
    private float spellRetryTime;

    private int castAnimationHash;

    // 마법마다 따로 식는다. 큰 것을 썼다고 잔 마법까지 잠기면 "큰 것을 기다리며 작은 것으로
    // 버틴다"가 성립하지 않는다. 속성이 정해지면 마법 수도 정해지므로(SpellCatalog) 그때 잡는다.
    private float[] spellReadyTime;

    private void EnsureSpellCooldowns()
    {
        int count = SpellCatalog.CountOf(stats.affinity);
        if (spellReadyTime != null && spellReadyTime.Length == count) return;

        spellReadyTime = count > 0 ? new float[count] : null;
    }

    // 지금 영창 중인 마법과 그 착탄 지점. 영창을 시작할 때 잠근다 —
    // 사제의 castHealTarget과 같은 이유다(영창 중에 판단이 다시 돌아 대상이 지워지면 안 된다).
    private SpellSpec castingSpell;
    private Vector3 castingAimPoint;
    private bool hasCastingSpell;

    // 광역 판정에 쓰는 공용 버퍼. 마법 한 번에 리스트를 새로 만들지 않는다.
    private static readonly List<UnitController> SpellVictims = new List<UnitController>(16);

    public bool HasCastAnimation => castAnimationHash != 0;

    // 지금 영창 중인 마법의 이름. 로그와 UI 표시용.
    public string CastingSpellName => hasCastingSpell ? castingSpell.Name : "";

    private void CacheMagicAnimationHashes()
    {
        castAnimationHash = ResolveStateHash(castStateName);
    }

    // ---------------------------------------------------------------- 연산

    // 지금 쓸 마법을 고른다. 없으면 Exists가 false인 값이 돌아온다.
    //
    // 고르는 순서가 곧 우선순위다: 판을 끝낼 수 있으면 끝내고(광역), 아니면 판을 만들고(제어),
    // 그것도 아니면 기본 마법을 쏜다(단일). 마지막 갈래가 반드시 있어야 한다 —
    // 마법사에게는 평타가 없으므로, 아무 마법도 고르지 못하면 그 유닛은 아무것도 못 한다.
    public bool SelectSpell(out SpellSpec spell, out Vector3 aimPoint)
    {
        spell = default;
        aimPoint = Vector3.zero;

        if (stats.affinity == MagicAffinity.None) return false;
        if (!IsTargetValid()) return false;
        // 마법은 겨눌 수 있어야 쏜다. 사거리 밖이면 먼저 붙어야 한다.
        if (!IsTargetInAttackRange()) return false;

        EnsureSpellCooldowns();
        SpellSpec[] spells = SpellCatalog.SpellsOf(stats.affinity);
        if (spells.Length == 0) return false;

        // 적이 품 안에 들어와 있으면 긴 영창은 시작하지 않는다.
        //
        // 영창은 한 대만 맞으면 통째로 흩어진다. 실측에서 붙잡힌 화염 마법사가 유성 낙하(2.6초)를
        // 연달아 시작했다가 번번이 끊겨 시간만 버렸다. 그렇다고 아무것도 못 하게 두면 마법사는
        // 붙잡힌 순간 무력해진다 — 평타가 없기 때문이다. 그래서 긴 것만 접고 잔 마법은 계속 쏜다.
        bool pressured = ShouldKeepDistance();

        // 위에서부터 훑어 지금 쓸 수 있는 첫 번째를 고른다. 표의 순서가 곧 우선순위이므로
        // (SpellCatalog 주석) 파괴력이 큰 것부터 검토하고, 마지막의 기본 마법이 언제나 받쳐 준다.
        for (int i = 0; i < spells.Length; i++)
        {
            SpellSpec candidate = spells[i];
            if (pressured && candidate.Role != SpellRole.Bolt) continue;
            if (!IsSpellReady(candidate)) continue;
            if (!TryFindAimPoint(candidate, out aimPoint)) continue;

            spell = candidate;
            return true;
        }

        return false;
    }

    private bool IsSpellReady(in SpellSpec spell)
    {
        if (spellReadyTime != null && spell.Index < spellReadyTime.Length &&
            Time.time < spellReadyTime[spell.Index])
        {
            return false;
        }

        return stats.HasMana(spell.ManaCost);
    }

    // 마법이 떨어질 자리를 고른다.
    //
    // 노리던 상대의 발밑이 기본이지만, 그 자리가 최선이라는 보장은 없다. 적이 몰려 있는 쪽에
    // 떨어뜨리는 것이 광역 마법의 값어치이므로, 사거리 안의 적들을 후보로 두고 각자의 자리에
    // 떨어뜨렸을 때 몇을 덮는지 세어 가장 많이 덮는 자리를 고른다.
    //
    // 후보를 적의 위치로만 잡는 것은 근사다. 정확한 최적해(원 덮기)를 구하려면 비용이 크고,
    // 실제 난전에서는 "가장 뭉친 놈 발밑"이 거의 언제나 답이다.
    private bool TryFindAimPoint(in SpellSpec spell, out Vector3 aimPoint)
    {
        aimPoint = CurrentTarget.transform.position;

        // 반경이 없는 마법은 노리는 상대에게 그대로 간다.
        if (spell.Radius <= 0.01f) return spell.MinTargets <= 1;

        int bestCount = UnitRegistry.CountEnemiesAround(this, aimPoint, spell.Radius);

        UnitRegistry.FindEnemiesInRange(this, stats.attackRange + stats.moveStopDistance, SpellVictims);
        int sampled = 0;
        for (int i = 0; i < SpellVictims.Count && sampled < spellAimSampleLimit; i++, sampled++)
        {
            UnitController candidate = SpellVictims[i];
            if (candidate == null) continue;

            Vector3 point = candidate.transform.position;
            int count = UnitRegistry.CountEnemiesAround(this, point, spell.Radius);
            if (count <= bestCount) continue;

            bestCount = count;
            aimPoint = point;
        }

        SpellVictims.Clear();

        // 광역기가 하나 잡자고 나가면 마력만 버린다. 값어치가 설 때만 쓴다.
        return bestCount >= spell.MinTargets;
    }

    // ---------------------------------------------------------------- 영창

    // 지금 영창을 시작할 수 있는가. CastBehavior 가지로 들어가는 조건이다.
    // 마법 연산을 다시 돌릴 시각. 쿨다운이 돌아왔는데 쓸 자리가 아직 안 나온 구간에서는
    // 이 판단이 매 프레임 불린다 — 그때마다 적을 훑어 착탄 지점을 다시 계산하면
    // 마법사 한 명이 난전에서 프레임당 수백 번의 거리 검사를 돌린다. 초당 몇 번이면 충분하다.
    private float nextSpellEvaluationTime;
    private const float SpellEvaluationInterval = 0.25f;

    public bool CanCastSpell()
    {
        if (IsDead || stats.affinity == MagicAffinity.None) return false;
        if (IsCasting) return false;
        // 휘두르는 중에는 영창을 시작하지 않는다. 이미 나간 마력탄이 있다.
        if (IsAttackAnimationLocked) return false;
        if (IsStaggered) return false;

        // 영창이 흩어진 직후에는 곧바로 다시 모으지 못한다.
        //
        // 이게 없으면 붙잡힌 마법사가 "시작 → 맞아서 중단 → 즉시 시작 → 다시 중단"을 초당 서너 번
        // 반복한다. 실측 로그가 통째로 그 왕복이었고(완주는 넷 중 하나), 영창 모션이 0.3초마다
        // 처음부터 되감기니 화면에서는 짧은 평타를 계속 내지르는 것처럼 보였다.
        // 흩어진 마력을 다시 모으는 데는 시간이 걸린다 — 한 박자 쉬어야 동작도 판단도 성립한다.
        if (Time.time < spellRetryTime) return false;

        if (Time.time < nextSpellEvaluationTime) return false;

        nextSpellEvaluationTime = Time.time + SpellEvaluationInterval;
        return SelectSpell(out _, out _);
    }

    // 영창 시작. 마력은 여기서 나가고, 끊기면 돌려받지 못한다.
    // 사제의 BeginHealCast와 같은 규칙이다 — 영창의 대가는 시간이 아니라 마력이다.
    public void BeginSpellCast(in SpellSpec spell, Vector3 aimPoint)
    {
        castingSpell = spell;
        castingAimPoint = aimPoint;
        hasCastingSpell = true;

        // 마력도 쿨다운도 여기서는 건드리지 않는다.
        //
        // 영창이 끊기면 아무것도 소모하지 않는다 — 모으던 마력은 아직 현상이 되지 않았으므로
        // 흩어질 뿐 쓰인 것이 아니다. 그래서 대가는 오직 시간이다: 그 자리에 무방비로 서 있던
        // 시간과, 다시 모으기까지의 한 박자(spellRetryDelay).
        // 실제 소모와 쿨다운은 마법이 현상으로 구현되는 순간에 일어난다(ExecuteSpell).

        // 물러난 뒤 한 수는 냈다는 표시.
        //
        // 이 플래그는 원거리 유닛이 Attack↔Evade만 오가는 것을 막으려고 TriggerAttack이 세우는
        // 값인데, 마법사는 평타가 없어 그 자리를 영영 지나지 않는다. 그래서 첫 회피 이후로는
        // 계속 거짓이었고, 공격 중의 거리 벌리기 조건이 다시는 성립하지 않았다 —
        // 실측에서 마법사가 1.8m(유지 거리 2.6m 안쪽)에 붙잡힌 채 빠져나오지 못했다.
        // 마법사에게는 영창이 곧 "한 수 냈다"이므로 여기서 세운다.
        MarkAttackedSinceEvade();

        BeginCast();
        TriggerCastAnimation();

        if (debugLogs) Debug.Log($"[UnitController] {name} 영창 시작 — {SpellCatalog.Korean(stats.affinity)} {spell.Name} ({spell.CastTime:0.0}초, 마력 {spell.ManaCost})");
    }

    // 이번 영창이 실제로 걸리는 시간. 성장(castSpeedMultiplier)이 여기에 곱해진다.
    public float CurrentCastDuration =>
        hasCastingSpell ? castingSpell.CastTime * Mathf.Max(0.1f, stats.castSpeedMultiplier) : 0f;

    private void TriggerCastAnimation()
    {
        // 전용 영창 모션이 없으면 대기 자세로 선다. 서서 마력을 모으는 그림으로 읽히므로
        // 아무것도 안 하는 것보다 낫고, 마법 자체는 그대로 나간다.
        PlayAnimation(castAnimationHash != 0 ? castAnimationHash : idleAnimationHash, true);
    }

    // 영창을 끝까지 마쳤다. 여기서 마력이 현상이 된다.
    public void ExecuteSpell()
    {
        EndCast();

        if (!hasCastingSpell) return;

        SpellSpec spell = castingSpell;
        Vector3 aimPoint = castingAimPoint;
        hasCastingSpell = false;

        if (IsDead) return;

        // 마력은 여기서 나간다 — 마법이 실제로 현상이 되는 순간이다.
        // 영창 도중에 끊겼다면 이 자리에 오지 않으므로 아무것도 소모되지 않는다.
        stats.SpendMana(spell.ManaCost);

        EnsureSpellCooldowns();
        if (spellReadyTime != null && spell.Index < spellReadyTime.Length)
        {
            spellReadyTime[spell.Index] = Time.time + spell.Cooldown;
        }

        int damage = ScaleDamage(Mathf.RoundToInt(stats.attackDamage * spell.DamageMultiplier));
        int hitCount = 0;

        if (spell.Radius <= 0.01f)
        {
            // 반경이 없는 마법은 겨눈 하나에게만 간다.
            if (IsTargetValid())
            {
                // 손을 떠나는 것이 보여야 한다. 마법사가 든 것은 무기가 아니라 맨손이지만
                // (Casting.asset — 모델 없음, 투사체만 있음) 탄환은 활의 화살과 같은 길을 간다.
                //
                // 마법사에게 평타가 없어진 뒤로 이 호출이 중요해졌다 — 예전에는 공격 애니메이션의
                // 타격 이벤트가 탄환을 쐈는데, 이제 그 이벤트 자체가 오지 않으므로
                // 영창을 마친 이 자리에서 직접 쏘지 않으면 아무것도 날아가지 않는다.
                bool fired = TryFireProjectile(CurrentTarget, damage, spell.PoiseDamage, true);
                if (!fired) CurrentTarget.TakeDamage(damage, this, true, true, spell.PoiseDamage);

                // 둔화는 탄환에 실어 보낼 수 없어(WeaponProjectile은 피해만 옮긴다) 여기서 건다.
                // 탄환이 닿기 직전에 걸리는 셈이지만 사거리 7.5m를 0.2초에 지나가므로 눈에 띄지 않는다.
                if (spell.SlowDuration > 0f && spell.SlowMultiplier < 1f)
                {
                    CurrentTarget.ApplySlow(spell.SlowDuration, spell.SlowMultiplier);
                }

                hitCount = 1;
            }
        }
        else
        {
            UnitRegistry.FindEnemiesAround(this, aimPoint, spell.Radius, SpellVictims);
            hitCount = SpellVictims.Count;
            for (int i = 0; i < SpellVictims.Count; i++) ApplySpellTo(SpellVictims[i], spell, damage);
            SpellVictims.Clear();
        }

        if (debugLogs) Debug.Log($"[UnitController] {name} {spell.Name} 발동 — {hitCount}명 적중 (피해 {damage})");
    }

    private void ApplySpellTo(UnitController victim, in SpellSpec spell, int damage)
    {
        if (victim == null || victim.IsDead) return;

        // 마법은 밀쳐낸다. fromSkill로 넘겨 평타와 다른 취급을 받게 한다(출혈 판정 등).
        victim.TakeDamage(damage, this, true, true, spell.PoiseDamage);

        // 속성이 남기는 것. 빙결은 묶고, 화염은 밀어내며 태우고, 전격은 무너뜨린다
        // (전격의 몫은 위 PoiseDamage가 이미 크게 잡혀 있다).
        if (spell.SlowDuration > 0f && spell.SlowMultiplier < 1f)
        {
            victim.ApplySlow(spell.SlowDuration, spell.SlowMultiplier);
        }
    }

    // 영창이 끊겼다. 마력은 이미 나갔다 — 그것이 무방비로 서 있던 대가다.
    public void CancelSpellCast()
    {
        if (!hasCastingSpell && !IsCasting) return;

        string lost = hasCastingSpell ? castingSpell.Name : "";
        hasCastingSpell = false;
        EndCast();
        spellRetryTime = Time.time + spellRetryDelay;

        if (debugLogs) Debug.Log($"[UnitController] {name} 영창 중단 — {lost}{SubjectParticle(lost)} 흩어졌다 (마력 소모 없음)");
    }

    // 앞 글자의 받침 유무로 은/는·이/가를 고른다. "유성 낙하이(가)" 같은 표기를 없애기 위한 것.
    // 로그에만 쓰이지만, 읽는 사람이 매번 걸려 넘어지는 문장은 고쳐 두는 편이 낫다.
    private static string SubjectParticle(string word)
    {
        if (string.IsNullOrEmpty(word)) return "가";

        char last = word[word.Length - 1];
        // 한글 음절 영역 밖(영문·숫자)이면 판단할 근거가 없다. 가장 무난한 쪽으로 둔다.
        if (last < 0xAC00 || last > 0xD7A3) return "가";

        // 한글 음절은 (초성 x 21 + 중성) x 28 + 종성 으로 배열돼 있다. 나머지가 0이면 받침이 없다.
        return (last - 0xAC00) % 28 == 0 ? "가" : "이";
    }

    // 죽은 유닛을 재사용하는 경로(Configure)를 위한 초기화.
    private void ResetMagicRuntime()
    {
        hasCastingSpell = false;
        if (spellReadyTime != null)
        {
            for (int i = 0; i < spellReadyTime.Length; i++) spellReadyTime[i] = 0f;
        }
        nextSpellEvaluationTime = 0f;
        spellRetryTime = 0f;
    }
}
