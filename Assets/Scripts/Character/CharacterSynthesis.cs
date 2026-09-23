// 캐릭터 합성 — 베이스 영웅이 재료 영웅을 태워 스킬 하나를 배운다.
//
// 이 게임의 영웅은 한 명 한 명이 고유한 개체라 같은 영웅이 두 번 나오지 않는다. 그래서 흔한 "중복 카드
// 합치기"가 아니라, 다른 영웅 한 명을 재료로 바쳐 베이스 영웅을 강하게 만드는 방식이다. 베이스의 레벨과
// 스탯은 그대로이고 스킬만 하나 늘어난다. 어떤 스킬이 나올지는 베이스의 직업과 이미 배운 스킬만 본다 —
// 재료가 몇 성인지는 보지 않는다(SkillCatalog.Roll).
//
// 재료 영웅은 보유 명단에서 사라진다. 들고 있던 제작 장비는 창고로 돌아간다(OwnedRoster.Remove).
// 예전에는 이 규칙이 합성소 창 안에 있었다. 창이 새로 지어져도 규칙은 그대로여야 해서 따로 뺐다.
public static class CharacterSynthesis
{
    public static long Cost(CharacterSO material) =>
        material == null ? 0 : GameEconomy.CharacterSynthesisGold;

    // 골드를 보기 전의 조건. 안 되면 화면에 그대로 띄울 이유를 돌려준다.
    public static bool CanSynthesize(CharacterSO main, CharacterSO material, out string reason)
    {
        reason = null;
        if (main == null || material == null)
        {
            reason = "베이스 영웅과 재료 영웅을 모두 고르세요.";
            return false;
        }
        if (main == material)
        {
            reason = "같은 영웅을 베이스와 재료로 함께 쓸 수 없습니다.";
            return false;
        }
        if (!OwnedRoster.Contains(main) || !OwnedRoster.Contains(material))
        {
            reason = "보유하지 않은 영웅입니다.";
            return false;
        }
        if (main.IsSkillFull)
        {
            reason = $"스킬은 최대 {SkillCatalog.MaxSkillsPerCharacter}개까지 배울 수 있습니다.";
            return false;
        }
        if (!SkillCatalog.HasCandidate(main))
        {
            reason = "이 영웅이 더 배울 스킬이 없습니다.";
            return false;
        }
        return true;
    }

    /// 골드를 내고 합성한다. 못 하면 false와 이유 — 그때는 골드도 영웅도 그대로다.
    public static bool TrySynthesize(CharacterSO main, CharacterSO material, out string skillId, out string reason)
    {
        skillId = null;
        if (!CanSynthesize(main, material, out reason)) return false;

        string rolled = SkillCatalog.Roll(main);
        if (string.IsNullOrEmpty(rolled))
        {
            reason = "이 영웅이 더 배울 스킬이 없습니다.";
            return false;
        }

        if (!PlayerAccount.TrySpend(Currency.Gold, Cost(material)))
        {
            reason = "골드가 부족합니다.";
            return false;
        }

        if (!main.LearnSkill(rolled))
        {
            // 조건은 위에서 다 봤으니 여기로 오면 규칙이 어긋난 것이다. 낸 골드는 돌려준다.
            PlayerAccount.Add(Currency.Gold, Cost(material));
            reason = "합성에 실패했습니다.";
            return false;
        }

        OwnedRoster.Remove(material);

        // 배운 스킬은 예전에는 다음 전투 정산에서야 파일에 적혔다. 골드는 곧바로 저장되는데 스킬만 늦으면
        // 그 사이 게임을 끄는 순간 골드만 내고 스킬은 잃는다. 여기서 바로 남긴다.
        SaveSystem.Save(OwnedRoster.Members);

        skillId = rolled;
        return true;
    }
}
