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
        for (int i = 0; i < spawnCountOnStart; i++) Spawn();
    }

    public CharacterCard Spawn()
    {
        if (cardPrefab == null)
        {
            Debug.LogError("[CardSpawner] cardPrefab이 비어 있습니다.");
            return null;
        }
        if (generator == null)
        {
            Debug.LogError("[CardSpawner] generator가 비어 있습니다.");
            return null;
        }

        Transform parent = cardParent != null ? cardParent : transform;
        CharacterCard card = Instantiate(cardPrefab, parent);
        card.ResetCard();
        StartCoroutine(generator.GenerateCharacter(card.Apply));
        return card;
    }
}
