using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

// 저장해 둔 GLB를 읽어 "싸울 수 있는 몸"으로 세우는 곳. 빌드에서 쓰는 길이다.
//
// 에디터에서 구워 둔 프리팹(CharacterSO.battlePrefab)이 있으면 그쪽이 언제나 우선이다.
// 그건 임포터가 아바타까지 만들어 둔 완성품이라 여기서 할 일이 없다.
// 여기는 빌드에서 소환한 캐릭터 — 프리팹이 있을 수 없는 캐릭터 — 를 위한 길이다.
//
// 세우는 순서가 중요하다.
//   1. 전투 부품만 든 템플릿을 꺼내되, Awake가 돌지 않게 꺼진 부모 아래에 만든다.
//   2. 그 아래에 GLB를 붙인다.
//   3. 뼈를 읽어 아바타를 세우고 Animator에 물린다.
//
// 1번에서 그냥 Instantiate하면 안 된다 — Awake는 Instantiate가 돌아오기 전에 이미 실행되므로,
// 받아서 SetActive(false)를 하는 것으로는 늦는다. 아직 사람도 아닌 Animator에서 손뼈를 찾다가
// 무기 소켓을 못 잡은 채로 굳고, UnitRegistry에는 싸우지도 않을 유령이 하나 등록된다.
// 꺼진 부모 아래에서 만들면 Awake 자체가 미뤄진다.
//
// 그래서 세워 둔 몸(프로토타입)은 계속 꺼진 부모 아래에 산다. 전투에 나갈 때는 그것을
// Instantiate해서 씬 루트에 놓는데, 그 순간 처음으로 Awake가 돈다 — 이미 사람인 몸으로.
public static class CharacterBodyFactory
{
    public const string TemplateResourceName = "CharacterBody";

    // 캐릭터 id → 꺼진 채로 대기 중인 몸. 씬이 바뀌어도 살아남는다(소환은 마을, 전투는 던전).
    private static readonly Dictionary<string, GameObject> Prototypes = new Dictionary<string, GameObject>();

    // GltfImport를 놓아 버리면 그것이 만든 메시와 텍스처가 함께 사라진다. 몸이 살아 있는 동안 같이 붙잡아 둔다.
    private static readonly Dictionary<string, GltfImport> Imports = new Dictionary<string, GltfImport>();

    // 세워 둔 몸이 사는 곳. 꺼져 있어서 그 아래 있는 동안에는 Awake가 돌지 않는다.
    private static Transform nursery;

    private static Transform Nursery()
    {
        if (nursery != null) return nursery;

        var go = new GameObject("[CharacterBodies]");
        go.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(go);
        nursery = go.transform;
        return nursery;
    }

    /// 이미 세워 둔 몸. 아직 안 세웠으면 null.
    public static GameObject Ready(CharacterSO character) =>
        character != null && Prototypes.TryGetValue(character.Id, out GameObject body) ? body : null;

