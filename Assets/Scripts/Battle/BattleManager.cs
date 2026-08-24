using System;
using System.Collections.Generic;
using UnityEngine;

// 전투에 시작과 끝을 붙여주는 컴포넌트.
// 지금까지는 스포너가 유닛을 뿌리면 끝이었고, 한쪽이 전멸해도 아무 일도 일어나지 않았다.
// 여기서 승패를 판정하고, 아군 사망은 PartyRoster에 영구 기록한다(원작의 영구 죽음).
[DisallowMultipleComponent]
public class BattleManager : MonoBehaviour
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

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    public static BattleManager Instance { get; private set; }

    // 정적 이벤트인 이유: 인스턴스 이벤트로 두면 구독자가 BattleManager.Instance를 먼저 찾아야 해서
    // Awake/Start 순서에 묶이고, 스크립트를 고쳐 도메인 리로드가 일어나면(플레이 중 흔한 일)
    // 구독이 통째로 끊긴 채 복구되지 않는다. 정적 이벤트 + OnEnable 구독이면 리로드 후에도 다시 붙는다.
    public static event Action OnBattleStarted;
    public static event Action<BattleResult> OnBattleEnded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents()
    {
        OnBattleStarted = null;
        OnBattleEnded = null;
    }

    private readonly BattleResult result = new BattleResult();

    // 전투 시작 시점의 아군 명단. 유닛은 죽으면 UnitRegistry에서 빠지기 때문에
    // 정산과 MVP 선정에 쓸 목록은 시작할 때 따로 붙잡아 둬야 한다.
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

    // Instance 대입을 Awake가 아니라 OnEnable에서 하는 이유:
    // 플레이 도중 스크립트를 고치면 도메인 리로드가 일어나는데, 이때 Unity는 OnDisable/OnEnable은
    // 다시 부르지만 Awake는 부르지 않는다. Awake에서만 대입하면 리로드 뒤 Instance가 null로 남는다.
    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BattleManager] 씬에 두 개 이상 있습니다. 나중 것을 비활성화합니다.");
            enabled = false;
            return;
        }

        Instance = this;
        result.Reset();
        UnitController.OnAnyUnitDied += HandleUnitDied;
    }

    private void OnDisable()
    {
        UnitController.OnAnyUnitDied -= HandleUnitDied;
        if (Instance == this) Instance = null;
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
    private void TryStart()
    {
        bool bothSidesPresent = UnitRegistry.Allies.Count > 0 && UnitRegistry.Enemies.Count > 0;
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
        if (debugLogs) Debug.Log($"[BattleManager] 전투 시작 — 아군 {UnitRegistry.Allies.Count} vs 적 {UnitRegistry.Enemies.Count}");
        OnBattleStarted?.Invoke();
    }

    private BattleOutcome EvaluateOutcome()
    {
        bool alliesGone = UnitRegistry.Allies.Count == 0;
        bool enemiesGone = UnitRegistry.Enemies.Count == 0;

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
            //     if (debugLogs) Debug.Log($"[BattleManager] 영구 사망: {unit.SourceCharacter.characterName}");
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

        BuildRewards();
        SelectMvp(outcome);
        GrantExp(outcome);

        if (debugLogs)
        {
            Debug.Log($"[BattleManager] 전투 종료 — {result.KoreanOutcome} " +
                      $"(생존 {result.AllySurvivors}, 아군 사망 {result.AllyDeaths}, 처치 {result.EnemyDeaths}, {result.Duration:F1}초)");
        }

        // 이긴 층은 해금 상태에 남긴다. 층은 자동으로 이어지지 않고,
        // 플레이어가 메인 씬에서 다시 고르는 구조라 여기서는 기록만 한다.
        if (outcome == BattleOutcome.Victory) FloorProgress.MarkCleared(FloorProgress.SelectedFloor);

        CaptureStress();
        SaveRoster();
        OnBattleEnded?.Invoke(result);

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

    // 참전한 아군 전원의 기여도를 결과로 옮겨 담는다.
    // 유닛 인스턴스는 곧 정리될 수 있으므로 결과창이 읽을 값은 여기서 복사해 둔다.
    private void BuildRewards()
    {
        for (int i = 0; i < allyRoster.Count; i++)
        {
            UnitController unit = allyRoster[i];
            if (unit == null) continue;

            result.Rewards.Add(new BattleReward
            {
                Character = unit.SourceCharacter,
                DisplayName = unit.SourceCharacter != null && !string.IsNullOrEmpty(unit.SourceCharacter.characterName)
                    ? unit.SourceCharacter.characterName
                    : unit.name,
                Kills = unit.Kills,
                DamageDealt = unit.DamageDealt,
                DamageTaken = unit.DamageTaken,
                Survived = !unit.IsDead,
            });
        }
    }

    // MVP는 승리했을 때만 뽑는다. 전멸한 판에서 최우수를 가리는 건 의미가 없다.
    private void SelectMvp(BattleOutcome outcome)
    {
        if (outcome != BattleOutcome.Victory || result.Rewards.Count == 0) return;

        BattleReward best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < result.Rewards.Count; i++)
        {
            BattleReward reward = result.Rewards[i];
            float score = MvpScore(reward);
            if (score <= bestScore) continue;

            bestScore = score;
            best = reward;
        }

        // 아무도 피해를 주지도 받지도 않은 판(예: 적이 스스로 사라진 경우)에는 MVP를 비워 둔다.
        if (best == null || bestScore <= 0f) return;

        best.IsMvp = true;
        result.Mvp = best;
    }

    private float MvpScore(BattleReward reward)
    {
        return reward.DamageDealt
               + reward.Kills * rewardSettings.mvpKillWeight
               + reward.DamageTaken * rewardSettings.mvpTankWeight
               + (reward.Survived ? rewardSettings.mvpSurvivalBonus : 0f);
    }

    // 쓰러진 참가자도 경험치를 받는다(비율은 expRatioWhenDown).
    //
    // 예전에는 생존자에게만 주고 쓰러진 쪽은 통째로 건너뛰었다. 영구 사망이 켜져 있을 때는
    // 다시 출전하지 않으니 문제가 없었지만, 영구 사망이 꺼진 지금은 쓰러진 캐릭터도 다음 전투에
    // 그대로 나온다. 그래서 한 번 쓰러진 캐릭터만 레벨이 영영 멈춘 채 계속 출전하게 됐다.
    private void GrantExp(BattleOutcome outcome)
    {
        int baseExp = outcome == BattleOutcome.Victory
            ? rewardSettings.expOnVictory
            : rewardSettings.expOnDefeat;

        for (int i = 0; i < result.Rewards.Count; i++)
        {
            BattleReward reward = result.Rewards[i];
            if (reward.Character == null) continue;

            int exp = baseExp
                      + reward.Kills * rewardSettings.expPerKill
                      + reward.DamageDealt / Mathf.Max(1, rewardSettings.damagePerExp);
            if (reward.IsMvp) exp += rewardSettings.mvpExpBonus;

            // 끝까지 버틴 쪽이 더 받는다는 규칙은 남긴다.
            if (!reward.Survived)
            {
                exp = Mathf.RoundToInt(exp * Mathf.Clamp01(rewardSettings.expRatioWhenDown));
            }

            reward.ExpGained = exp;
            reward.LevelBefore = reward.Character.Level;
            reward.Character.GainExp(exp);
            reward.LevelAfter = reward.Character.Level;
        }
    }
}
