using System;
using System.Collections.Generic;

// 무기창고 — 만든 장비 전부와 누가 무엇을 들었는지. 장비창·제작소·전투 장착이 이것으로 본다.
public interface IEquipmentStore
{
    IReadOnlyList<OwnedEquipment> Items { get; }
    OwnedEquipment Add(WeaponDefinition weapon, EquipmentGrade grade);
    bool Contains(OwnedEquipment item);
    void Remove(OwnedEquipment item);
    OwnedEquipment EquippedIn(CharacterSO character, EquipSlot slot);

    /// 들 수 없으면 false와 그 이유를 돌려준다 — 부르는 쪽이 그대로 화면에 띄운다.
    bool Equip(CharacterSO character, OwnedEquipment item, out string reason);
    void Unequip(OwnedEquipment item);
    void UnequipAll(CharacterSO character);

    event Action Changed;
}

// 장비는 만들어지는 순간 여기로 들어오고, 무기창고 창에서 영웅에게 들린다. 해제하면 다시 창고로 돌아온다.
// 캐릭터 에셋(CharacterSO.mainHandWeapon)은 건드리지 않는다 — 에셋은 시작값 템플릿이고,
// 굴러가는 값은 런타임에 둔다는 규칙(CharacterProgress 주석 참조)을 장비에도 그대로 따른다.
// 에셋에 적힌 무기는 "기본 장비"로 남아, 제작 장비를 내려놓으면 다시 그것을 든다(CharacterLoadout).
//
// 바뀔 때마다 저장을 부탁한다(Committed). 제작과 장착은 마을에서 일어나는데, 마을에는 전투 정산 같은
// 저장 시점이 없어서 기다렸다가 몰아 저장하면 게임을 끄는 순간 방금 만든 칼이 사라진다.
public sealed class EquipmentStore : PersistentState, IEquipmentStore
{
    private readonly List<OwnedEquipment> items = new List<OwnedEquipment>();

    // 창고 내용이나 누가 무엇을 들었는지가 바뀌었다. 무기창고 창이 다시 그린다.
    public event Action Changed;

    public IReadOnlyList<OwnedEquipment> Items
    {
        get
        {
            EnsureLoaded();
            return items;
        }
    }

    // ---- 들어오고 나가기 ---------------------------------------------------

    // 제작소가 방금 만든 장비를 넣는다. 처음에는 아무도 들고 있지 않다.
    public OwnedEquipment Add(WeaponDefinition weapon, EquipmentGrade grade)
    {
        if (weapon == null) return null;
        EnsureLoaded();

        var item = new OwnedEquipment(weapon, grade);
        items.Add(item);
        Commit();
        return item;
    }

    public bool Contains(OwnedEquipment item)
    {
        if (item == null) return false;
        EnsureLoaded();
        return items.Contains(item);
    }

    // 창고에서 영영 뺀다(강화 중 파괴). 들고 있던 영웅은 기본 장비로 돌아간다 — 주인은 장비 쪽에만 적혀 있어
    // 장비가 사라지면 따로 풀어 줄 것이 없다.
    public void Remove(OwnedEquipment item)
    {
        if (item == null) return;
        EnsureLoaded();
        if (items.Remove(item)) Commit();
    }

    // 재료 장비를 빼고 결과 장비 한 점을 넣는다(장비 합성). 저장과 알림을 한 번만 하려고 한 번에 한다 —
    // 따로 하면 창고 창이 "재료만 사라진" 중간 상태를 한 번 그린다.
    public OwnedEquipment ReplaceWith(IReadOnlyList<OwnedEquipment> consumed, WeaponDefinition weapon, EquipmentGrade grade)
    {
        if (weapon == null) return null;
        EnsureLoaded();

        for (int i = 0; i < consumed.Count; i++) items.Remove(consumed[i]);

        var item = new OwnedEquipment(weapon, grade);
        items.Add(item);
        Commit();
        return item;
    }

