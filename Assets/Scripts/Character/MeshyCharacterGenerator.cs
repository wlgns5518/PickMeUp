using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// 소환 한 장을 만드는 순서만 쥐고 있는 곳 — 속 굴리기 → 이름 → 초상화 → 저장.
//
// 각 단계는 따로 산다. 속(직업·능력치)은 CharacterRoller, 이름은 CharacterNameGenerator(Gemini),
// 초상화는 CharacterPortraitGenerator(Meshy), 남기기는 CharacterAssetWriter. 통신은 그 아래
// MeshyClient/GeminiClient 한 벌을 모두가 같이 쓴다. 여기 남은 것은 인스펙터 값과 순서뿐이다.
public class MeshyCharacterGenerator : MonoBehaviour
{
    [Header("Meshy API")]
    // 키는 인스펙터에 직렬화하지 않는다 — 씬 파일로 커밋되기 때문. ApiKeys 참고.
    [Tooltip("Meshy text-to-image 모델 (nano-banana-pro 등)")]
    [SerializeField] private string aiModel = "nano-banana-pro";

    [Header("Gemini (이름 생성용)")]
    [Tooltip("gemini-2.5-flash-lite (RPM 15) / gemini-2.5-flash (RPM 5)")]
    [SerializeField] private string geminiModel = "gemini-2.5-flash-lite";
    [Tooltip("429 응답 시 응답 본문의 retryDelay를 읽어 자동 재시도")]
    [SerializeField] private int gemini429MaxRetries = 2;
    [Tooltip("retryDelay 파싱 실패 시 대기 (초)")]
    [SerializeField] private float gemini429FallbackWait = 30f;

    [Header("Polling")]
    [SerializeField] private float pollInterval = 3f;
    [SerializeField] private float timeoutSeconds = 600f;

    [Header("Retry")]
    [SerializeField] private int maxRetries = 3;
    [SerializeField] private float baseRetryDelay = 2f;

    [Header("Background")]
    [Tooltip("거의 흰색인 픽셀을 완전 투명으로 변환")]
    [SerializeField] private bool transparentBackground = true;
    [Tooltip("이 값 이상의 RGB는 흰색 배경으로 간주 (0~255)")]
    [Range(200, 255)] [SerializeField] private int whiteThreshold = 235;
    [Tooltip("경계 부드럽게 — 임계값 ~ 255 사이는 알파를 점진적으로")]
    [SerializeField] private bool softEdge = true;

    [Header("Asset Save")]
    [Tooltip("에디터에서 생성된 캐릭터를 .asset으로 저장")]
    [SerializeField] private bool saveAsAsset = true;
    [Tooltip("새로 만든 캐릭터를 얹을 보유 명단. 비워 두면 프로젝트에 하나뿐인 로스터 에셋을 찾아 쓴다.")]
    [SerializeField] private CharacterRosterSO roster;
    [SerializeField] private string imageDir = "Assets/Character/CharacterImage";
    [SerializeField] private string assetDir = "Assets/Character/Characters";

    // ─────────────────────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────────────────────

    /// onUpdate 두 번 호출: (1) 메타데이터 즉시, (2) 이미지 완료 후.
    /// presetName이 있으면 Gemini 호출 생략.
    /// forcedStars가 1 이상이면 그 등급으로 고정한다 — 소환소가 확률표로 굴린 결과를 넘긴다.
    public IEnumerator GenerateCharacter(Action<CharacterSO> onUpdate, string presetName = null, int forcedStars = 0)
    {
        CharacterRoller.JobEntry job = CharacterRoller.RollJob();
        CharacterRoller.Trait trait = CharacterRoller.RollTrait();
        CharacterSO so = CharacterRoller.Create(job, forcedStars);

        if (!string.IsNullOrEmpty(presetName))
        {
            so.characterName = presetName;
        }
        else
        {
            Task<string> naming = Names().GenerateOneAsync();
            yield return new WaitForTask(naming);
            so.characterName = naming.Status == TaskStatus.RanToCompletion ? naming.Result : CharacterNameGenerator.Fallback;
        }

        CharacterRoller.Finish(so, trait);
        onUpdate?.Invoke(so);

        Task<Texture2D> drawing = Portraits().GenerateAsync(CharacterPortraitGenerator.Prompt(job, trait));
        yield return new WaitForTask(drawing);
        if (drawing.Status == TaskStatus.RanToCompletion && drawing.Result != null)
            ApplyPortrait(drawing.Result, so);

        if (saveAsAsset) Writer().SaveCharacter(so);

        onUpdate?.Invoke(so);
    }

    /// 한 번 호출로 N개 이름 받기 — RPM 부담 회피
    public IEnumerator GenerateNames(int count, Action<List<string>> onComplete)
    {
        Task<List<string>> naming = Names().GenerateManyAsync(count);
        yield return new WaitForTask(naming);

        List<string> names = naming.Status == TaskStatus.RanToCompletion ? naming.Result : new List<string>();
        while (names.Count < count) names.Add(CharacterNameGenerator.Fallback);
        onComplete?.Invoke(names);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 단계별 일꾼 — 인스펙터 값을 그때그때 읽어 만든다(플레이 중에 고친 값도 다음 소환부터 먹는다).
    // ─────────────────────────────────────────────────────────────────────────

    private CharacterNameGenerator Names()
    {
        var gemini = new GeminiClient(new GeminiClient.Options
        {
            RateLimitRetries = gemini429MaxRetries,
            FallbackWaitSeconds = gemini429FallbackWait,
        });
        return new CharacterNameGenerator(gemini, geminiModel);
    }

    private CharacterPortraitGenerator Portraits()
    {
        var meshy = new MeshyClient(new MeshyClient.Options
        {
            PollIntervalSeconds = pollInterval,
            TimeoutSeconds = timeoutSeconds,
            Retry = new RetryPolicy(maxRetries, RetryPolicy.IsTransient,
                (attempt, reply) => TimeSpan.FromSeconds(baseRetryDelay * Math.Pow(2, attempt))),
            RetryLogLabel = "Meshy",
        });
        return new CharacterPortraitGenerator(meshy, aiModel);
    }

    private CharacterAssetWriter Writer() => new CharacterAssetWriter(imageDir, assetDir, roster);

    private void ApplyPortrait(Texture2D tex, CharacterSO so)
    {
        if (transparentBackground)
            tex = WhiteBackgroundRemover.Apply(tex, whiteThreshold, softEdge);

        so.portrait = Writer().StorePortrait(tex, so.characterName, out string assetPath);
        so.portraitAssetPath = assetPath;

#if UNITY_EDITOR
        // 에디터에서는 방금 쓴 PNG를 에셋으로 다시 읽어 쓰므로 내려받은 텍스처는 더 쓸 데가 없다.
        // 런타임 텍스처라 지우지 않으면 소환할 때마다 초상화 한 장(수 MB)씩 메모리에 쌓인다.
        // 빌드에서는 이 텍스처가 곧 초상화 스프라이트의 원본이라 남겨 둔다.
        Destroy(tex);
#endif
    }
}
