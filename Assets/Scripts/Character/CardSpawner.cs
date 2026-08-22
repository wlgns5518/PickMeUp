using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 카드를 뽑아 화면에 늘어놓는 곳.
//
// 소환소(SummonUI)가 부르는 유일한 통로다. 확률표는 SummonTable이 들고 있고,
// 여기서는 굴린 등급을 생성기에 넘겨 그 등급의 캐릭터를 만들게 한다.
public class CardSpawner : MonoBehaviour
{
    [Header("Card")]
    [SerializeField] private CharacterCard cardPrefab;
    [SerializeField] private Transform cardParent;

    [Header("Generator")]
    [SerializeField] private MeshyCharacterGenerator generator;

    [Header("Spawn")]
    [Tooltip("씬을 켤 때 저절로 뽑을 장수. 소환소로만 뽑게 하려면 0으로 둔다.")]
    [SerializeField] private int spawnCountOnStart;

    // 소환이 진행 중인 동안 소환소 UI가 버튼을 잠근다. 같은 뽑기를 두 번 돌리면
    // 이름을 받아오는 요청이 겹쳐 이름이 통째로 중복된다.
    public bool IsBusy { get; private set; }

    // 뽑아 놓은 카드. 카드는 씬에 상설로 있는 캔버스에 붙으므로 치워 주는 쪽이 없으면
    // 화면에 그대로 쌓여 마을이 보이지 않게 된다. 소환소가 결과를 확인하면 ClearCards로 치운다.
    private readonly List<CharacterCard> spawned = new List<CharacterCard>();

    public int SpawnedCount => spawned.Count;

    private void Start()
    {
        if (spawnCountOnStart > 0) StartCoroutine(SpawnBatch(spawnCountOnStart));
    }

    // 확률표 없이 그냥 뽑는다. 등급은 유료 소환과 같은 확률이 된다.
    public IEnumerator SpawnBatch(int count)
    {
        yield return SummonBatch(SummonKind.Paid, count);
    }

    /// 소환소에서 부르는 뽑기.
    /// 이름은 한 번에 몰아 받는다(제미나이 RPM 제한). 등급은 장마다 따로 굴린다.
    /// onSummoned에는 만들어진 카드와 굴려 나온 별 등급이 함께 넘어간다 —
    /// 카드의 Character는 생성이 끝나야 채워지므로 결과 요약을 여기서 세지 못한다.
    public IEnumerator SummonBatch(SummonKind kind, int count, Action<CharacterCard, int> onSummoned = null)
    {
        if (count <= 0) yield break;
        if (!IsReady()) yield break;
        if (IsBusy)
        {
            Debug.LogWarning("[CardSpawner] 앞선 소환이 아직 끝나지 않았습니다.");
            yield break;
        }

        IsBusy = true;
        try
        {
            List<string> names = null;
            yield return generator.GenerateNames(count, list => names = list);

            for (int i = 0; i < count; i++)
            {
                string preset = (names != null && i < names.Count) ? names[i] : null;
                int stars = SummonTable.RollStars(kind);

                CharacterCard card = Spawn(preset, stars);
                onSummoned?.Invoke(card, stars);
            }
        }
        finally
        {
            // 이름 받기가 중간에 끊겨도 잠금은 풀려야 한다. 안 그러면 소환소가 영영 잠긴 채로 남는다.
            IsBusy = false;
        }
    }

    /// forcedStars가 1 이상이면 그 등급으로 나온다. 0이면 생성기가 알아서 굴린다.
    public CharacterCard Spawn(string presetName = null, int forcedStars = 0)
    {
        if (!IsReady()) return null;

        CharacterCard card = Instantiate(cardPrefab, cardParent != null ? cardParent : transform);
        card.ResetCard();
        spawned.Add(card);

        StartCoroutine(SpawnRoutine(card, presetName, forcedStars));
        return card;
    }

    /// 화면에 늘어놓은 카드를 전부 치운다. 소환 중에는 듣지 않는다 —
    /// 아직 그려지는 중인 카드를 지우면 남은 생성이 빈 자리에 값을 쓰게 된다.
    public void ClearCards()
    {
        if (IsBusy) return;

        for (int i = 0; i < spawned.Count; i++)
            if (spawned[i] != null) Destroy(spawned[i].gameObject);
        spawned.Clear();
    }

    private IEnumerator SpawnRoutine(CharacterCard card, string presetName, int forcedStars)
    {
        yield return generator.GenerateCharacter(so =>
        {
            // 뽑은 캐릭터는 보유 명단에 들어간다. 명단에 없으면 편성에도 합성에도 쓸 수 없다.
            // onUpdate는 메타데이터와 이미지 완료 때 두 번 불리는데 Add가 중복을 걸러 준다.
            OwnedRoster.Add(so);

            // 이미지가 오기 전에 카드가 치워질 수 있다. 지워진 카드에 값을 쓰면 예외가 난다.
            if (card != null) card.Apply(so);
        }, presetName, forcedStars);
    }

    private bool IsReady()
    {
        if (cardPrefab != null && generator != null) return true;

        Debug.LogError("[CardSpawner] cardPrefab or generator is missing.", this);
        return false;
    }
}