    /// 디스크의 GLB에서 몸을 세운다. 세우지 못하면 onDone(null).
    /// 이미 세워 둔 몸이 있으면 그것을 그대로 돌려주고 끝난다.
    public static IEnumerator Build(CharacterSO character, Action<GameObject> onDone)
    {
        if (character == null) { onDone?.Invoke(null); yield break; }

        string id = character.Id;
        if (Prototypes.TryGetValue(id, out GameObject existing) && existing != null)
        {
            onDone?.Invoke(existing);
            yield break;
        }

        if (!CharacterModelStore.Exists(id))
        {
            onDone?.Invoke(null);
            yield break;
        }

        var template = Resources.Load<GameObject>(TemplateResourceName);
        if (template == null)
        {
            Debug.LogError($"[CharacterBodyFactory] 전투 부품 템플릿을 찾지 못했다: " +
                           $"Resources/{TemplateResourceName}. 이게 없으면 몸을 세워도 싸우지 못한다.");
            onDone?.Invoke(null);
            yield break;
        }

        // 아래 세 단계는 전부 네이티브로 내려가고, 잘못된 입력을 만나면 예외 없이 에디터째
        // 멈춰 버리는 일이 있다(특히 아바타 세우기). 어디서 멈췄는지 로그로 남겨 두지 않으면
        // 다음에도 "그냥 멈췄다"밖에 알 수 없다.
        // 코루틴이 한 번 쉬고 나서 시작한다. 부른 쪽의 호출 스택 위에서 곧바로 glTFast를 건드리면
        // await가 붙잡는 동기화 문맥이 Unity의 것이 아닐 수 있다.
        yield return null;

        Trace(character, "GLB 읽기 시작");

        // 프레임을 쪼개지 않는 에이전트를 쓴다.
        //
        // glTFast의 기본값(TimeBudgetPerFrameDeferAgent)은 예산을 넘기면 await Task.Yield()로
        // 다음 프레임을 기다리는데, 그 재개가 돌아오지 않으면 메인 스레드가 그대로 잠긴다 —
        // 에디터가 통째로 멈추고 CPU는 0인 채로 예외도 남지 않는다(실제로 두 번 겪었다).
        // 어차피 몸 굽기는 뒤에서 도는 일이라 프레임 몇 개를 한 번에 먹어도 상관없다.
        var import = new GltfImport(deferAgent: new UninterruptedDeferAgent());
        Task<bool> loading = import.LoadFile(CharacterModelStore.PathFor(id));
        while (!loading.IsCompleted) yield return null;

        Trace(character, "GLB 읽기 끝 (" + (loading.IsFaulted ? "실패" : loading.Result.ToString()) + ")");

        if (loading.IsFaulted || !loading.Result)
        {
            Debug.LogError($"[CharacterBodyFactory] {character.characterName}의 GLB를 읽지 못했다: " +
                           (loading.Exception != null ? loading.Exception.Message : "형식이 맞지 않는다"));
            onDone?.Invoke(null);
            yield break;
        }

        // 꺼진 부모 아래에서 만든다 — 아바타를 물리기 전에 Awake가 돌면 안 된다.
        GameObject body = UnityEngine.Object.Instantiate(template, Nursery());
        body.name = string.IsNullOrEmpty(character.characterName) ? id : character.characterName;

        Trace(character, "장면 꺼내기 시작");
        if (!import.InstantiateMainScene(body.transform))
        {
            Debug.LogError($"[CharacterBodyFactory] {character.characterName}의 GLB에서 장면을 꺼내지 못했다.");
            UnityEngine.Object.Destroy(body);
            onDone?.Invoke(null);
            yield break;
        }
        Trace(character, "장면 꺼내기 끝, 뼈 " + body.GetComponentsInChildren<Transform>(true).Length + "개");

        // 한 프레임 쉬어 준다. 여기까지가 무거워서, 아바타 세우기와 같은 프레임에 몰면
        // 에디터가 오래 멈춘 것처럼 보인다.
        yield return null;

        Trace(character, "아바타 세우기 시작");
        if (!CharacterModelRig.TryBuildAvatar(body, out Avatar avatar, out string problem))
        {
            Debug.LogError($"[CharacterBodyFactory] {character.characterName}: 사람으로 세우지 못했다 — {problem}");
            UnityEngine.Object.Destroy(body);
            onDone?.Invoke(null);
            yield break;
        }

        var animator = body.GetComponent<Animator>();
        animator.avatar = avatar;

        // 씬이 갈려도 남는다(소환은 마을, 전투는 던전). 부모가 DontDestroyOnLoad라 따라간다.
        Prototypes[id] = body;
        Imports[id] = import;

        Debug.Log($"[CharacterBodyFactory] {body.name}의 몸을 세웠다 (아바타 {avatar.name}).");
        onDone?.Invoke(body);
    }

    // 꺼내 놓는 일은 여기서 하지 않는다. 세워 둔 몸은 프리팹 에셋과 똑같이 Instantiate하면 되고
    // (부모 없는 활성 복사본이 나온다), 꺼낸 뒤에 전투 수치를 물리는 일까지는 스포너의 몫이다.

    // 네이티브 단계 사이사이에 발자국을 남긴다. 로그가 어디서 끊겼는지가 곧 어디서 멈췄는지다.
    private static void Trace(CharacterSO character, string step)
    {
        Debug.Log($"[CharacterBodyFactory] {character.characterName}: {step}");
    }

    /// 캐릭터가 사라졌을 때(영구 사망, 삭제) 붙잡고 있던 것을 놓는다.
    public static void Forget(string characterId)
    {
        if (Prototypes.TryGetValue(characterId, out GameObject body) && body != null)
            UnityEngine.Object.Destroy(body);
        Prototypes.Remove(characterId);

        if (Imports.TryGetValue(characterId, out GltfImport import)) import?.Dispose();
        Imports.Remove(characterId);
    }
}
