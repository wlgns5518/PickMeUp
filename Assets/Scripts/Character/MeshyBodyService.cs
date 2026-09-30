using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// 소환한 캐릭터의 몸을 굽는 줄. 소환(CardSpawner)이 부탁하고, 전투(CharacterBattleSpawner)가 기다리고,
// 명단에서 빠진 사람(합성 재료)은 GameServices가 놓아 달라고 한다.
public interface IBodyBakery
{
    MeshyBodyService.BodyState StateOf(CharacterSO character);

    /// 굽는 중일 때의 진행률(0~1). 다른 상태에서는 의미가 없다.
    float ProgressOf(CharacterSO character);

    /// 이 캐릭터의 몸을 만들어 달라. 이미 있거나 이미 굽는 중이면 아무것도 하지 않는다.
    void Request(CharacterSO character);

    /// 이미 받아 둔 GLB가 있는 캐릭터만 세운다. Meshy를 두드리지 않으므로 크레딧이 들지 않는다.
    void RebuildIfDownloaded(CharacterSO character);

    /// 명단에서 사라진 캐릭터(합성 재료)의 몸을 메모리에서 내린다.
    void Release(CharacterSO character);

    /// 이 명단의 몸이 다 설 때까지 기다린다. 정해진 시간을 넘기면 그대로 돌아온다.
    IEnumerator WaitUntilReady(IReadOnlyList<CharacterSO> lineup, float timeoutSeconds);
}

// 소환한 캐릭터의 몸을 뒤에서 굽는 일꾼. 빌드에서도 돈다.
//
// 소환은 즉시 끝나야 한다. 카드가 뒤집히는 데 3분을 기다리게 할 수는 없으므로,
// 몸 굽기는 카드가 나온 뒤에 조용히 시작해서 조용히 끝난다. 다 구워지기 전에 전투에 나가면
// 그 캐릭터는 공용 몸(Y Bot)으로 나가고, 다음 판부터 제 몸으로 나온다.
//
// 한 번에 하나씩만 굽는다. 10연차를 돌리면 열 명이 줄을 서는데, 한꺼번에 보내면 Meshy 쪽에서
// 막히기도 하고 텍스처 열 장이 동시에 메모리에 올라온다.
//
// 굽고 나면 GLB가 디스크에 남는다(CharacterModelStore). 다음에 게임을 켤 때는 크레딧을 쓰지 않고
// 그 파일에서 다시 세운다.
//
// 게임오브젝트가 아니다. 예전에는 스스로 DontDestroyOnLoad 오브젝트를 만들어 앉는 싱글턴이었고 쓰는 쪽이
// 정적 메서드로 불렀다. 지금은 GameServices가 하나 만들어 IBodyBakery로 건네고, 코루틴만 CoroutineRunner에서
// 빌려 돌린다 — 줄과 상태가 게임오브젝트 수명에 묶이지 않는다.
public sealed class MeshyBodyService : IBodyBakery
{
    public enum BodyState
    {
        None,     // 아직 부탁받은 적 없다
        Queued,   // 줄을 섰다
        Working,  // 굽는 중
        Ready,    // 몸이 섰다
        Failed,   // 못 구웠다. 공용 몸으로 나간다
    }

    private const string EnabledKey = "MeshyBodyService.Enabled";

