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
// 로스터 상태와 층 해금 상태, 무기창고(제작한 장비와 누가 무엇을 들었는지), 모아 둔 제작 재료,
// 플레이어 이름과 재화, 파티 편성(세 파티와 고른 파티), 시설 레벨을 함께 남긴다.
// 캐릭터 식별은 에셋 이름(CharacterSO.name)을 쓴다. GUID는 에디터 전용이라 빌드에서 못 쓴다.
public static class SaveSystem
{
    private const string FileName = "pickmeup_roster.json";

    // 무기창고의 장비 한 점. 무기는 에셋 이름으로, 주인은 CharacterSO.Id로 가리킨다.
    [Serializable]
    private class EquipmentRecord
    {
        public string weapon;
        public EquipmentGrade grade;
        // 비어 있으면 창고에 보관 중이다.
        public string owner;
        // 강화 단계. 이 칸이 없던 세이브는 +0으로 읽힌다.
        public int level;
    }

    // 제작 재료 한 칸. 종류와 등급이 같은 재료는 개수로 쌓인다.
    [Serializable]
    private class MaterialRecord
    {
        public MaterialKind kind;
        public EquipmentGrade grade;
        public int count;
    }

    // 플레이어 본인의 것. 이름이 비어 있으면 기본 이름(PlayerAccount.DefaultName)을 쓴다.
    [Serializable]
    private class AccountRecord
    {
        public string name;
        public long gold;
        public long gems;
        // 시작 젬(GameEconomy.StarterGems)을 이미 받았는지. 이 칸이 없던 세이브는 아직 안 받은 것으로 읽혀 한 번 받는다.
        public bool starterGranted;
    }

    // 파티 편성(PartyDeck). 파티마다 고른 순서대로 캐릭터를 CharacterSO.Id로 적는다 — 순서가 곧
    // 스폰 지점 배정 순서라 그대로 남겨야 한다.
    [Serializable]
    private class PartyRecord
    {
        public List<string> members = new List<string>();
    }

    [Serializable]
    private class PartyDeckRecord
    {
        // 편성 화면에서 마지막으로 고른 파티(층에 들어갈 때 출전하는 파티).
        public int active;
        public List<PartyRecord> parties = new List<PartyRecord>();
    }

