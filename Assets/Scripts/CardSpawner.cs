using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardSpawner : MonoBehaviour
{
    [Header("Prefab & Parent")]
    [SerializeField] private CharacterCard cardPrefab;
    [SerializeField] private Transform cardParent;

    [Header("Generator")]
    [SerializeField] private MeshyCharacterGenerator generator;

    [Header("Spawn")]
    [SerializeField] private int spawnCountOnStart = 1;

    private void Start()
    {
        StartCoroutine(SpawnBatch(spawnCountOnStart));
    }

    // 이름을 한 번에 N개 받아 카드별로 주입
    public IEnumerator SpawnBatch(int count)
    {
        if (cardPrefab == null || generator == null)
        {
            Debug.LogError("[CardSpawner] cardPrefab 또는 generator가 비어있습니다.");
            yield break;
        }
        if (count <= 0) yield break;

        List<string> names = null;
        yield return generator.GenerateNames(count, list => names = list);

        for (int i = 0; i < count; i++)
        {
            string presetName = (names != null && i < names.Count) ? names[i] : null;
            Spawn(presetName);
        }
    }

    public CharacterCard Spawn(string presetName = null)
    {
        if (cardPrefab == null || generator == null)
        {
            Debug.LogError("[CardSpawner] cardPrefab 또는 generator가 비어있습니다.");
            return null;
        }

        Transform parent = cardParent != null ? cardParent : transform;
        CharacterCard card = Instantiate(cardPrefab, parent);
        card.ResetCard();
        StartCoroutine(generator.GenerateCharacter(card.Apply, presetName));
        return card;
    }
}
