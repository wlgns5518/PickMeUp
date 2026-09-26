// 테스트용 임시 손잡이. 밸런스 값이 아니다 — 확인이 끝나면 1로 되돌린다.
//
// 양 진영 체력에 같은 배율을 곱해 전투를 오래 끌어, 애니메이션·전투 흐름을 눈으로 볼 시간을 번다.
// 한 번은 인스펙터 필드(debugHealthMultiplier)로 뒀다가 씬에 100으로 저장돼 기본값을 바꿔도
// 돌아오지 않는 일이 있었다. 그래서 씬에 남지 않는 상수로 둔다 — 여기 한 줄만 고치면 끝난다.
public static class BattleTestTuning
{
    // 아군(CharacterBattleSpawner.MapStats)과 적(EnemyHordeSpawner.BuildStats)의 최대 체력 배율.
    public const int HealthMultiplier = 100;
}
