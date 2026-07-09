using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardSpawner : MonoBehaviour
{
    [Header("Card")]
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

    public IEnumerator SpawnBatch(int count)
    {
        if (count <= 0) yield break;
        if (cardPrefab == null || generator == null)
        {
            Debug.LogError("[CardSpawner] cardPrefab or generator is missing.");
            yield break;
        }

        List<string> names = null;
        yield return generator.GenerateNames(count, list => names = list);

        for (int i = 0; i < count; i++)
        {
            string preset = (names != null && i < names.Count) ? names[i] : null;
            Spawn(preset);
        }
    }

    public CharacterCard Spawn(string presetName = null)
    {
        if (cardPrefab == null || generator == null)
        {
            Debug.LogError("[CardSpawner] cardPrefab or generator is missing.");
            return null;
        }

        CharacterCard card = Instantiate(cardPrefab, cardParent != null ? cardParent : transform);
        card.ResetCard();

        StartCoroutine(SpawnRoutine(card, presetName));
        return card;
    }

    private IEnumerator SpawnRoutine(CharacterCard card, string presetName)
    {
        yield return generator.GenerateCharacter(card.Apply, presetName);
    }
}
