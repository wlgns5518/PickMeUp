using UnityEngine;

// 이동 클립의 재생 배속을 실제 이동 속도에 맞춰 Animator의 float 파라미터로 넘긴다.
//
// 클립이 원래 나아가는 속도와 실제 이동 속도가 어긋나면 발이 땅에서 미끄러진다.
// 민첩과 직업 배율로 이동 속도가 캐릭터마다 달라지므로(달리기 4~6m/s) 고정 배속으로는 맞출 수 없다.
// 그래서 Animator의 float 파라미터로 배속을 넘기고, Run/Walk 상태가 그 값을 곱해 재생한다.
//
// 한때 여기서 MoveMultiplier(공포/둔화)를 곱했다. 그 시절에는 부르는 쪽이 요청 속도를
// 넘겼기 때문인데, 나중에 실측 속도를 넘기는 경로가 생기면서 배율이 두 번 곱해졌다.
// 게다가 상태마다 넘기는 기준이 달라져(Chase는 실측, Move는 요청, Evade는 실측) 같은
// 달리기인데도 상태가 바뀔 때마다 재생 속도가 달라졌다 — 그게 버벅임의 정체다.
//
// 지금은 규칙이 하나다: 배속은 언제나 실제 이동 속도에서 나온다.
// 공포와 둔화는 이미 그 속도 안에 들어 있으므로(agent.speed에 곱해져 있다) 여기서 또 곱하지 않는다.
public sealed class MoveAnimationSpeed
{
    private readonly Animator animator;
    private readonly int parameterHash;
    private readonly bool hasParameter;

    // 마지막으로 배속 기준으로 삼은 클립 속도. 기준이 바뀌는 순간을 잡아내 damping을 건너뛴다.
    private float lastClipSpeed = -1f;

    public MoveAnimationSpeed(Animator animator, string parameterName)
    {
        this.animator = animator;
        if (animator == null || string.IsNullOrEmpty(parameterName)) return;

        parameterHash = Animator.StringToHash(parameterName);
        // 파라미터가 없는 컨트롤러에 SetFloat을 부르면 매 프레임 경고가 쌓인다. 먼저 확인한다.
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Float) continue;
            if (parameter.nameHash != parameterHash) continue;

            hasParameter = true;
            return;
        }
    }

    /// groundSpeed는 "지금 실제로 땅 위를 나아가는 속도"다. 요청 속도가 아니다.
    /// dampTime만큼 늦게 따라가게 해서 실제 속도가 매 프레임 출렁여도 재생 속도는 그 출렁임을 그대로 받지 않는다.
    public void Apply(float groundSpeed, float clipSpeed, Vector2 multiplierRange, float dampTime)
    {
        if (!hasParameter || animator == null) return;

        float multiplier = Mathf.Clamp(groundSpeed / Mathf.Max(0.1f, clipSpeed), multiplierRange.x, multiplierRange.y);

        // 기준 클립이 바뀌면(달리기↔걷기↔뒷걸음) 같은 배속 숫자가 다른 뜻을 갖는다.
        // 그 경계를 damping으로 이어붙이면 새 클립이 한동안 엉뚱한 배속으로 돈다 — 그때만 즉시 맞춘다.
        bool clipChanged = !Mathf.Approximately(clipSpeed, lastClipSpeed);
        lastClipSpeed = clipSpeed;

        if (clipChanged || dampTime <= 0f)
        {
            animator.SetFloat(parameterHash, multiplier);
            return;
        }

        // 크게 벌어졌을 때 감쇠를 건너뛰고 바로 맞추는 안을 실측했다가 걷어냈다.
        // 출발·정지 구간에서 배속이 실제 속도를 뒤따라가는 것이 눈에 걸린다고 봤는데,
        // 켜고 끄며 45초씩 두 바퀴 재 보니 차이가 없었다 — 평균 오차 0.557 대 0.540으로
        // 실행 간 편차 안이고, 1m/s 초과 비율은 11.0% 대 11.2%로 사실상 같았다.
        // 이 구간은 보행 선택(ShouldRunAtGroundSpeed)이 클립을 바꾸면서 이미 즉시 맞춰진다.
        animator.SetFloat(parameterHash, multiplier, dampTime, Time.deltaTime);
    }
}