    // 굽기를 끌 수 있게 둔다. 에디터에서 소환을 반복 시험할 때 한 번에 44크레딧씩 나가면 곤란하다.
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
        set => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
    }

    private readonly MeshyBodyBaker baker;
    private readonly MeshyClient meshy;

    private readonly Dictionary<string, BodyState> states = new Dictionary<string, BodyState>();
    private readonly Dictionary<string, float> progress = new Dictionary<string, float>();
    private readonly Queue<CharacterSO> queue = new Queue<CharacterSO>();
    private bool working;

    // 줄에 서 있거나 굽는 도중에 명단에서 빠진 캐릭터(합성 재료). 차례가 오면 몸을 세우지 않고 놓는다.
    //
    // 줄(Queue)에서 바로 뺄 길이 없어 표시만 해 둔다. 이게 없던 때는 소환 직후 몸이 아직 줄에 있는 카드를
    // 합성 재료로 쓰면, 사라진 캐릭터를 위해 Meshy 굽기(44크레딧)가 그대로 돌고 다 선 몸(2K 텍스처 + 메시)이
    // 세션 내내 메모리에 남았다 — Release가 그 상태를 건드리지 않고 돌아갔기 때문이다.
    private readonly HashSet<string> released = new HashSet<string>();

    public MeshyBodyService(MeshyClient meshy = null)
    {
        this.meshy = meshy ?? MeshyClient.Shared;
        baker = new MeshyBodyBaker(this.meshy);
    }

    // ── 바깥에서 부르는 것 ───────────────────────────────────────────────

    public BodyState StateOf(CharacterSO character)
    {
        if (character == null) return BodyState.None;
        if (CharacterBodyFactory.Ready(character) != null) return BodyState.Ready;

        return states.TryGetValue(character.Id, out BodyState state) ? state : BodyState.None;
    }

    public float ProgressOf(CharacterSO character)
    {
        if (character == null) return 0f;
        return progress.TryGetValue(character.Id, out float value) ? value : 0f;
    }

    // 소환이 끝난 직후에 부른다(CardSpawner).
    public void Request(CharacterSO character)
    {
        if (character == null) return;
        KeepBody(character);

        BodyState state = StateOf(character);
        if (state == BodyState.Ready || state == BodyState.Queued || state == BodyState.Working) return;

        // 지난번에 구워 둔 GLB가 디스크에 있으면 크레딧을 쓰지 않는다. 읽어 세우기만 하면 된다.
        if (CharacterModelStore.Exists(character.Id))
        {
            Enqueue(character);
            return;
        }

        // 몸 굽기가 꺼져 있으면 공용 몸으로 나간다.
        if (!Enabled) return;

        Enqueue(character);
    }

    // 전투에 들어갈 때 부르는 쪽이 이것이다. Request를 부르면 아직 몸이 없는 캐릭터까지
    // 굽기 시작해서, 던전에 한 번 들어가는 것만으로 파티 전원분 크레딧이 나가 버린다.
    // 굽기를 시작하는 것은 소환하는 순간뿐이어야 한다.
    public void RebuildIfDownloaded(CharacterSO character)
    {
        if (character == null) return;
        if (!CharacterModelStore.Exists(character.Id)) return;
        KeepBody(character);

        BodyState state = StateOf(character);
        if (state == BodyState.Ready || state == BodyState.Queued || state == BodyState.Working) return;

        Enqueue(character);
    }

    // 세워 둔 몸은 2K 텍스처와 메시를 통째로 들고 씬 전환에도 살아남는다(CharacterBodyFactory).
    // 놓지 않으면 합성할 때마다 그 한 벌이 세션 내내 남는다. 디스크의 GLB는 지우지 않는다 —
    // 에셋이 남아 있는 한 다시 명단에 오를 수 있고, 그때는 크레딧 없이 파일에서 다시 세우면 된다.
    // 그래서 상태도 함께 지운다. Ready로 남겨 두면 다시 세울 차례가 와도 이미 섰다고 건너뛴다.
    public void Release(CharacterSO character)
    {
        if (character == null) return;

        string id = character.Id;
        states.TryGetValue(id, out BodyState state);

        // 줄에 섰거나 굽는 중이면 지금 지워 봐야 끝나고 다시 선다. 표시만 해 두고 차례가 왔을 때 놓는다
        // (released 주석 참조). 아직 줄에 있으면 굽기 자체를 건너뛰어 크레딧도 쓰지 않는다.
        if (state == BodyState.Queued || state == BodyState.Working)
        {
            released.Add(id);
            return;
        }

        CharacterBodyFactory.Forget(id);
        Drop(id);
    }

    // 다시 부탁받았다. 줄에 선 채로 놓으라고 해 둔 표시가 있으면 거둔다 — 명단에서 빠졌던 캐릭터가
    // 다시 오른 경우다. 거두지 않으면 차례가 와도 몸을 세우지 않는다.
    private void KeepBody(CharacterSO character) => released.Remove(character.Id);

    private void Drop(string id)
    {
        states.Remove(id);
        progress.Remove(id);
    }

    // 아직 안 된 캐릭터는 공용 몸으로 나가면 되지, 전투를 막을 일은 아니다.
    public IEnumerator WaitUntilReady(IReadOnlyList<CharacterSO> lineup, float timeoutSeconds)
    {
        if (lineup == null || lineup.Count == 0) yield break;

        // 디스크에 있는데 아직 안 세운 몸이 있으면 여기서 세운다(크레딧 안 씀).
        for (int i = 0; i < lineup.Count; i++) RebuildIfDownloaded(lineup[i]);

        float waited = 0f;
        while (waited < timeoutSeconds)
        {
            bool pending = false;
            for (int i = 0; i < lineup.Count; i++)
            {
                CharacterSO c = lineup[i];
                if (c == null) continue;

                // 아직 API를 두드리는 중인 몸은 기다리지 않는다 — 몇 분이 걸린다.
                // 디스크에서 읽어 세우는 것(몇 초)만 기다린다.
                if (StateOf(c) != BodyState.Ready && CharacterModelStore.Exists(c.Id)) pending = true;
            }
            if (!pending) yield break;

            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ── 줄 세우기 ────────────────────────────────────────────────────────

    private void Enqueue(CharacterSO character)
    {
        states[character.Id] = BodyState.Queued;
        queue.Enqueue(character);
        if (!working) CoroutineRunner.Run(WorkThroughQueue());
    }

    private IEnumerator WorkThroughQueue()
    {
        working = true;
        while (queue.Count > 0)
        {
            CharacterSO character = queue.Dequeue();
            if (character == null) continue;

            // 줄에서 기다리는 사이에 명단에서 빠졌다. 굽지도 세우지도 않는다.
            if (released.Remove(character.Id))
            {
                Drop(character.Id);
                continue;
            }

            yield return MakeBody(character);
        }
        working = false;
    }

    // ── 몸 하나 만들기 ───────────────────────────────────────────────────

    private IEnumerator MakeBody(CharacterSO character)
    {
        string id = character.Id;
        states[id] = BodyState.Working;
        progress[id] = 0f;

        // 파일이 이미 있으면 굽지 않고 세우기만 한다.
        if (!CharacterModelStore.Exists(id))
        {
            bool downloaded = false;
            yield return Bake(character, ok => downloaded = ok);
            if (!downloaded)
            {
                if (released.Remove(id)) Drop(id);
                else states[id] = BodyState.Failed;
                yield break;
            }
        }

        // 굽는 동안 명단에서 빠졌다. 받아 둔 GLB는 디스크에 남기고(Release와 같은 규칙) 메모리에는 세우지 않는다.
        if (released.Remove(id))
        {
            Drop(id);
            yield break;
        }

        progress[id] = 0.95f;
        GameObject body = null;
        yield return CharacterBodyFactory.Build(character, b => body = b);

        // 세우는 몇 프레임 사이에 빠졌다. 방금 선 몸을 그대로 내린다.
        if (released.Remove(id))
        {
            CharacterBodyFactory.Forget(id);
            Drop(id);
            yield break;
        }

        states[id] = body != null ? BodyState.Ready : BodyState.Failed;
        progress[id] = 1f;

        if (body == null)
            Debug.LogWarning($"[MeshyBodyService] {character.characterName}의 몸을 세우지 못했다. 공용 몸으로 나간다.");
    }

    // 초상화 → 외형 설명 → 전신 시트 → 메시 → 리깅 → GLB 저장.
    // 사슬 자체는 MeshyBodyBaker가 들고 있다(에디터 메뉴와 같은 것). 여기서는 줄 세우기와 상태만 본다.
    private IEnumerator Bake(CharacterSO character, Action<bool> onDone)
    {
        if (!meshy.HasKey)
        {
            Debug.LogWarning($"[MeshyBodyService] Meshy 키가 없어 {character.characterName}의 몸을 굽지 못한다. " +
                             $"{ApiKeys.FilePath}를 채워라.");
            onDone(false);
            yield break;
        }

        Task baking = BakeAsync(character);
        yield return new WaitForTask(baking);

        if (baking.IsFaulted)
        {
            Debug.LogError($"[MeshyBodyService] {character.characterName}의 몸을 굽지 못했다: " +
                           baking.Exception.GetBaseException().Message);
            onDone(false);
            yield break;
        }

        onDone(true);
    }

    private async Task BakeAsync(CharacterSO character)
    {
        string id = character.Id;

        // 초상화는 메인 스레드에서 떠야 한다(렌더 텍스처). 첫 await 전이라 여기는 늘 메인 스레드다.
        string appearance = await CharacterAppearance.DescribeAsync(character, TextureReadback.EncodePng(character.portrait));
        progress[id] = 0.05f;

        await baker.BakeAsync(id, appearance, (stage, value, status) => progress[id] = value);
    }
}
