using System;
using System.Collections.Generic;

// 세이브 파일의 모양 그대로. JsonUtility가 이 클래스들을 그대로 적고 읽는다.
//
// 칸 이름을 바꾸면 옛 세이브가 그 칸을 잃는다. 새 칸은 "없던 시절의 세이브는 어떻게 읽히는가"를 함께 적는다.
// 칸마다 무엇을 채우고 되살리는지는 각 칸을 맡은 섹션(*SaveSection)에 있다.
[Serializable]
internal class SaveData
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

// 무기창고의 장비 한 점. 무기는 에셋 이름으로, 주인은 CharacterSO.Id로 가리킨다.
[Serializable]
internal class EquipmentRecord
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
internal class MaterialRecord
{
    public MaterialKind kind;
    public EquipmentGrade grade;
    public int count;
}

// 플레이어 본인의 것. 이름이 비어 있으면 기본 이름(PlayerAccount.DefaultName)을 쓴다.
[Serializable]
internal class AccountRecord
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
internal class PartyRecord
{
    public List<string> members = new List<string>();
}

[Serializable]
internal class PartyDeckRecord
{
    // 편성 화면에서 마지막으로 고른 파티(층에 들어갈 때 출전하는 파티).
    public int active;
    public List<PartyRecord> parties = new List<PartyRecord>();
}

// 시설 레벨 한 칸(FacilityLevels). 시설은 enum 순번이 아니라 이름으로 적는다 — 순번으로 적으면
// VillageBlockout.Kind에 항목이 끼어들 때 옛 세이브가 엉뚱한 시설을 가리킨다.
[Serializable]
internal class FacilityRecord
{
    public string kind;
    public int level;
}

[Serializable]
internal class CharacterRecord
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
