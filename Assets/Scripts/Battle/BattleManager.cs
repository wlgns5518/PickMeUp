using System.Collections.Generic;
using UnityEngine;

// 전투에 시작과 끝을 붙여주는 컴포넌트.
// 지금까지는 스포너가 유닛을 뿌리면 끝이었고, 한쪽이 전멸해도 아무 일도 일어나지 않았다.
// 여기서 승패를 판정하고, 아군 사망은 PartyRoster에 영구 기록한다(원작의 영구 죽음).
//
// 시작·끝 알림은 BattleEvents로, 쓰는 쪽이 보는 모습은 IBattleSession(GameServices.Battle)으로 내보낸다.
// 셈법(기여도·MVP·경험치·보상)은 BattleSettlement에 있다.
[DisallowMultipleComponent]
public class BattleManager : MonoBehaviour, IBattleSession
{
    [Header("Start")]
    [Tooltip("스포너가 유닛을 다 뿌릴 때까지 기다리는 최대 시간. 이 안에 양 팀이 모두 등장하면 전투가 시작된다.")]
    [SerializeField] private float startTimeout = 5f;

    [Header("End")]
    [Tooltip("전멸 판정 후 결과를 알리기까지의 여유. 사망 애니메이션이 끝나기 전에 결과창이 뜨는 것을 막는다.")]
    [SerializeField] private float endDelay = 1.5f;

    [Header("Roster")]
    [Tooltip("저장 범위가 되는 보유 캐릭터 명단. 출전하지 않은 캐릭터의 진행도까지 함께 남긴다.")]
    [SerializeField] private CharacterRosterSO roster;

    [Header("Return")]
    [Tooltip("전투가 끝난 뒤 메인 씬으로 돌아가기까지의 시간. 결과창을 읽을 여유를 준다.")]
    [SerializeField] private float returnDelay = 5f;
    [Tooltip("돌아갈 메인 씬 이름. Build Settings에 등록돼 있어야 한다.")]
    [SerializeField] private string mainSceneName = "MainScene";

    [Header("Reward")]
    [SerializeField] private BattleRewardSettings rewardSettings = new BattleRewardSettings();

    private readonly BattleResult result = new BattleResult();

    // 전투 시작 시점의 아군 명단. UnitRegistry.Allies는 죽은 유닛이 빠지는 "지금 살아 있는" 목록이라,
    // 개수와 순서가 전투 내내 고정돼야 하는 쪽에서는 쓸 수 없다. 그래서 시작할 때 한 번 붙잡아 둔다.
    //
    // 이 명단을 쓰는 곳과 각자의 이유:
    //   BattleHud     — 파티 슬롯. 동료가 쓰러져도 슬롯이 밀리지 않고 그 자리에 전투 불능으로 남는다.
    //   PartyFollowCamera — 시작 시점에 볼 대상. 슬롯과 같은 순서라야 "맨 위 슬롯 = 시작 카메라"가 된다.
    //   CaptureStress — 쓰러진 동료의 스트레스도 전투 밖으로 들고 나가야 한다.
    //   SaveRoster    — 로스터 에셋이 없을 때의 저장 범위(참전자 전원).
    //   BuildRewards  — 쓰러진 동료의 기여도도 결과창은 보여준다.
    private readonly List<UnitController> allyRoster = new List<UnitController>();
    private bool started;
    private bool ended;
    private float elapsed;
    private float pendingEndTimer;
    private BattleOutcome pendingOutcome = BattleOutcome.InProgress;
    private readonly List<CharacterSO> rosterBuffer = new List<CharacterSO>();
    private bool returningToMain;
    private float returnTimer;

    public BattleResult Result => result;
    public IReadOnlyList<UnitController> AllyRoster => allyRoster;
    public bool IsRunning => started && !ended;

    // 자리에 앉는 것을 Awake가 아니라 OnEnable에서 하는 이유:
    // 플레이 도중 스크립트를 고치면 도메인 리로드가 일어나는데, 이때 Unity는 OnDisable/OnEnable은
    // 다시 부르지만 Awake는 부르지 않는다. Awake에서만 앉으면 리로드 뒤 자리가 빈 채로 남는다.
    private void OnEnable()
    {
        if (!GameServices.Battle.TryRegister(this))
        {
            Debug.LogWarning("[BattleManager] 씬에 두 개 이상 있습니다. 나중 것을 비활성화합니다.");
            enabled = false;
            return;
        }

        result.Reset();
        UnitController.OnAnyUnitDied += HandleUnitDied;
        // 엔티티가 된 적은 게임오브젝트가 아니라 브리지가 죽음을 알린다.
        EnemyWorldBridge.OnEnemyKilled += HandleEnemyEntityKilled;
    }

    private void OnDisable()
    {
        UnitController.OnAnyUnitDied -= HandleUnitDied;
        EnemyWorldBridge.OnEnemyKilled -= HandleEnemyEntityKilled;
        GameServices.Battle.Unregister(this);
    }

    private void HandleEnemyEntityKilled()
    {
        if (!started || ended) return;
        result.EnemyDeaths++;
    }

    private void Update()
    {
        if (returningToMain)
        {
            returnTimer -= Time.deltaTime;
            if (returnTimer <= 0f) ReturnToMain();
            return;
        }

        elapsed += Time.deltaTime;

        if (!started)
        {
            TryStart();
            return;
        }

        if (ended) return;

        result.Duration = elapsed;

        if (pendingOutcome != BattleOutcome.InProgress)
        {
            pendingEndTimer -= Time.deltaTime;
            if (pendingEndTimer <= 0f) Finish(pendingOutcome);
            return;
        }

        BattleOutcome outcome = EvaluateOutcome();
        if (outcome == BattleOutcome.InProgress) return;

        // 즉시 끝내지 않고 유예를 둔다. 마지막 유닛이 쓰러지는 모션이 남아 있기 때문.
        pendingOutcome = outcome;
        pendingEndTimer = endDelay;
    }

