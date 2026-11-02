using UnityEngine;

// 11층부터 전장에 세우는 건물·지형지물 목록(BattleTerrain이 Resources에서 읽는다).
//
// 새로 만든 모델은 없다 — 마을에 쓰는 것들을 그대로 가져온다. 성벽·망루는 마을 성벽 칸(Wall_Segment·Wall_Tower,
// 원작 색을 입힌 Meshy 파츠), 집은 마을 거리를 채운 Gaia 3DForge 집, 바리케이드는 Gaia 수레·장작·울타리와
// Meshy 상자, 폐허 기둥은 Meshy 키트다.
//
// Gaia 에셋은 저장소 밖이다(Assets/Procedural Worlds, .gitignore). Gaia가 없는 체크아웃에서는 그 칸이 비어 있고,
// BattleTerrain은 빈 칸을 건너뛰어 Meshy 파츠만으로 세운다.
[CreateAssetMenu(menuName = "PickMeUp/Battle Building Set", fileName = "BattleBuildingSet")]
public class BattleBuildingSet : ScriptableObject
{
    public const string ResourcePath = "BattleBuildingSet";

    [Tooltip("성벽 한 칸. 긴 쪽이 로컬 X다.")]
    public GameObject wallSegment;

    [Tooltip("성벽 이음새의 망루.")]
    public GameObject wallTower;

    [Tooltip("마을 성벽은 거점(지름 250m)에 맞춰 높이 16m로 만들어져 있다. 사람(1.8m) 옆에 세울 배율.")]
    [Min(0.05f)] public float wallScale = 0.35f;

    [Tooltip("평지용 집(바닥이 y=0인 B·C 변형). 긴 쪽이 로컬 X다.")]
    public GameObject[] houses;

    [Tooltip("허리 높이 바리케이드 — 수레·장작더미·상자·마구간. 길을 막되 카메라를 가리지 않는다.")]
    public GameObject[] barricades;

    [Tooltip("울타리 한 칸(긴 쪽이 로컬 Z).")]
    public GameObject fence;

    [Tooltip("무너진 기둥·오벨리스크. 혼자 서서 등을 기대는 자리가 된다.")]
    public GameObject[] pillars;

    [Tooltip("불 피운 화로. 길을 막지 않는 장식이다.")]
    public GameObject[] lights;
}