    // 시설 레벨 한 칸(FacilityLevels). 시설은 enum 순번이 아니라 이름으로 적는다 — 순번으로 적으면
    // VillageBlockout.Kind에 항목이 끼어들 때 옛 세이브가 엉뚱한 시설을 가리킨다.
    [Serializable]
    private class FacilityRecord
    {
        public string kind;
        public int level;
    }

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
        // 전투에 나선 횟수. 이 필드가 없던 세이브는 0으로 읽힌다.
        public int battlesFought;
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
        // 이 칸이 없던 시절의 세이브는 빈 창고로 읽힌다.
        public List<EquipmentRecord> equipment = new List<EquipmentRecord>();
        // 마찬가지로, 없으면 재료 하나 없이 시작한다.
        public List<MaterialRecord> materials = new List<MaterialRecord>();
        // 없던 시절의 세이브는 기본 이름에 재화 0으로 읽힌다.
        public AccountRecord account = new AccountRecord();
        // 없던 시절의 세이브는 세 파티가 비어 있는 것으로 읽힌다.
        public PartyDeckRecord party = new PartyDeckRecord();
        // 없던 시절의 세이브는 모든 시설이 1레벨로 읽힌다.
        public List<FacilityRecord> facilities = new List<FacilityRecord>();
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
                battlesFought = entry.BattlesFought,
            };
            record.skillIds.AddRange(entry.SkillIds);
            data.characters.Add(record);
        }

        // 창고와 재료는 스스로 파일에서 읽어 온 뒤에 적는다(EquipmentInventory.Items 등). 창고를 한 번도
        // 열지 않은 전투 씬에서 저장해도 방금 덮어쓸 파일에 있던 장비와 재료가 그대로 실린다.
        WriteEquipment(data);
        WriteMaterials(data);
        WriteAccount(data);
        WriteParty(data);
        WriteFacilities(data);
        Write(data);
    }

    // 무기창고만 저장한다. 제작·장착은 마을에서 일어나 로스터 명단을 쥔 쪽이 없으므로,
    // 파일에 이미 있는 성장 기록은 그대로 두고 장비 칸만 갈아 끼운다.
    public static void SaveEquipment() => Patch(WriteEquipment);

    // 재료만 저장한다. 이유는 SaveEquipment와 같다.
    public static void SaveMaterials() => Patch(WriteMaterials);

    // 이름과 재화만 저장한다. 이유는 SaveEquipment와 같다.
    public static void SaveAccount() => Patch(WriteAccount);

    // 파티 편성만 저장한다. 편성은 마을의 훈련소에서 바뀌므로 이유는 SaveEquipment와 같다.
    // PartyDeck이 바뀔 때마다 스스로 부른다.
    public static void SaveParty() => Patch(WriteParty);

    // 시설 레벨만 저장한다. 업그레이드는 마을에서 일어나므로 이유는 SaveEquipment와 같다.
    public static void SaveFacilities() => Patch(WriteFacilities);

    // FacilityLevels가 처음 쓰일 때 스스로 부른다. 세이브가 없거나 깨졌으면 모든 시설이 1레벨이다.
    public static void LoadFacilities()
    {
        var restored = new List<KeyValuePair<VillageBlockout.Kind, int>>();

        SaveData data;
        if (HasSave && TryRead(out data) && data.facilities != null)
        {
            for (int i = 0; i < data.facilities.Count; i++)
            {
                FacilityRecord record = data.facilities[i];
                if (record == null || !Enum.TryParse(record.kind, out VillageBlockout.Kind kind)) continue;
                restored.Add(new KeyValuePair<VillageBlockout.Kind, int>(kind, record.level));
            }
        }

        FacilityLevels.Restore(restored);
    }

    private static void WriteFacilities(SaveData data)
    {
        var levels = new List<KeyValuePair<VillageBlockout.Kind, int>>();
        FacilityLevels.Snapshot(levels);

        data.facilities = new List<FacilityRecord>();
        for (int i = 0; i < levels.Count; i++)
            data.facilities.Add(new FacilityRecord { kind = levels[i].Key.ToString(), level = levels[i].Value });
    }

    // 세이브의 편성을 PartyDeck에 얹는다. 세션마다 한 번, 보유 명단을 세운 뒤에 부른다(RosterBootstrap).
    //
    // 캐릭터는 보유 명단(owned)에서 Id로 찾는다. 합성 재료로 사라졌거나 명단에서 빠진 사람은 조용히 건너뛴다 —
    // 없는 사람을 붙들고 있으면 편성에 빈칸이 생기고 전투에 끌려 나갈 사람이 없다.
    // 세이브가 없거나 깨졌으면 빈 편성으로 시작한다.
    public static void LoadParty(IReadOnlyList<CharacterSO> owned)
    {
        var restored = new List<IReadOnlyList<CharacterSO>>(PartyDeck.PartyCount);
        int active = 0;

        SaveData data;
        PartyDeckRecord record = HasSave && TryRead(out data) ? data.party : null;
        if (record != null && record.parties != null)
        {
            active = record.active;
            for (int p = 0; p < record.parties.Count && p < PartyDeck.PartyCount; p++)
            {
                var members = new List<CharacterSO>();
                PartyRecord party = record.parties[p];
                if (party != null && party.members != null)
                {
                    for (int m = 0; m < party.members.Count; m++)
                    {
                        CharacterSO character = FindOwned(owned, party.members[m]);
                        if (character != null) members.Add(character);
                    }
                }
                restored.Add(members);
            }
        }

        PartyDeck.Restore(restored, active);
    }

    private static CharacterSO FindOwned(IReadOnlyList<CharacterSO> owned, string id)
    {
        if (owned == null || string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < owned.Count; i++)
        {
            CharacterSO character = owned[i];
            if (character != null && character.Id == id) return character;
        }

        return null;
    }

    private static void WriteParty(SaveData data)
    {
        // 이번 세션에 편성을 아직 읽지 않았으면 파일에 있던 편성을 그대로 둔다. Patch는 파일에서 읽은 값을
        // 들고 오므로 손대지 않으면 되고, 새로 짜는 Save는 파일의 편성을 옮겨 담는다(아래).
        if (!PartyDeck.IsRestored)
        {
            if (data.party == null || data.party.parties == null || data.party.parties.Count == 0) CarryPartyFromFile(data);
            return;
        }

        var record = new PartyDeckRecord { active = PartyDeck.ActiveIndex };
        for (int p = 0; p < PartyDeck.PartyCount; p++)
        {
            var party = new PartyRecord();
            IReadOnlyList<CharacterSO> members = PartyDeck.Party(p);
            for (int m = 0; m < members.Count; m++)
            {
                if (members[m] != null) party.members.Add(members[m].Id);
            }
            record.parties.Add(party);
        }

        data.party = record;
    }

    private static void CarryPartyFromFile(SaveData data)
    {
        SaveData existing;
        if (HasSave && TryRead(out existing) && existing.party != null) data.party = existing.party;
    }

    // 파일에 이미 있는 것은 그대로 두고 한 칸만 갈아 끼운다.
    private static void Patch(Action<SaveData> write)
    {
        SaveData data;
        if (HasSave)
        {
            if (!TryRead(out data)) return; // 깨진 파일을 창고만 든 새 파일로 덮으면 남은 진행도까지 영영 사라진다.
        }
        else
        {
            // 스트레스 기준 시각은 0으로 둔다(기록 없음). 캐릭터 기록이 하나도 없는 파일이라 기준으로 삼을 값도 없다.
            data = new SaveData { highestClearedFloor = FloorProgress.HighestCleared };
        }

        write(data);
        Write(data);
    }

    // MaterialInventory가 처음 쓰일 때 스스로 부른다. 세이브가 없거나 깨졌으면 재료 없이 시작한다.
    public static void LoadMaterials()
    {
        var restored = new List<KeyValuePair<CraftMaterial, int>>();

        SaveData data;
        if (HasSave && TryRead(out data) && data.materials != null)
        {
            for (int i = 0; i < data.materials.Count; i++)
            {
                MaterialRecord record = data.materials[i];
                if (record == null || record.count <= 0) continue;
                restored.Add(new KeyValuePair<CraftMaterial, int>(new CraftMaterial(record.kind, record.grade), record.count));
            }
        }

        MaterialInventory.Restore(restored);
    }

    private static void WriteMaterials(SaveData data)
    {
        data.materials = new List<MaterialRecord>();

        var stacks = new List<KeyValuePair<CraftMaterial, int>>();
        MaterialInventory.CollectNonEmpty(stacks);
        for (int i = 0; i < stacks.Count; i++)
        {
            data.materials.Add(new MaterialRecord
            {
                kind = stacks[i].Key.Kind,
                grade = stacks[i].Key.Grade,
                count = stacks[i].Value,
            });
        }
    }

    // PlayerAccount가 처음 쓰일 때 스스로 부른다. 세이브가 없거나 깨졌으면 기본 이름에 재화 0으로 시작한다.
    public static void LoadAccount()
    {
        SaveData data;
        AccountRecord record = HasSave && TryRead(out data) ? data.account : null;
        if (record == null) record = new AccountRecord();

        PlayerAccount.Restore(record.name, record.gold, record.gems, record.starterGranted);
    }

    private static void WriteAccount(SaveData data)
    {
        PlayerAccount.Snapshot(out string name, out long gold, out long gems, out bool starterGranted);
        data.account = new AccountRecord { name = name, gold = gold, gems = gems, starterGranted = starterGranted };
    }

    // EquipmentInventory가 처음 쓰일 때 스스로 부른다. 세이브가 없거나 깨졌으면 빈 창고로 시작한다.
    public static void LoadEquipment()
    {
        var restored = new List<OwnedEquipment>();

        SaveData data;
        if (HasSave && TryRead(out data) && data.equipment != null)
        {
            for (int i = 0; i < data.equipment.Count; i++)
            {
                EquipmentRecord record = data.equipment[i];
                if (record == null) continue;

                WeaponDefinition weapon = WeaponCatalog.Find(record.weapon);
                if (weapon == null)
                {
                    Debug.LogWarning($"[SaveSystem] 무기창고의 '{record.weapon}'을(를) 카탈로그에서 찾지 못해 건너뜁니다.");
                    continue;
                }

                restored.Add(new OwnedEquipment(weapon, record.grade, record.owner, record.level));
            }
        }

        EquipmentInventory.Restore(restored);
    }

    private static void WriteEquipment(SaveData data)
    {
        data.equipment = new List<EquipmentRecord>();

        IReadOnlyList<OwnedEquipment> items = EquipmentInventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            OwnedEquipment item = items[i];
            if (item == null || item.Weapon == null) continue;

            data.equipment.Add(new EquipmentRecord
            {
                weapon = item.Weapon.name,
                grade = item.Grade,
                owner = item.OwnerId,
                level = item.Level,
            });
        }
    }

    private static bool TryRead(out SaveData data)
    {
        data = null;
        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 불러오기 실패: {e.Message}\n경로: {SavePath}");
            return false;
        }
        return data != null;
    }

    private static void Write(SaveData data)
    {
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
        if (!TryRead(out data)) return false;

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
            CharacterProgress.RestoreBattlesFought(so, p.battlesFought);

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
            // 창고와 재료는 파일에서 스스로 읽어 온 값을 들고 있다. 파일이 사라졌는데 그대로 두면 다음 저장이 되살려 놓는다.
            EquipmentInventory.Forget();
            MaterialInventory.Forget();
            PlayerAccount.Forget();
            FacilityLevels.Forget();
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
