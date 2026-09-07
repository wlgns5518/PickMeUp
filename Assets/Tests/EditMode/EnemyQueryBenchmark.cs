using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

// 둘레 질의(EnemyWorldBridge.CountEnemiesAround 등)에 공간 색인을 씌우는 것이 이득인지 재는 자.
//
// 한 번 씌워 봤다가 되돌린 자리다. 격자를 세우는 데 O(N) 한 바퀴가 드는데, 실측한 질의
// 횟수가 프레임당 0.29~0.45회밖에 되지 않아 본전을 못 뽑았다. 적 1000마리 기준으로
// 격자 세우기가 +98 us/프레임인데 아끼는 것은 0.33 x 29 = 9.7 us였다.
//
// 그래서 여기서 재는 것은 "얼마나 빨라지는가"가 아니라 손익분기다:
//   질의 한 번의 비용 vs 색인을 세우는 데 드는 최소 비용(전체를 한 바퀴 도는 값).
// 프레임당 질의가 그 비율을 넘지 않는 한, 어떤 색인을 쓰든 손해다.
//
// 다시 시도한다면 색인을 새로 세우지 말고 ECS 쪽에 이미 있는 것을 읽어야 한다
// (EnemySimulationSystems의 EnemySpatialHashSystem — Burst 잡에서 매 프레임 세운다).
// 그쪽은 세우는 값을 이미 치렀으므로 손익분기가 0에 가까워진다.
//
// [Explicit]이라 평소 실행에는 끼지 않는다. 시간을 재는 것이라 값이 흔들리고 실패로
// 잡아야 할 성질도 아니다 — 숫자를 보고 싶을 때 이름을 지정해 돌린다.
public class EnemyQueryBenchmark
{
    private World world;
    private EntityManager manager;

    [SetUp]
    public void SetUp()
    {
        world = new World("EnemyQueryBenchmark");
        manager = world.EntityManager;

        EnemyWorldBridge.Initialize();
        EnemyWorldBridge.AllyStates.Clear();
        EnemyWorldBridge.EnemyStates.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        if (world != null && world.IsCreated) world.Dispose();
        EnemyWorldBridge.Dispose();
    }

    private void Fill(int count, float extent)
    {
        var random = new Unity.Mathematics.Random(20260907);
        for (int i = 0; i < count; i++)
        {
            Entity entity = manager.CreateEntity();
            EnemyWorldBridge.EnemyStates.Add(new EnemyWorldBridge.EnemyState
            {
                entity = entity,
                position = new float3(random.NextFloat(-extent, extent), 0f, random.NextFloat(-extent, extent)),
                forward = new float3(0f, 0f, 1f),
                radius = 0.5f,
                hp = 100,
                maxHp = 100,
                poise = 100f,
                threatWeight = 1f,
                targetAllyIndex = EnemyTarget.None,
                action = EnemyActionKind.Approach,
            });
        }

        EnemyWorldBridge.RebuildEnemyIndex();
    }

    [Test, Explicit]
    public void 공간_색인의_손익분기를_잰다()
    {
        // 실제 값이다. 마법사의 간격 유지가 2.6m다(UnitController.KeepDistanceThreshold).
        const float Radius = 2.6f;
        const int Calls = 20000;
        const int Runs = 2000;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("== 둘레 질의 vs 색인 세우기 (반경 " + Radius + "m) ==");
        sb.AppendLine();
        sb.AppendLine(" 적 수 |  질의 1회 |  전체 1바퀴 | 손익분기(질의/프레임)");
        sb.AppendLine("-------+-----------+-------------+----------------------");

        foreach (int enemies in new[] { 50, 200, 500, 1000, 2000 })
        {
            EnemyWorldBridge.EnemyStates.Clear();
            Fill(enemies, 60f);

            var probe = new Unity.Mathematics.Random(777);
            var points = new float3[256];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new float3(probe.NextFloat(-60f, 60f), 0f, probe.NextFloat(-60f, 60f));
            }

            int sink = 0;
            for (int i = 0; i < 2000; i++)
            {
                sink += EnemyWorldBridge.CountEnemiesAround(points[i % points.Length], Radius);
                EnemyWorldBridge.RebuildEnemyIndex();
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Calls; i++) sink += EnemyWorldBridge.CountEnemiesAround(points[i % points.Length], Radius);
            sw.Stop();
            double queryUs = sw.Elapsed.TotalMilliseconds * 1000.0 / Calls;

            // 어떤 색인이든 최소한 전체를 한 바퀴는 돌아야 세워진다. RebuildEnemyIndex가
            // 그 한 바퀴이므로, 색인을 얹었을 때 붙는 값의 하한으로 쓴다.
            sw.Restart();
            for (int i = 0; i < Runs; i++) EnemyWorldBridge.RebuildEnemyIndex();
            sw.Stop();
            double passUs = sw.Elapsed.TotalMilliseconds * 1000.0 / Runs;

            sb.AppendLine(string.Format("{0,6} | {1,6:F2} us | {2,8:F1} us | {3,15:F2}회 이상",
                enemies, queryUs, passUs, queryUs > 0 ? passUs / queryUs : 0));

            Assert.Greater(sink, int.MinValue);
        }

        sb.AppendLine();
        sb.AppendLine("실측한 게임 안의 질의 횟수는 0.29~0.45회/프레임(아군 2명)이다.");
        sb.AppendLine("손익분기를 한 자릿수 배 밑돌므로, 색인을 프레임마다 새로 세우면 손해다.");

        UnityEngine.Debug.Log(sb.ToString());
        Assert.Pass(sb.ToString());
    }
}
