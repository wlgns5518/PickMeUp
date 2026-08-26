using System;
using System.IO;
using UnityEngine;

/// 외부 서비스 API 키 로더. 키는 절대 씬/스크립트에 직렬화하지 않는다 — 커밋되기 때문.
/// 우선순위: 환경변수 → 프로젝트 루트의 Secrets/apikeys.json (gitignore 대상).
/// 빌드에서는 실행 파일 옆의 Secrets/apikeys.json 을 본다.
public static class ApiKeys
{
    [Serializable]
    private class KeyFile
    {
        public string meshy;
        public string gemini;
    }

    private const string FileRelativePath = "../Secrets/apikeys.json";

    private static KeyFile cache;
    private static bool loaded;

    /// Meshy (text-to-image) 키. 없으면 빈 문자열.
    public static string Meshy => Get(Environment.GetEnvironmentVariable("MESHY_API_KEY"), f => f.meshy);

    /// Gemini (이름 생성) 키. 없으면 빈 문자열.
    public static string Gemini => Get(Environment.GetEnvironmentVariable("GEMINI_API_KEY"), f => f.gemini);

    /// 파일을 고쳐도 에디터 재시작 없이 다시 읽게 한다.
    public static void Reload() { loaded = false; cache = null; }

    /// 파일 경로 — 어디에 키를 넣어야 하는지 로그로 안내할 때 쓴다.
    public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, FileRelativePath));

    private static string Get(string fromEnv, Func<KeyFile, string> pick)
    {
        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv.Trim();

        KeyFile file = Load();
        if (file == null) return "";

        string value = pick(file);
        return string.IsNullOrEmpty(value) ? "" : value.Trim();
    }

    private static KeyFile Load()
    {
        if (loaded) return cache;
        loaded = true;

        string path = FilePath;
        if (!File.Exists(path)) return cache = null;

        try
        {
            cache = JsonUtility.FromJson<KeyFile>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ApiKeys] {path} 파싱 실패: {e.Message}");
            cache = null;
        }
        return cache;
    }
}
