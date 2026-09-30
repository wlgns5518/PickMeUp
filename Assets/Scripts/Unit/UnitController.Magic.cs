using UnityEngine;

// UnitController의 마법 입구. 연산·영창·구현은 SpellcasterPart가 한다.
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

    private SpellcasterPart spellcasterPart;
    private SpellcasterPart Spellcaster => spellcasterPart ?? (spellcasterPart = new SpellcasterPart(this));

    private void CacheMagicAnimationHashes() => Spellcaster.CacheAnimations();

    // 지금 쓸 마법을 고른다. 없으면 false.
    public bool SelectSpell(out SpellSpec spell, out Vector3 aimPoint) => Spellcaster.Select(out spell, out aimPoint);

    public bool CanCastSpell() => Spellcaster.CanCast();

    public void BeginSpellCast(in SpellSpec spell, Vector3 aimPoint) => Spellcaster.BeginCast(spell, aimPoint);

    public float CurrentCastDuration => Spellcaster.CurrentCastDuration;

    public void ExecuteSpell() => Spellcaster.Execute();

    public void CancelSpellCast() => Spellcaster.Cancel();

    private void ResetMagicRuntime() => Spellcaster.Reset();
}