    // 강화 단계를 바꾼다. 확률과 비용은 EquipmentEnhancement가 따진다.
    public void SetLevel(OwnedEquipment item, int level)
    {
        if (item == null) return;
        EnsureLoaded();

        item.Level = EquipmentEnhancement.ClampLevel(level);
        Commit();
    }

    // ---- 장착 ------------------------------------------------------------

    public OwnedEquipment EquippedIn(CharacterSO character, EquipSlot slot)
    {
        if (character == null) return null;
        EnsureLoaded();

        string id = character.Id;
        for (int i = 0; i < items.Count; i++)
        {
            OwnedEquipment item = items[i];
            if (item.OwnerId == id && item.Slot == slot) return item;
        }
        return null;
    }

    /// 장비를 캐릭터에게 들린다. 다른 영웅이 들고 있던 것이면 그 손에서 가져온다.
    /// 같은 자리에 이미 든 제작 장비는 창고로 돌아간다. 두손 무기를 들면 방패도 함께 내려놓는다.
    public bool Equip(CharacterSO character, OwnedEquipment item, out string reason)
    {
        reason = null;
        if (character == null || item == null || item.Weapon == null)
        {
            reason = "장착할 장비나 영웅이 없습니다.";
            return false;
        }

        EnsureLoaded();
        if (!items.Contains(item))
        {
            reason = "무기창고에 없는 장비입니다.";
            return false;
        }

        if (item.IsHeldBy(character)) return true;

        // 직업은 화면에 드러내지 않는다(초상화로만 짐작한다). 거절 문구에도 직업 이름을 적지 않는다.
        if (!CharacterLoadout.CanEquip(character))
        {
            reason = "이 영웅은 장비를 들지 않습니다.";
            return false;
        }

        if (item.Slot == EquipSlot.OffHand)
        {
            // 방패를 들 손이 남아 있는지는 지금 들고 있는 주무기가 정한다.
            if (!CharacterLoadout.CanHoldShield(character))
            {
                reason = "지금 든 무기로는 방패를 들 수 없습니다.";
                return false;
            }
        }
        else if (item.Weapon.IsTwoHanded)
        {
            ReleaseSlot(character, EquipSlot.OffHand);
        }

        ReleaseSlot(character, item.Slot);
        item.OwnerId = character.Id;
        Commit();
        return true;
    }

    public void Unequip(OwnedEquipment item)
    {
        if (item == null || !item.IsEquipped) return;
        EnsureLoaded();

        item.OwnerId = null;
        Commit();
    }

    /// 이 캐릭터가 든 제작 장비를 전부 창고로 돌린다. 합성 재료로 사라지는 영웅이 칼을 들고 가지 않게.
    public void UnequipAll(CharacterSO character)
    {
        if (character == null) return;
        EnsureLoaded();

        bool changed = ReleaseSlot(character, EquipSlot.MainHand);
        changed |= ReleaseSlot(character, EquipSlot.OffHand);
        if (changed) Commit();
    }

    private bool ReleaseSlot(CharacterSO character, EquipSlot slot)
    {
        bool changed = false;
        string id = character.Id;
        for (int i = 0; i < items.Count; i++)
        {
            OwnedEquipment held = items[i];
            if (held.OwnerId != id || held.Slot != slot) continue;

            held.OwnerId = null;
            changed = true;
        }
        return changed;
    }

    private void Commit()
    {
        RaiseCommitted();
        Changed?.Invoke();
    }

    // ---- 세이브 연동 ------------------------------------------------------

    // 세이브에서 읽어 온 창고를 그대로 얹는다. 저장을 다시 부르지 않는다.
    public void Restore(IEnumerable<OwnedEquipment> restored)
    {
        MarkLoaded();
        items.Clear();
        if (restored != null)
        {
            foreach (OwnedEquipment item in restored)
            {
                if (item != null && item.Weapon != null) items.Add(item);
            }
        }
        Changed?.Invoke();
    }

    // 세이브 파일이 지워졌다. 들고 있던 값을 버리고, 다음에 쓰일 때 파일에서 다시 읽는다.
    public void Forget()
    {
        items.Clear();
        MarkUnloaded();
    }
}
