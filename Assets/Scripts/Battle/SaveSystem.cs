using System.Collections.Generic;

// 캐릭터 성장과 영구 사망을 파일로 남긴다.
//
// 영구 죽음은 세션을 넘어 유지될 때에야 무게를 가진다. 사망 기록이 런타임 컬렉션뿐이던 때는
// 플레이를 멈추면 사라졌고, 결과적으로 "영구"라는 말이 실제로는 성립하지 않았다.
//
// 로스터 상태와 층 해금 상태, 무기창고, 모아 둔 제작 재료, 플레이어 이름과 재화, 파티 편성,
// 시설 레벨을 함께 남긴다. 캐릭터 식별은 CharacterSO.Id를 쓰고, 옛 세이브는 에셋 이름으로 되짚는다.
//
// 이 클래스는 입구일 뿐이다. 실제 일은 GameServices가 조립한 SaveService가 한다 — 파일 모양은 SaveData,
// 디스크는 SaveFile, 칸마다의 읽고 쓰기는 Save/*SaveSection. 컴포넌트와 테스트는 여기로 부르고,
// 생성자로 의존성을 받을 수 있는 코드는 SaveService를 직접 받는다.
public static class SaveSystem
{
    private static SaveService Service => GameServices.Saves;

    public static string SavePath => Service.SavePath;

    public static bool HasSave => Service.HasSave;

    public static void Save(IReadOnlyList<CharacterSO> roster) => Service.Save(roster);

    public static void SaveEquipment() => Service.SaveEquipment();
    public static void SaveMaterials() => Service.SaveMaterials();
    public static void SaveAccount() => Service.SaveAccount();
    public static void SaveParty() => Service.SaveParty();
    public static void SaveFacilities() => Service.SaveFacilities();

    public static void LoadEquipment() => Service.LoadEquipment();
    public static void LoadMaterials() => Service.LoadMaterials();
    public static void LoadAccount() => Service.LoadAccount();
    public static void LoadFacilities() => Service.LoadFacilities();
    public static void LoadParty(IReadOnlyList<CharacterSO> owned) => Service.LoadParty(owned);

    public static bool Load(IReadOnlyList<CharacterSO> roster = null) => Service.Load(roster);

    public static void Delete() => Service.Delete();
}
