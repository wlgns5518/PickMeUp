// 세이브 파일의 한 칸을 맡는 쪽 — 지금 상태를 칸에 적고(WriteTo), 칸에서 상태를 되살린다(ReadFrom).
//
// 처음 쓰일 때 스스로 파일에서 읽어 오는 것들(무기창고, 재료, 지갑, 시설 레벨)이 이것을 구현한다.
// 새 칸을 늘릴 때 SaveSystem의 저장·삭제 흐름은 건드리지 않고, 섹션 하나를 만들어 목록에 얹으면 된다.
internal interface ISaveSection
{
    void WriteTo(SaveData data);

    /// 세이브가 없거나 깨졌으면 data는 null이다. 그때는 처음 상태로 되살린다.
    void ReadFrom(SaveData data);

    /// 파일이 지워졌다. 들고 있는 값을 놓아야 다음 저장이 옛 값을 되살려 놓지 않는다.
    void Forget();
}
