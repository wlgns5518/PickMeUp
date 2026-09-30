using System;
using System.IO;
using UnityEngine;

// 세이브 파일을 디스크에서 읽고 쓰는 일만 한다. 무엇을 적는지는 모른다.
internal static class SaveFile
{
    private const string FileName = "pickmeup_roster.json";

    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

    public static bool Exists => File.Exists(Path);

    /// 파일이 있고 제대로 읽혔으면 true. 없거나 깨졌으면 false(깨진 경우는 로그를 남긴다).
    public static bool TryRead(out SaveData data)
    {
        data = null;
        if (!Exists) return false;

        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 불러오기 실패: {e.Message}\n경로: {Path}");
            return false;
        }
        return data != null;
    }

    public static void Write(SaveData data)
    {
        try
        {
            File.WriteAllText(Path, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            // 저장 실패로 게임이 멈추면 안 되지만, 조용히 넘어가면 진행도가 사라진 걸 아무도 모른다.
            Debug.LogError($"[SaveSystem] 저장 실패: {e.Message}\n경로: {Path}");
        }
    }

    /// 파일에 이미 있는 것은 그대로 두고 한 칸만 갈아 끼운다. 파일이 없으면 새로 만든다 —
    /// 그때 적을 층 진행도(highestClearedForNewFile)는 부르는 쪽이 준다.
    public static void Patch(Action<SaveData> write, int highestClearedForNewFile)
    {
        SaveData data;
        if (Exists)
        {
            if (!TryRead(out data)) return; // 깨진 파일을 한 칸만 든 새 파일로 덮으면 남은 진행도까지 영영 사라진다.
        }
        else
        {
            // 스트레스 기준 시각은 0으로 둔다(기록 없음). 캐릭터 기록이 하나도 없는 파일이라 기준으로 삼을 값도 없다.
            data = new SaveData { highestClearedFloor = highestClearedForNewFile };
        }

        write(data);
        Write(data);
    }

    public static void Delete()
    {
        if (Exists) File.Delete(Path);
    }
}
