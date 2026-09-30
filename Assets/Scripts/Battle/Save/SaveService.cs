using System;
using System.Collections.Generic;
using UnityEngine;

// 저장·불러오기·삭제의 순서를 쥔 쪽. 칸마다 무엇을 적고 되살리는지는 각 섹션이, 디스크는 SaveFile이 맡는다.
//
// 저장할 상태는 생성자로 받는다(GameServices가 넘긴다). 예전에는 섹션들이 PlayerAccount·EquipmentInventory 같은
// 정적 클래스를 직접 불렀고, 그 정적 클래스들이 다시 SaveSystem을 불러 서로가 서로를 부르는 고리였다.
public sealed class SaveService
{
    private readonly CharacterSaveSection characters;
    private readonly PartySaveSection party;
    private readonly EquipmentSaveSection equipment;
    private readonly MaterialSaveSection materials;
    private readonly AccountSaveSection account;
    private readonly FacilitySaveSection facilities;

    // 처음 쓰일 때 스스로 파일에서 읽어 오고, 파일을 지우면 함께 잊는 칸들.
    private readonly ISaveSection[] selfLoading;

    private readonly FloorProgressStore floors;
    private readonly StressLedger stress;
    private readonly IStressClock clock;

    public SaveService(Wallet wallet, EquipmentStore armory, MaterialStore materialStore, FacilityLevelStore facilityLevels,
        PartyDeckStore deck, CharacterProgressStore progress, FallenRecord fallen, StressLedger stress,
        IStressClock clock, FloorProgressStore floors)
    {
        characters = new CharacterSaveSection(progress, fallen, stress);
        party = new PartySaveSection(deck);
        equipment = new EquipmentSaveSection(armory);
        materials = new MaterialSaveSection(materialStore);
        account = new AccountSaveSection(wallet);
        facilities = new FacilitySaveSection(facilityLevels);
        selfLoading = new ISaveSection[] { equipment, materials, account, facilities };

        this.floors = floors;
        this.stress = stress;
        this.clock = clock;
    }

    public string SavePath => SaveFile.Path;

    public bool HasSave => SaveFile.Exists;

    public void Save(IReadOnlyList<CharacterSO> roster)
    {
        if (roster == null) return;

        // 스트레스는 "정산 시각 기준의 값"으로 저장한다. 먼저 정산해 두지 않으면
        // 저장한 값에 이미 반영된 회복분을 다음 실행에서 한 번 더 빼게 된다.
        stress.Settle();

        var data = new SaveData
        {
            highestClearedFloor = floors.HighestCleared,
            stressStampUtcTicks = clock.StampTicks,
        };
        characters.WriteTo(data, roster);

        // 창고와 재료는 스스로 파일에서 읽어 온 뒤에 적는다. 창고를 한 번도 열지 않은 전투 씬에서
        // 저장해도 방금 덮어쓸 파일에 있던 장비와 재료가 그대로 실린다.
        for (int i = 0; i < selfLoading.Length; i++) selfLoading[i].WriteTo(data);
        party.WriteTo(data);

        SaveFile.Write(data);
    }

    // 아래 SaveX는 한 칸만 저장한다. 제작·장착·업그레이드는 마을에서 일어나 로스터 명단을 쥔 쪽이 없으므로,
    // 파일에 이미 있는 성장 기록은 그대로 두고 그 칸만 갈아 끼운다.

    public void SaveEquipment() => Patch(equipment.WriteTo);
    public void SaveMaterials() => Patch(materials.WriteTo);
    public void SaveAccount() => Patch(account.WriteTo);
    public void SaveParty() => Patch(party.WriteTo);
    public void SaveFacilities() => Patch(facilities.WriteTo);

    private void Patch(Action<SaveData> write) => SaveFile.Patch(write, floors.HighestCleared);

    // 아래 LoadX는 그 칸의 상태가 처음 쓰일 때 불린다(GameServices가 잇는다). 세이브가 없거나 깨졌으면 처음 상태로 시작한다.

    public void LoadEquipment() => equipment.ReadFrom(ReadOrNull());
    public void LoadMaterials() => materials.ReadFrom(ReadOrNull());
    public void LoadAccount() => account.ReadFrom(ReadOrNull());
    public void LoadFacilities() => facilities.ReadFrom(ReadOrNull());

    // 세이브의 편성을 얹는다. 세션마다 한 번, 보유 명단을 세운 뒤에 부른다(RosterBootstrap).
    public void LoadParty(IReadOnlyList<CharacterSO> owned) => party.ReadFrom(ReadOrNull(), owned);

    // 세이브가 없으면 false. 있으면 층 해금을 복원하고, 로스터가 주어지면 성장/영구 사망도 얹는다.
    // 메인 씬에는 로스터가 없으므로 층 해금만 읽는 호출도 허용한다.
    public bool Load(IReadOnlyList<CharacterSO> roster = null)
    {
        if (!SaveFile.TryRead(out SaveData data)) return false;

        floors.RestoreCleared(data.highestClearedFloor);
        // 스트레스 값을 얹기 전에 그 값들이 기준으로 삼는 시각부터 되돌린다.
        clock.RestoreStamp(data.stressStampUtcTicks);
        if (roster == null) return true;

        characters.ReadFrom(data, roster);
        return true;
    }

    public void Delete()
    {
        try
        {
            SaveFile.Delete();
            // 창고와 재료는 파일에서 스스로 읽어 온 값을 들고 있다. 파일이 사라졌는데 그대로 두면 다음 저장이 되살려 놓는다.
            for (int i = 0; i < selfLoading.Length; i++) selfLoading[i].Forget();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 삭제 실패: {e.Message}");
        }
    }

    private static SaveData ReadOrNull() => SaveFile.TryRead(out SaveData data) ? data : null;
}
