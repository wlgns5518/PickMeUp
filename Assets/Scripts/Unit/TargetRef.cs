using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

// 겨누고 있는 상대 하나를 가리키는 손잡이.
//
// 아군은 게임오브젝트(UnitController)로 남고 적은 엔티티가 됐는데, 아군의 전투 판단은 둘을
// 구분할 이유가 없다 — "어디에 있고, 살아 있고, 얼마나 깎였는가"만 알면 된다. 그 질문들을
// 여기 한 곳에 모아 두고 안에서 갈라 준다.
//
// 예전에는 CurrentTarget이 그냥 UnitController였다. 그래서 적이 엔티티가 되는 순간 아군은
// 적을 아예 겨눌 수 없었다 — 담을 타입이 없었기 때문이다. 참조를 손잡이로 바꾼 것이
// 두 세계를 잇는 마지막 조각이다.
//
// 값 타입이고 필드가 둘뿐이라 들고 다니는 비용이 참조와 다르지 않다. 엔티티 쪽 값은
// 들고 있지 않고 매번 브리지의 이번 프레임 스냅샷에서 푼다(EnemyWorldBridge.TryGetEnemy) —
// 값을 복사해 두면 그 순간 낡고, 낡은 위치로 칼을 휘두르게 된다.
public readonly struct TargetRef : IEquatable<TargetRef>
{
    // 둘 중 하나만 채워진다. 둘 다 비어 있으면 "겨누는 것이 없다".
    public readonly UnitController Unit;
    public readonly Entity Entity;

    public static readonly TargetRef None = default;

    public TargetRef(UnitController unit)
    {
        Unit = unit;
        Entity = Entity.Null;
    }

    public TargetRef(Entity entity)
    {
        Unit = null;
        Entity = entity;
    }

    // 게임오브젝트 표적은 예전처럼 그냥 넘길 수 있게 해 둔다. 호출부 대부분이
    // UnitController를 그대로 들고 오는 자리라, 여기서 받아 주면 그 코드가 안 바뀐다.
    public static implicit operator TargetRef(UnitController unit) => new TargetRef(unit);

    public bool IsUnit => Unit != null;

    public bool IsEntity => Entity != Entity.Null;

    // 가리키는 것이 있는가. 살아 있는지는 별개 질문이다(IsAlive).
    public bool Exists => IsUnit || IsEntity;

    // 지금도 때릴 수 있는 상대인가.
    public bool IsAlive
    {
        get
        {
            if (IsUnit) return !Unit.IsDead && Unit.isActiveAndEnabled;
            if (IsEntity) return EnemyWorldBridge.IsEnemyAlive(Entity);
            return false;
        }
    }

    public Vector3 Position
    {
        get
        {
            if (IsUnit) return Unit.transform.position;
            if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state)) return state.position;
            return Vector3.zero;
        }
    }

    public Vector3 Forward
    {
        get
        {
            if (IsUnit) return Unit.transform.forward;
            if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state)) return state.forward;
            return Vector3.forward;
        }
    }

    // 예측 사격과 접근 지점 계산이 쓰는 속도. 엔티티는 스냅샷에 속도를 싣지 않았으므로
    // 0을 돌려준다 — 예측이 빠지면 조금 뒤를 겨눌 뿐이고, 그 값을 위해 스냅샷을 키우는 것이
    // 1000마리에서는 더 비싸다.
    public Vector3 Velocity
    {
        get
        {
            if (!IsUnit) return Vector3.zero;

            NavMeshAgent agent = Unit.Agent;
            return agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        }
    }

    // 화살과 마법이 겨누는 지점. 발밑이 아니라 몸통이어야 화살이 땅에 꽂히지 않는다.
    //
    // 엔티티는 콜라이더가 없으므로 고정 높이로 어림한다. 고블린 한 종류뿐인 지금은 그 값이
    // 곧 정확한 값이고, 덩치가 다른 적이 생기면 스냅샷에 실어 보내면 된다.
    private const float EntityChestHeight = 0.9f;

    public Vector3 AimPoint
    {
        get
        {
            if (IsUnit) return Unit.AimPoint;
            if (IsEntity) return Position + Vector3.up * EntityChestHeight;
            return Vector3.zero;
        }
    }

    public float HpRatio
    {
        get
        {
            if (IsUnit) return Unit.Stats != null ? Unit.Stats.HpRatio : 0f;
            if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state))
            {
                return state.maxHp > 0 ? Mathf.Clamp01(state.hp / (float)state.maxHp) : 0f;
            }

            return 0f;
        }
    }

    public float CurrentPoise
    {
        get
        {
            if (IsUnit) return Unit.Stats != null ? Unit.Stats.currentPoise : 0f;
            if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state)) return state.poise;
            return 0f;
        }
    }

    public float ThreatWeight
    {
        get
        {
            if (IsUnit) return Unit.Stats != null ? Unit.Stats.threatWeight : 1f;
            if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state)) return state.threatWeight;
            return 1f;
        }
    }

    // 지금 방패를 들고 있는가. 고블린은 막지 않으므로(guardStyle None) 엔티티는 늘 거짓이다.
    public bool IsBlocking => IsUnit && Unit.IsBlocking;

    public bool IsStaggered
    {
        get
        {
            if (IsUnit) return Unit.IsStaggered;
            if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state))
            {
                return state.action == EnemyActionKind.Stagger;
            }

            return false;
        }
    }

    // 붙잡는 스킬에 다시 당할 수 있는가. 엔티티에는 그 시계를 두지 않았으므로 늘 참이다 —
    // 이 규칙이 필요한 쪽은 같은 목에 여럿이 매달리는 고블린이고, 그건 적이 아군에게 거는
    // 방향이라 여기(아군이 적에게 거는 방향)에서는 성립할 일이 없다.
    public bool CanBeSkillVictim => !IsUnit || Unit.CanBeSkillVictim;

    public string DebugName
    {
        get
        {
            if (IsUnit) return Unit.name;
            if (IsEntity) return "Entity:" + Entity.Index;
            return "-";
        }
    }

    // ---------------------------------------------------------------- 이 상대에게 거는 것

    public int AttackersFrom(UnitTeam team)
    {
        if (IsUnit) return Unit.AttackersFrom(team);
        // 엔티티에 붙은 아군 수는 브리지가 지난 프레임에 집계해 둔다.
        if (IsEntity) return EnemyWorldBridge.AllyAttackersOn(Entity);
        return 0;
    }

    // 게임오브젝트 표적만 스스로 센다. 엔티티 쪽 집계는 브리지가 아군 표적을 훑어 만든다.
    public void AddAttacker(UnitTeam team, int delta)
    {
        if (IsUnit) Unit.AddAttacker(team, delta);
    }

    public void TakeDamage(int damage, UnitController attacker, bool applyKnockback, bool fromSkill, float poiseDamage)
    {
        if (IsUnit)
        {
            Unit.TakeDamage(damage, attacker, applyKnockback, fromSkill, poiseDamage);
            return;
        }

        if (!IsEntity) return;

        // 엔티티는 그 자리에서 건드리지 않는다. 시뮬레이션 도중에 구조를 바꾸면 돌고 있던
        // 잡이 전부 무효가 되므로, 큐에 넣고 ECS 쪽 시스템이 꺼내 적용한다.
        Vector3 from = attacker != null ? attacker.transform.position : Position;
        EnemyWorldBridge.DamageEnemy(Entity, damage, poiseDamage, from, attacker);

        // 기여도는 여기서 센다. 큐를 건너간 피해는 때린 쪽으로 돌아오지 않기 때문이다
        // (UnitController.CreditDamageDealt 주석 참조).
        if (attacker != null) attacker.CreditDamageDealt(damage);
    }

    public void MarkSkillVictim(float duration)
    {
        if (IsUnit) Unit.MarkSkillVictim(duration);
    }

    // 붙잡아 무너뜨리는 스킬이 부른다. 강인도와 무관하게 그 자리에서 자세를 무너뜨린다.
    public void TryForceStagger(float duration)
    {
        if (IsUnit)
        {
            Unit.TryForceStagger(duration);
            return;
        }

        if (IsEntity) EnemyWorldBridge.StaggerEnemy(Entity, duration, Position);
    }

    public void ApplySlow(float duration, float multiplier)
    {
        if (IsUnit) Unit.ApplySlow(duration, multiplier);
    }

    // 이 상대가 지금 겨누고 있는 쪽. 도주를 멈출지 정할 때 "그놈이 아직 나를 보는가"를 묻는다.
    public bool IsTargeting(UnitController ally)
    {
        if (IsUnit) return Unit.CurrentTarget.Unit == ally;
        if (IsEntity && EnemyWorldBridge.TryGetEnemy(Entity, out var state))
        {
            return state.targetAllyIndex >= 0 && EnemyWorldBridge.GetAlly(state.targetAllyIndex) == ally;
        }

        return false;
    }

    // ---------------------------------------------------------------- 같음 판정

    public bool Equals(TargetRef other) => Unit == other.Unit && Entity == other.Entity;

    public override bool Equals(object obj) => obj is TargetRef other && Equals(other);

    public override int GetHashCode()
    {
        return IsUnit ? Unit.GetHashCode() : Entity.GetHashCode();
    }

    public static bool operator ==(TargetRef a, TargetRef b) => a.Equals(b);

    public static bool operator !=(TargetRef a, TargetRef b) => !a.Equals(b);
}