    // 스포너가 Start에서 유닛을 만들기 때문에 첫 프레임에는 레지스트리가 비어 있을 수 있다.
    // 양 팀이 모두 등장한 시점을 전투 시작으로 본다.
    // 살아 있는 적의 수. 게임오브젝트로 남은 적과 엔티티가 된 적을 함께 센다.
    //
    // 두 세계를 한 곳에서 세는 것이 중요하다. 예전처럼 UnitRegistry.Enemies만 보면,
    // 엔티티 1000마리가 멀쩡히 달려오는데도 게임오브젝트 적이 전멸한 순간 승리로 끝난다.
    private static int LivingEnemyCount => UnitRegistry.Enemies.Count + EnemyWorldBridge.EnemyCount;

    private void TryStart()
    {
        bool bothSidesPresent = UnitRegistry.Allies.Count > 0 && LivingEnemyCount > 0;
        if (!bothSidesPresent)
        {
            if (elapsed < startTimeout) return;

            // 시간 안에 양 팀이 모이지 않았다면 배치 문제다. 조용히 멈추지 않고 알린다.
            Debug.LogWarning($"[BattleManager] {startTimeout}초 안에 양 팀이 모이지 않아 전투를 시작하지 못했습니다. " +
                             $"(아군 {UnitRegistry.Allies.Count} / 적 {UnitRegistry.Enemies.Count})");
            enabled = false;
            return;
        }

        started = true;
        elapsed = 0f;
        result.Reset();

        allyRoster.Clear();
        allyRoster.AddRange(UnitRegistry.Allies);
        BattleEvents.RaiseStarted();
    }

    private BattleOutcome EvaluateOutcome()
    {
        bool alliesGone = UnitRegistry.Allies.Count == 0;
        bool enemiesGone = LivingEnemyCount == 0;

        if (alliesGone && enemiesGone) return BattleOutcome.Draw;
        if (enemiesGone) return BattleOutcome.Victory;
        if (alliesGone) return BattleOutcome.Defeat;
        return BattleOutcome.InProgress;
    }

    private void HandleUnitDied(UnitController unit)
    {
        if (unit == null || !started || ended) return;

        if (unit.Team == UnitTeam.Ally)
        {
            result.AllyDeaths++;

            // TODO: 테스트를 위해 영구 죽음(PartyRoster.MarkFallen) 임시 비활성화. 테스트 후 복구할 것.
            // if (PartyRoster.MarkFallen(unit.SourceCharacter))
            // {
            //     result.FallenCharacters.Add(unit.SourceCharacter);
            // }
        }
        else if (unit.Team == UnitTeam.Enemy)
        {
            result.EnemyDeaths++;
        }
    }

    private void Finish(BattleOutcome outcome)
    {
        ended = true;
        result.Outcome = outcome;
        result.Duration = elapsed;
        result.AllySurvivors = UnitRegistry.Allies.Count;

        // 기여도 → MVP → 경험치 → 승리 보상. 셈법은 BattleSettlement에 있다.
        new BattleSettlement(rewardSettings).Settle(outcome, allyRoster, FloorProgress.SelectedFloor, result);

        CaptureStress();
        SaveRoster();

        // 남은 적 엔티티를 치운다. 엔티티는 씬에 속하지 않아서 씬을 갈아도 그대로 살아남는다 —
        // 지우지 않으면 마을로 돌아간 뒤에도 표적 없는 고블린 무리가 계속 돌고, 다음 전투에
        // 지난 층의 잔당이 섞여 들어간다.
        EnemyHorde.Clear();

        BattleEvents.RaiseEnded(result);

        returnTimer = returnDelay;
        returningToMain = true;
    }

    // 성장과 영구 사망을 파일에 남긴다. 이게 없으면 플레이를 멈추는 순간 사망 기록이 사라져
    // "영구 죽음"이 실제로는 성립하지 않는다.
    private void ReturnToMain()
    {
        returningToMain = false;

        if (string.IsNullOrEmpty(mainSceneName))
        {
            Debug.LogWarning("[BattleManager] 메인 씬 이름이 비어 있어 돌아갈 수 없습니다.");
            return;
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene(mainSceneName);
    }

    // 전투에서 쌓인 스트레스만 전투 밖으로 들고 나간다.
    // 나머지 스탯은 다음 전투에 만회복되므로 굳이 옮기지 않는다.
    private void CaptureStress()
    {
        for (int i = 0; i < allyRoster.Count; i++)
        {
            UnitController unit = allyRoster[i];
            if (unit == null || unit.SourceCharacter == null || unit.Emotion == null) continue;

            CharacterStress.Set(unit.SourceCharacter, unit.Emotion.Profile.stress);
        }
    }

    private void SaveRoster()
    {
        rosterBuffer.Clear();
        for (int i = 0; i < allyRoster.Count; i++)
        {
            UnitController unit = allyRoster[i];
            if (unit != null && unit.SourceCharacter != null) rosterBuffer.Add(unit.SourceCharacter);
        }

        // 로스터 에셋이 있으면 그쪽을 우선한다. 출전하지 않은 캐릭터의 진행도가 지워지지 않도록.
        if (roster != null) SaveSystem.Save(roster.Members);
        else SaveSystem.Save(rosterBuffer);
    }
}
