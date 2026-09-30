using UnityEngine;
using UnityEngine.AI;

public partial class UnitController
{
    // 스킬을 쓰는 동안 상대의 목에 매달리는 부품(clingToNeckDuringSkill).
    //
    // 이 동안만 NavMeshAgent를 통째로 끈다. 목은 땅에서 1.4m 위에 있어서 NavMesh 위의
    // 어떤 좌표로도 닿을 수 없고, 회피는 두 몸을 계속 떼어 놓으려 하기 때문이다 —
    // 붙어 있어야 하는 동작에 "붙지 못하게 하는 것"이 둘이나 걸려 있는 셈이다.
    //
    // 대신 매 프레임 상대의 목뼈를 따라간다. 물린 쪽이 끌려다니거나 몸을 돌려도 그대로
    // 붙어 있는 것은 이것 덕분이다(경직이 풀린 뒤에도 남은 시간 동안 매달려 있는다).
    //
    // 끝나면 반드시 NavMesh 위로 되돌려 놓아야 한다 — 공중에 뜬 좌표에서 에이전트를 다시
    // 켜면 그 자리에서 굳거나 엉뚱한 곳으로 튄다. End가 그 일을 한다.
    private sealed class ClingPart
    {
        private readonly UnitController u;

        private bool clinging;
        private UnitController victim;
        private Vector3 returnPoint;

        public ClingPart(UnitController owner)
        {
            u = owner;
        }

        public void Begin()
        {
            if (!u.clingToNeckDuringSkill) return;
            if (!u.IsTargetValid()) return;
            if (clinging) return;

            victim = u.CurrentTarget.Unit;
            clinging = true;
            // 달라붙는 동안은 이쪽이 위치를 정한다. 되돌릴 좌표는 지금 서 있는 자리다 —
            // 여기는 방금까지 걸어온 곳이라 반드시 NavMesh 위다.
            returnPoint = u.transform.position;

            NavMeshAgent agent = u.agent;
            if (agent != null && agent.enabled)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.enabled = false;
            }
        }

        // SkillBehavior가 매 프레임 부른다. progress는 스킬 모션의 진행도(0~1).
        public void Update(float progress)
        {
            if (!clinging) return;

            // 물고 있던 상대가 죽거나 사라졌다. 허공을 물고 매달려 있을 이유가 없다.
            if (victim == null || victim.IsDead)
            {
                End();
                return;
            }

            Transform transform = u.transform;
            Vector3 neck = victim.NeckPoint;

            // 상대의 어느 쪽에 매달릴지는 물기 시작할 때 서 있던 방향 그대로다.
            Vector3 side = transform.position - victim.transform.position;
            side.y = 0f;
            if (side.sqrMagnitude <= 0.0001f) side = -victim.transform.forward;
            side.Normalize();

            // 루트가 아니라 입이 목에 닿아야 한다. 이 유닛의 루트에서 입까지의 높이만큼 내려 잡는다.
            Vector3 anchor = neck + side * u.clingDistance - Vector3.up * u.clingMouthHeight;

            // 덤벼든 자리에서 목까지는 순간이동이 아니라 짧게 당겨 붙는다. 그 사이가
            // "물었다"로 읽히는 구간이라, 0으로 두면 이가 닿기도 전에 이미 붙어 있다.
            float snap = u.clingSnapTime > 0f ? Mathf.Clamp01(progress / u.clingSnapTime) : 1f;
            transform.position = Vector3.Lerp(transform.position, anchor, snap);

            // 무는 내내 상대를 마주 본다.
            transform.rotation = Quaternion.LookRotation(-side, Vector3.up);
        }

        public void End()
        {
            if (!clinging) return;

            clinging = false;
            victim = null;

            NavMeshAgent agent = u.agent;
            if (agent == null || agent.enabled) return;

            // 뜬 좌표에서 그대로 켜면 에이전트가 NavMesh를 못 찾는다. 먼저 발 디딜 자리를
            // 찾아 내려놓고 켠다 — 물고 매달린 사이에 상대가 옮겨 갔을 수 있으므로 지금
            // 위치 주변을 먼저 보고, 그것도 없으면 물기 시작한 자리로 돌아간다.
            Vector3 landing = returnPoint;
            if (NavMesh.SamplePosition(u.transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                landing = hit.position;
            }

            u.transform.position = landing;
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.isStopped = false;
        }
    }
}
