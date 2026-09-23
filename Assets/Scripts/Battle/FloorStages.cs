using UnityEngine;

// 탑의 구간(다섯 층)마다의 이름과 인상색. 층 선택 화면이 탑을 그릴 때 쓴다.
//
// 이름은 그 구간의 전투 맵 이름과 같다 — 맵 테마 표(BattleMapThemes, 에디터 전용)도 이름은 여기서 읽는다.
// 색은 그 맵의 바닥·안개·빛에서 뽑은 한 가지 색이다. 화면은 이 색을 어둡게 눌러 구간 띠를 칠하고, 밝게 올려 글자에 쓴다.
// 탑을 오를수록 거칠고 어두워지다가 꼭대기에서 다시 밝아지는 맵의 흐름이 색에도 그대로 드러난다.
public static class FloorStages
{
    private struct Stage
    {
        public readonly string Title;
        public readonly Color Color;

        public Stage(string title, uint rgb)
        {
            Title = title;
            Color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }

    // 1~5층부터 다섯 층씩.
    private static readonly Stage[] Stages =
    {
        new Stage("푸른 평야", 0x7FB069),
        new Stage("붉은 암석 협곡", 0xD9825B),
        new Stage("안개 낀 침엽수 숲", 0x7FA89A),
        new Stage("작열하는 모래 언덕", 0xE8C170),
        new Stage("눈 덮인 산 분지", 0xA9C3E6),
        new Stage("썩은 늪", 0x8A9A5B),
        new Stage("불타는 분화구", 0xE0552F),
        new Stage("노을 진 메사 고원", 0xE08A6E),
        new Stage("단풍 든 구릉", 0xE39A45),
        new Stage("고요한 호숫가", 0x7FB3E0),
        new Stage("잿빛 고사목 황무지", 0x9A958F),
        new Stage("얼어붙은 빙원", 0x8FC1F0),
        new Stage("달빛 숲", 0x6F86C9),
        new Stage("모래바람 붉은 황야", 0xD37447),
        new Stage("구름 위의 능선", 0xC9DBF5),
        new Stage("폭풍우 치는 고원", 0x6F8499),
        new Stage("보랏빛 황혼 사구", 0xA77BD1),
        new Stage("핏빛 달의 칼데라", 0xC23B35),
        new Stage("별빛 벼랑", 0x5C6FC2),
        new Stage("탑의 정상", 0xF2D48A),
    };

    // 탑의 구간 수(100층이면 20).
    public static int Count =>
        (FloorProgress.LastFloor - FloorProgress.FirstFloor) / FloorProgress.FloorsPerStage + 1;

    // 층이 몇 번째 구간인지(0부터). 7층이면 1.
    public static int IndexOf(int floor) =>
        (FloorProgress.StageFirstFloor(floor) - FloorProgress.FirstFloor) / FloorProgress.FloorsPerStage;

    // 구간의 첫 층과 마지막 층.
    public static int FirstFloorOf(int stage) => FloorProgress.FirstFloor + stage * FloorProgress.FloorsPerStage;
    public static int LastFloorOf(int stage) =>
        Mathf.Min(FirstFloorOf(stage) + FloorProgress.FloorsPerStage - 1, FloorProgress.LastFloor);

    public static string TitleOfStage(int stage) => Get(stage).Title;
    public static Color ColorOfStage(int stage) => Get(stage).Color;

    public static string TitleOf(int floor) => TitleOfStage(IndexOf(floor));
    public static Color ColorOf(int floor) => ColorOfStage(IndexOf(floor));

    // 표보다 탑이 높아지면 마지막 줄을 되풀이한다. 이름이 겹칠 뿐 화면이 깨지지는 않는다.
    private static Stage Get(int stage) => Stages[Mathf.Clamp(stage, 0, Stages.Length - 1)];
}
