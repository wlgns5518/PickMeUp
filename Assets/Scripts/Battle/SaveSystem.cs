using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 캐릭터 성장과 영구 사망을 파일로 남긴다.
//
// 영구 죽음은 세션을 넘어 유지될 때에야 무게를 가진다.
// PartyRoster의 사망 기록은 런타임 컬렉션뿐이라 플레이를 멈추면 사라졌고,
// 결과적으로 "영구"라는 말이 실제로는 성립하지 않았다.
//
// 로스터 상태와 층 해금 상태를 함께 남긴다.
// 캐릭터 식별은 에셋 이름(CharacterSO.name)을 쓴다. GUID는 에디터 전용이라 빌드에서 못 쓴다.
public static class SaveSystem
{
    private const string FileName = "pickmeup_roster.json";

    [Serializable]
    private class CharacterProgress
    {
        public string id;
        public int level;
        public int exp;
        public int expToNext;
        public int strength;
        public int intelligence;
        public int vitality;
        public int agility;
        public bool fallen;
        public float stress;
    }

    [Serializable]
    private class SaveData
    {
        public int highestClearedFloor;
        public List<CharacterProgress> characters = new List<CharacterProgress>();
    }

    public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    public static bool HasSave => File.Exists(SavePath);

    public static void Save(IReadOnlyList<CharacterSO> roster)
    {
        if (roster == null) return;

        var data = new SaveData { highestClearedFloor = FloorProgress.HighestCleared };
        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null) continue;
            // 같은 에셋이 로스터에 여러 번 들어가 있을 수 있으므로 중복은 건너뛴다.
            if (Find(data.characters, so.name) != null) continue;

            data.characters.Add(new CharacterProgress
            {
                id = so.name,
                level = so.level,
                exp = so.exp,
                expToNext = so.expToNext,
                strength = so.stats.strength,
                intelligence = so.stats.intelligence,
                vitality = so.stats.vitality,
                agility = so.stats.agility,
                fallen = PartyRoster.IsFallen(so),
                stress = CharacterStress.Get(so),
            });
        }

        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            // 저장 실패로 게임이 멈추면 안 되지만, 조용히 넘어가면 진행도가 사라진 걸 아무도 모른다.
            Debug.LogError($"[SaveSystem] 저장 실패: {e.Message}\n경로: {SavePath}");
        }
    }

    // 세이브가 없으면 false. 있으면 층 해금을 복원하고, 로스터가 주어지면 성장/영구 사망도 얹는다.
    // 메인 씬에는 로스터가 없으므로 층 해금만 읽는 호출도 허용한다.
    public static bool Load(IReadOnlyList<CharacterSO> roster = null)
    {
        if (!HasSave) return false;

        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 불러오기 실패: {e.Message}\n경로: {SavePath}");
            return false;
        }

        if (data == null) return false;

        FloorProgress.RestoreCleared(data.highestClearedFloor);
        if (roster == null) return true;

        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null) continue;

            CharacterProgress p = Find(data.characters, so.name);
            if (p == null) continue;

            so.level = p.level;
            so.exp = p.exp;
            so.expToNext = p.expToNext;
            so.stats.strength = p.strength;
            so.stats.intelligence = p.intelligence;
            so.stats.vitality = p.vitality;
            so.stats.agility = p.agility;

            if (p.fallen) PartyRoster.MarkFallen(so);
            CharacterStress.Set(so, p.stress);
        }

        return true;
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 삭제 실패: {e.Message}");
        }
    }

    private static CharacterProgress Find(List<CharacterProgress> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].id == id) return list[i];
        }

        return null;
    }
}
