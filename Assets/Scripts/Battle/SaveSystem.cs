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
    private class CharacterRecord
    {
        // 안정적 식별자(CharacterSO.Id). 에셋 이름을 바꿔도 이 값은 그대로다.
        public string id;
        // 옛 세이브 호환용. id가 아직 없던 시절의 세이브는 여기(=에셋 이름)로 찾아낸다.
        public string assetName;
        public int level;
        public int exp;
        public int expToNext;
        public int strength;
        public int intelligence;
        public int vitality;
        public int agility;
        public bool fallen;
        public float stress;
        // 합성으로 배운 스킬. 예전에는 저장하지 않아서 게임을 껐다 켜면 통째로 사라졌다.
        public List<string> skillIds = new List<string>();
    }

    [Serializable]
    private class SaveData
    {
        public int highestClearedFloor;
        // 아래 stress 값들이 어느 시각 기준인지. 값과 시각이 함께 있어야
        // 세이브를 옮기거나 되돌렸을 때 회복이 두 번 적용되거나 사라지지 않는다.
        // 0이면 이 칸이 없던 시절의 세이브다 — 그때는 PlayerPrefs에 남은 시각을 쓴다.
        public long stressStampUtcTicks;
        public List<CharacterRecord> characters = new List<CharacterRecord>();
    }

    public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    public static bool HasSave => File.Exists(SavePath);

    public static void Save(IReadOnlyList<CharacterSO> roster)
    {
        if (roster == null) return;

        // 스트레스는 "정산 시각 기준의 값"으로 저장한다. 먼저 정산해 두지 않으면
        // 저장한 값에 이미 반영된 회복분을 다음 실행에서 한 번 더 빼게 된다.
        CharacterStress.Settle();

        var data = new SaveData
        {
            highestClearedFloor = FloorProgress.HighestCleared,
            stressStampUtcTicks = StressClock.StampTicks,
        };
        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null) continue;
            // 같은 에셋이 로스터에 여러 번 들어가 있을 수 있으므로 중복은 건너뛴다.
            if (Find(data.characters, so) != null) continue;

            // 에셋이 아니라 런타임 진행도를 저장한다(CharacterProgress 주석 참조).
            CharacterProgress.Entry entry = CharacterProgress.Of(so);

            var record = new CharacterRecord
            {
                id = so.Id,
                assetName = so.name,
                level = entry.Level,
                exp = entry.Exp,
                expToNext = entry.ExpToNext,
                strength = entry.Strength,
                intelligence = entry.Intelligence,
                vitality = entry.Vitality,
                agility = entry.Agility,
                fallen = PartyRoster.IsFallen(so),
                stress = CharacterStress.Get(so),
            };
            record.skillIds.AddRange(entry.SkillIds);
            data.characters.Add(record);
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
        // 스트레스 값을 얹기 전에 그 값들이 기준으로 삼는 시각부터 되돌린다.
        StressClock.RestoreStamp(data.stressStampUtcTicks);
        if (roster == null) return true;

        for (int i = 0; i < roster.Count; i++)
        {
            CharacterSO so = roster[i];
            if (so == null) continue;

            CharacterRecord p = Find(data.characters, so);
            if (p == null) continue;

            CharacterProgress.Restore(so, p.level, p.exp, p.expToNext,
                p.strength, p.intelligence, p.vitality, p.agility, p.skillIds);

            if (p.fallen) PartyRoster.MarkFallen(so);
            // 저장된 값은 저장 시점 기준이다. Set으로 넣으면 그 사이 흐른 자리비움 시간이
            // 통째로 버려지므로 반드시 Restore를 쓴다.
            CharacterStress.Restore(so, p.stress);
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

    // 안정적 식별자로 먼저 찾고, 없으면 에셋 이름으로 되짚는다.
    // 두 번째 경로는 id가 도입되기 전에 쓰던 세이브(그 시절 id 칸에는 에셋 이름이 들어 있다)를
    // 그대로 읽기 위한 것이다. 새로 저장할 때 id가 채워지므로 한 번 저장하면 첫 경로로 넘어간다.
    private static CharacterRecord Find(List<CharacterRecord> list, CharacterSO character)
    {
        if (character == null) return null;

        string id = character.Id;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && !string.IsNullOrEmpty(list[i].id) && list[i].id == id) return list[i];
        }

        string assetName = character.name;
        for (int i = 0; i < list.Count; i++)
        {
            CharacterRecord record = list[i];
            if (record == null) continue;
            if (record.assetName == assetName || record.id == assetName) return record;
        }

        return null;
    }
}
