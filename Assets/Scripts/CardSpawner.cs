using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CardSpawner : MonoBehaviour
{
    [Header("Card")]
    [SerializeField] private CharacterCard cardPrefab;
    [SerializeField] private Transform cardParent;

    [Header("3D Character (성별별 프리팹)")]
    [SerializeField] private CharacterPartsController malePrefab;
    [SerializeField] private CharacterPartsController femalePrefab;
    [SerializeField] private Transform characterParent;
    [Tooltip("여러 캐릭터를 한 줄로 배치 시 간격(m)")]
    [SerializeField] private float characterSpacing = 2f;

    [Header("Save As Prefab (에디터 전용)")]
    [Tooltip("씬에 만든 캐릭터를 .prefab으로 저장")]
    [SerializeField] private bool saveCharacterPrefab = true;
    [SerializeField] private string modelDir = "Assets/CharacterModels";

    [Header("Generator")]
    [SerializeField] private MeshyCharacterGenerator generator;

    [Header("Spawn")]
    [SerializeField] private int spawnCountOnStart = 1;

    private int spawnedIndex;

    private void Start()
    {
        StartCoroutine(SpawnBatch(spawnCountOnStart));
    }

    public IEnumerator SpawnBatch(int count)
    {
        if (count <= 0) yield break;
        if (cardPrefab == null || generator == null)
        {
            Debug.LogError("[CardSpawner] cardPrefab 또는 generator가 비어있습니다.");
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
            Debug.LogError("[CardSpawner] cardPrefab 또는 generator가 비어있습니다.");
            return null;
        }

        CharacterCard card = Instantiate(cardPrefab, cardParent != null ? cardParent : transform);
        card.ResetCard();

        int index = spawnedIndex++;
        StartCoroutine(SpawnRoutine(card, index, presetName));
        return card;
    }

    private IEnumerator SpawnRoutine(CharacterCard card, int index, string presetName)
    {
        CharacterPartsController character = null;
        CharacterSO finalSO = null;

        Action<CharacterSO> onUpdate = so =>
        {
            finalSO = so;
            card.Apply(so);

            if (character == null)
            {
                var prefab = PickPrefab(so.gender);
                if (prefab != null)
                {
                    Vector3 basePos = characterParent != null ? characterParent.position : transform.position;
                    Vector3 pos = basePos + Vector3.right * (index * characterSpacing);
                    character = Instantiate(prefab, pos, Quaternion.identity, characterParent);
                    character.name = "Character_" + index; // 임시명, 곧 SO 이름으로 교체
                }
            }

            if (character != null) character.Apply(so);
        };

        yield return generator.GenerateCharacter(onUpdate, presetName);

        // 모든 적용이 끝난 캐릭터 인스턴스를 .prefab으로 베이크
#if UNITY_EDITOR
        if (saveCharacterPrefab && character != null && finalSO != null)
        {
            character.name = finalSO.characterName;
            string path = SaveAsPrefab(character.gameObject, finalSO.characterName);
            if (!string.IsNullOrEmpty(path))
            {
                finalSO.modelAssetPath = path;
                EditorUtility.SetDirty(finalSO);
                AssetDatabase.SaveAssets();
                Debug.Log($"[CardSpawner] 캐릭터 프리팹 저장: {path}");
            }
        }
#endif
    }

#if UNITY_EDITOR
    private string SaveAsPrefab(GameObject instance, string characterName)
    {
        try
        {
            string fullDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), modelDir);
            Directory.CreateDirectory(fullDir);

            string safe = SanitizeFileName(characterName ?? "character");
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{modelDir}/{safe}.prefab".Replace('\\', '/'));
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            return path;
        }
        catch (Exception e)
        {
            Debug.LogError($"[CardSpawner] 프리팹 저장 실패: {e.Message}");
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "character";
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        return sb.ToString();
    }
#endif

    private CharacterPartsController PickPrefab(Gender gender)
    {
        switch (gender)
        {
            case Gender.Male:   return malePrefab   != null ? malePrefab   : femalePrefab;
            case Gender.Female: return femalePrefab != null ? femalePrefab : malePrefab;
            default:            return malePrefab ?? femalePrefab;
        }
    }
}
