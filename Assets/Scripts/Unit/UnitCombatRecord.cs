// 전투 한 판 동안 유닛 하나가 남긴 기여도 — MVP 선정과 경험치 정산이 이 값을 읽는다(BattleSettlement).
//
// 가한 피해는 방어로 감소된 뒤의 실제 감소량 기준이라, 방패에 막힌 공격은 기여로 잡히지 않는다.
// 예전에는 이 셈이 UnitController 안의 필드 넷과 메서드 넷으로 흩어져 있었다.
public sealed class UnitCombatRecord
{
    public int DamageDealt { get; private set; }
    public int DamageTaken { get; private set; }
    public int Kills { get; private set; }

    // 마지막으로 피해를 준 유닛. 처치를 이쪽에 귀속시킨다 — 출혈로 쓰러진 경우에도
    // 출혈을 걸어둔 공격자가 여기 남아 있어 기여가 사라지지 않는다.
    public UnitController LastAttacker { get; private set; }

    public void Reset()
    {
        DamageDealt = 0;
        DamageTaken = 0;
        Kills = 0;
        LastAttacker = null;
    }

    public void CreditDealt(int damage)
    {
        if (damage <= 0) return;
        DamageDealt += damage;
    }

    public void CreditKill() => Kills++;

    /// 때린 쪽을 모르는 피해(출혈). 받은 만큼만 센다.
    public void AddTaken(int amount) => DamageTaken += amount;

    /// 게임오브젝트끼리의 한 대. 맞은 쪽이 "실제로 깎인 만큼"을 때린 쪽의 기록에 얹는다.
    public void RecordHit(int dealt, UnitController attacker, UnitController self)
    {
        if (dealt <= 0) return;

        DamageTaken += dealt;
        if (attacker == null || attacker == self) return;

        LastAttacker = attacker;
        attacker.CombatRecord.CreditDealt(dealt);
    }
}
