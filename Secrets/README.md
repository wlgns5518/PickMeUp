# Secrets

API 키를 두는 곳. `apikeys.json` 은 **커밋되지 않는다** (`.gitignore`).

1. `apikeys.example.json` 을 `apikeys.json` 으로 복사
2. 각자 발급받은 키를 채운다

읽는 쪽은 `Assets/Scripts/Core/ApiKeys.cs`. 우선순위는

1. 환경변수 `MESHY_API_KEY` / `GEMINI_API_KEY`
2. 이 폴더의 `apikeys.json`

빌드에서는 실행 파일 옆의 `Secrets/apikeys.json` 을 본다.
