using Unity.Cinemachine;
using UnityEngine;

// 큰 한 방이 화면에 남기는 진동. Cinemachine Impulse로 낸다.
//
// 카메라 자체는 Cinemachine으로 옮기지 않았다. 전투 카메라(PartyFollowCamera)는 "지금 보고 있는 한 명을
// 문턱 너머로 벗어날 때만 따라간다"는 고유한 규칙을 들고 있고, 그걸 CinemachineCamera로 다시 세우면
// 흔들기 하나를 위해 추적 규칙 전체를 옮기게 된다. Impulse는 카메라와 무관한 독립 계층이라
// (CinemachineImpulseManager) 소스로 신호를 내고, 기존 카메라가 그 신호를 읽어 제 위치에 더하기만 하면 된다.
//
// 흔드는 것은 지금 카메라가 비추는 캐릭터에게 일어난 일뿐이다. 거리로 줄이는 방식(Dissipating)을 썼을 때는
// 옆에서 싸우는 동료의 스킬이나 흘려내기도 조금씩 화면을 흔들어, 누구의 한 방인지 읽히지 않았다.
// 이제 부르는 쪽이 "이 한 방의 주인공"을 함께 넘기고, 그게 비추는 캐릭터가 아니면 버린다. 걸러낸 뒤에는
// 거리로 줄일 이유가 없으므로 어디서 불러도 같은 세기로 흔든다(Uniform).
//
// 부르는 곳은 판을 가르는 순간뿐이다: 스킬 적중, 흘려내기(퍼펙트 가드), 실드 배시, 광역 마법 착탄,
// 물려서 무너지는 순간, 죽음.
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineImpulseSource))]
public class CombatImpulse : MonoBehaviour
{
    [Tooltip("세기 1일 때 카메라가 밀리는 거리(미터). 부르는 쪽은 0~1 사이로 세기를 넘긴다 " +
             "(스킬 적중 0.5 → 약 14cm, 죽음 0.8 → 약 22cm).")]
    [SerializeField, Min(0f)] private float amplitude = 0.28f;

    [Tooltip("한 번 흔들리는 시간(초). 짧고 굵게 — 길면 흔들림이 아니라 멀미가 된다.")]
    [SerializeField, Min(0.01f)] private float duration = 0.24f;

    [Tooltip("이보다 짧은 간격(초)으로 들어온 약한 충격은 버린다. 같은 프레임에 광역 마법이 여섯을 " +
             "맞히면 여섯 번 겹쳐 흔들리는 것을 막는다. 더 센 충격은 간격과 무관하게 받는다.")]
    [SerializeField, Min(0f)] private float minInterval = 0.12f;

    [Tooltip("켜 두면 Awake에서 Impulse Source의 모양·길이·전파 방식을 위 값으로 덮어쓴다. " +
             "Impulse Source를 인스펙터에서 직접 다듬고 싶으면 끈다.")]
    [SerializeField] private bool configureSource = true;

    // 이 채널만 듣는다. 다른 연출(컷신 등)이 Impulse를 쓰게 돼도 전투 흔들림과 섞이지 않게 한다.
    private const int Channel = 1 << 3;

    private static CombatImpulse instance;

    private CinemachineImpulseSource source;
    private PartyFollowCamera followCamera;
    private float lastEmitTime = -999f;
    private float lastEmitStrength;

    private void Awake()
    {
        source = GetComponent<CinemachineImpulseSource>();
        followCamera = GetComponent<PartyFollowCamera>();
        if (configureSource) ConfigureSource();
    }

    private void OnEnable()
    {
        instance = this;
    }

    private void OnDisable()
    {
        if (instance == this) instance = null;
    }

    private void ConfigureSource()
    {
        CinemachineImpulseDefinition definition = source.ImpulseDefinition;
        definition.ImpulseChannel = Channel;
        definition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        definition.ImpulseDuration = duration;
        definition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
    }

    // 전투 코드가 부르는 입구. character는 이 한 방의 주인공이다 — 스킬을 맞힌 쪽, 흘려낸 쪽,
    // 물린 쪽, 쓰러진 쪽. 카메라가 그 캐릭터를 비추고 있을 때만 흔든다.
    // 카메라가 없는 씬(테스트, 마을)에서는 아무 일도 하지 않는다.
    //
    // strength는 0~1. 방향은 조금씩 흔들어 준다 — 매번 같은 방향으로 밀리면 흔들림이 아니라
    // 카메라가 한쪽으로 튀는 것으로 보인다.
    public static void Emit(UnitController character, float strength)
    {
        if (instance == null || character == null || strength <= 0f) return;
        if (!instance.IsFocused(character)) return;

        instance.EmitInternal(character.transform.position, Mathf.Clamp01(strength));
    }

    private bool IsFocused(UnitController character)
    {
        // 보통은 같은 오브젝트에 있다(PartyFollowCamera가 붙인다). 따로 붙인 경우에만 찾아 둔다.
        if (followCamera == null) followCamera = FindAnyObjectByType<PartyFollowCamera>();
        return followCamera != null && followCamera.FocusTarget == character;
    }

    private void EmitInternal(Vector3 position, float strength)
    {
        float now = Time.time;
        if (now < lastEmitTime + minInterval && strength <= lastEmitStrength) return;

        lastEmitTime = now;
        lastEmitStrength = strength;

        Vector2 side = Random.insideUnitCircle * 0.6f;
        Vector3 velocity = new Vector3(side.x, -1f, side.y).normalized * (amplitude * strength);
        source.GenerateImpulseAtPositionWithVelocity(position, velocity);
    }

    // 카메라가 LateUpdate에서 부른다.
    public static bool TrySample(Vector3 listener, out Vector3 offset, out Quaternion rotation)
    {
        offset = Vector3.zero;
        rotation = Quaternion.identity;
        if (instance == null) return false;

        return CinemachineImpulseManager.Instance.GetImpulseAt(listener, true, Channel, out offset, out rotation);
    }
}
