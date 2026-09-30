using UnityEngine;

// 게임 전체가 나눠 쓰는 상태를 만들고 서로 잇는 유일한 곳(조립 지점).
//
// 상태 하나하나(지갑, 무기창고, 재료, 시설 레벨, 층 진행도, 편성, 보유 명단, 사망 기록, 성장, 스트레스)는
// 자기 규칙만 안다. 세이브 파일과 언제 이어지는지, 누가 빠지면 무엇을 같이 치우는지는 여기 한 곳에만 적는다.
// 예전에는 그 연결이 상태마다 흩어져 있었다 — 지갑이 SaveSystem을 부르고, 보유 명단이 편성·무기창고·몸 굽기를
// 직접 불렀다. 연결 하나를 바꾸려면 여러 클래스를 고쳐야 했고, 하나만 떼어 시험할 수도 없었다.
//
// 생성자로 의존성을 받을 수 있는 코드는 인터페이스(IWallet 등)를 받는다. 씬에 놓인 컴포넌트와 테스트는
// 예전 이름의 입구(PlayerAccount, EquipmentInventory …)를 그대로 부른다 — 입구는 여기로 넘기기만 한다.
public static class GameServices
{
    private static PlayerPrefsStressClock clock;
    private static StressLedger stress;
    private static CharacterProgressStore progress;
    private static FallenRecord fallen;
    private static FloorProgressStore floors;
    private static Wallet wallet;
    private static FacilityLevelStore facilities;
    private static EquipmentStore armory;
    private static MaterialStore materials;
    private static PartyDeckStore deck;
    private static OwnedRosterStore roster;
    private static SaveService saves;

    // ---- 쓰는 쪽이 보는 모습(인터페이스) -------------------------------------------

    public static IWallet Account => wallet;
    public static IEquipmentStore Armory => armory;
    public static IMaterialStore Materials => materials;
    public static IFacilityLevels Facilities => facilities;
    public static IFloorProgress Floors => floors;
    public static IPartyDeck Deck => deck;
    public static IOwnedRoster Roster => roster;
    public static IFallenRecord Fallen => fallen;
    public static ICharacterProgress Progress => progress;
    public static IStressLedger Stress => stress;
    public static IStressClock Clock => clock;
    public static SaveService Saves => saves;

    // ---- 게임 안에서 도는 서비스 ------------------------------------------------------
    // 씬에 놓인 컴포넌트가 켜질 때 자리에 앉고 꺼질 때 내려간다(ServiceSlot). 쓰는 쪽은 인터페이스만 본다.

    /// 지금 진행 중인 전투. 전투 씬 밖에서는 Peek이 null이다.
    public static ServiceSlot<IBattleSession> Battle { get; private set; }

    /// 화면 흔들기. 카메라가 없으면 아무것도 하지 않는 대역이 대신 앉는다.
    public static ServiceSlot<ICombatShake> Shake { get; private set; }

    /// 피 효과. 씬에 놓인 풀이 없으면 처음 쓰일 때 하나 만든다.
    public static ServiceSlot<IBloodEffects> BloodEffects { get; private set; }

    /// 소환한 캐릭터의 3D 몸 굽기 줄. 게임오브젝트가 아니라 늘 있다.
    public static IBodyBakery Bodies { get; private set; }

    /// 지휘관 명령(집중·후퇴·진형). 전투 하나가 쓰는 값이라 플레이마다 새로 만든다.
    public static IPartyCommander Command { get; private set; }

    /// 팀이 공유하는 "마지막으로 발견된 적" 게시판.
    public static IThreatBoard Threats { get; private set; }

    // ---- 입구(정적 클래스)와 세이브가 쓰는 실제 상태 ------------------------------------
    // 파일에서 되살리기(Restore)·잊기(Forget)처럼 화면이 부르면 안 되는 일은 인터페이스에 없다.

    internal static Wallet AccountState => wallet;
    internal static EquipmentStore ArmoryState => armory;
    internal static MaterialStore MaterialState => materials;
    internal static FacilityLevelStore FacilityState => facilities;
    internal static FloorProgressStore FloorState => floors;
    internal static PartyDeckStore DeckState => deck;
    internal static OwnedRosterStore RosterState => roster;
    internal static FallenRecord FallenState => fallen;
    internal static CharacterProgressStore ProgressState => progress;
    internal static StressLedger StressState => stress;
    internal static PlayerPrefsStressClock ClockState => clock;

    static GameServices() => Compose();

    // 도메인 리로드를 끈 에디터에서 이전 플레이의 값과 구독자가 남지 않도록 플레이 시작마다 새로 짠다.
    // 실제 값은 세이브에서 다시 읽는다. 정산 시각만은 세이브와 별개로 PlayerPrefs에서 읽는다(PlayerPrefsStressClock).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        Compose();
        clock.LoadFromPrefs();
    }

    private static void Compose()
    {
        clock = new PlayerPrefsStressClock();
        stress = new StressLedger(clock);
        progress = new CharacterProgressStore();
        fallen = new FallenRecord();
        floors = new FloorProgressStore();
        wallet = new Wallet(GameEconomy.StarterGems);
        facilities = new FacilityLevelStore(wallet);
        armory = new EquipmentStore();
        materials = new MaterialStore();
        deck = new PartyDeckStore(fallen, FacilityUnlocks.IsPartyUsable);
        roster = new OwnedRosterStore();

        saves = new SaveService(wallet, armory, materials, facilities, deck, progress, fallen, stress, clock, floors);

        // 세이브 파일과 잇기 — 처음 쓰일 때 채우고, 바뀔 때마다 그 칸만 저장한다.
        wallet.BindLoader(saves.LoadAccount);
        wallet.Committed += saves.SaveAccount;
        armory.BindLoader(saves.LoadEquipment);
        armory.Committed += saves.SaveEquipment;
        materials.BindLoader(saves.LoadMaterials);
        materials.Committed += saves.SaveMaterials;
        facilities.BindLoader(saves.LoadFacilities);
        facilities.Committed += saves.SaveFacilities;
        // 편성은 보유 명단이 서야 되살릴 수 있어서 처음 쓰일 때 채우지 않는다(RosterBootstrap이 부른다).
        deck.Committed += saves.SaveParty;

        Battle = new ServiceSlot<IBattleSession>();
        Shake = new ServiceSlot<ICombatShake>(fallback: NullCombatShake.Instance);
        BloodEffects = new ServiceSlot<IBloodEffects>(create: BloodEffectPool.CreateHost);
        Bodies = new MeshyBodyService();
        Command = new PartyCommander();
        Threats = new ThreatBoard();

        // 보유 명단에서 빠진 사람(합성 재료)의 딸린 것을 치운다.
        //  - 편성에 남아 있으면 가지고 있지도 않은 사람이 전투에 끌려 나간다.
        //  - 들고 있던 제작 장비는 무기창고로 돌아온다. 사라진 사람 손에 걸린 칼은 다시 꺼낼 길이 없다.
        //  - 세워 둔 3D 몸은 씬을 넘어 살아남는 것이라 여기서 놓지 않으면 세션 내내 남는다.
        roster.Removed += character => deck.RemoveEverywhere(character);
        roster.Removed += armory.UnequipAll;
        roster.Removed += Bodies.Release;
    }
}
