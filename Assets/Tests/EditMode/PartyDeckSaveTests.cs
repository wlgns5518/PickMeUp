using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// 파티 편성이 세이브에 남고 다시 읽힌다는 것. 예전에는 런타임 목록뿐이라 게임을 껐다 켜면 세 파티가 비었다.
public class PartyDeckSaveTests
{
    private string backup;
    private bool hadSave;
    private readonly List<CharacterSO> heroes = new List<CharacterSO>();

    [SetUp]
    public void SetUp()
    {
        hadSave = SaveSystem.HasSave;
        backup = hadSave ? File.ReadAllText(SaveSystem.SavePath) : null;
        SaveSystem.Delete();
        // 2·3파티는 훈련소 레벨로 연다. 여기서는 저장만 보므로 끝까지 열어 둔다(LockedPartyTests).
        FacilityTesting.UnlockAll();

        for (int i = 0; i < 4; i++)
        {
            CharacterSO hero = ScriptableObject.CreateInstance<CharacterSO>();
            hero.name = "PartyDeckSaveTests_Hero" + i;
            hero.EnsureId();
            heroes.Add(hero);
        }

        // 빈 세이브에서 읽어 편성을 비운 채로 시작한다(읽어야 저장도 한다).
        SaveSystem.LoadParty(heroes);
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.Delete();
        // 메모리에 남은 편성도 비운다. 이 테스트의 캐릭터는 곧 사라지는 임시 인스턴스다.
        SaveSystem.LoadParty(heroes);
        if (hadSave && backup != null) File.WriteAllText(SaveSystem.SavePath, backup);

        foreach (CharacterSO hero in heroes) Object.DestroyImmediate(hero);
        heroes.Clear();
    }

    private static void WriteParty(int active, params string[][] parties)
    {
        var json = new System.Text.StringBuilder();
        json.Append("{\"highestClearedFloor\":0,\"characters\":[],\"party\":{\"active\":").Append(active).Append(",\"parties\":[");
        for (int p = 0; p < parties.Length; p++)
        {
            if (p > 0) json.Append(',');
            json.Append("{\"members\":[");
            for (int m = 0; m < parties[p].Length; m++)
            {
                if (m > 0) json.Append(',');
                json.Append('"').Append(parties[p][m]).Append('"');
            }
            json.Append("]}");
        }
        json.Append("]}}");
        File.WriteAllText(SaveSystem.SavePath, json.ToString());
    }

    [Test]
    public void 편성을_바꾸면_그_자리에서_세이브에_남는다()
    {
        PartyDeck.SetActive(1);
        PartyDeck.Add(heroes[0]);
        PartyDeck.Add(heroes[2]);

        Assert.IsTrue(SaveSystem.HasSave, "편성을 바꾸는 순간 저장해야 한다");
        string saved = File.ReadAllText(SaveSystem.SavePath);
        StringAssert.Contains(heroes[0].Id, saved);
        StringAssert.Contains(heroes[2].Id, saved);
        StringAssert.Contains("\"active\": 1", saved, "고른 파티도 남아야 한다");
    }

    [Test]
    public void 세이브의_편성을_세_파티와_고른_파티까지_되살린다()
    {
        WriteParty(2,
            new[] { heroes[0].Id },
            new string[0],
            new[] { heroes[3].Id, heroes[1].Id });

        SaveSystem.LoadParty(heroes);

        Assert.AreEqual(2, PartyDeck.ActiveIndex);
        CollectionAssert.AreEqual(new[] { heroes[0] }, PartyDeck.Party(0));
        Assert.AreEqual(0, PartyDeck.CountOf(1));
        CollectionAssert.AreEqual(new[] { heroes[3], heroes[1] }, PartyDeck.Party(2), "고른 순서(스폰 순서)도 그대로여야 한다");
    }

    [Test]
    public void 없는_캐릭터와_두_파티에_걸친_캐릭터는_걸러_낸다()
    {
        // 합성 재료로 사라진 사람, 그리고 어쩌다 두 파티에 함께 적힌 사람(한 사람은 한 파티에만).
        WriteParty(0,
            new[] { "사라진_캐릭터", heroes[0].Id },
            new[] { heroes[0].Id, heroes[1].Id });

        SaveSystem.LoadParty(heroes);

        CollectionAssert.AreEqual(new[] { heroes[0] }, PartyDeck.Party(0));
        CollectionAssert.AreEqual(new[] { heroes[1] }, PartyDeck.Party(1), "먼저 든 파티에만 남는다");
    }

    [Test]
    public void 편성이_없던_시절의_세이브는_빈_편성으로_읽는다()
    {
        File.WriteAllText(SaveSystem.SavePath, "{\"highestClearedFloor\":3,\"characters\":[]}");

        SaveSystem.LoadParty(heroes);

        for (int p = 0; p < PartyDeck.PartyCount; p++) Assert.AreEqual(0, PartyDeck.CountOf(p));
        Assert.AreEqual(0, PartyDeck.ActiveIndex);
    }

    [Test]
    public void 전체_저장은_파일의_편성을_지우지_않는다()
    {
        // 전투 정산·합성은 세이브를 통째로 새로 쓴다(SaveSystem.Save). 그때 편성 칸이 빠지면 저장한 편성이 사라진다.
        PartyDeck.Add(heroes[1]);

        SaveSystem.Save(new List<CharacterSO>());
        SaveSystem.LoadParty(heroes);

        CollectionAssert.AreEqual(new[] { heroes[1] }, PartyDeck.Party(0));
    }
}
