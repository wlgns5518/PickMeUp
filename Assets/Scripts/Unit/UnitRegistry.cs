using System.Collections.Generic;
using UnityEngine;

public static class UnitRegistry
{
    private static readonly List<UnitController> allies = new List<UnitController>(32);
    private static readonly List<UnitController> enemies = new List<UnitController>(32);
    private static readonly List<UnitController> neutrals = new List<UnitController>(16);

    public static IReadOnlyList<UnitController> Allies => allies;
    public static IReadOnlyList<UnitController> Enemies => enemies;
    public static IReadOnlyList<UnitController> Neutrals => neutrals;

    public static void Register(UnitController unit)
    {
        if (unit == null) return;

        List<UnitController> list = GetList(unit.Team);
        if (list.Contains(unit)) return;

        list.Add(unit);
    }

    public static void Unregister(UnitController unit)
    {
        if (unit == null) return;

        allies.Remove(unit);
        enemies.Remove(unit);
        neutrals.Remove(unit);
    }

    public static UnitController FindNearestVisibleEnemy(
        UnitController requester,
        float range,
        float viewAngle,
        float closeVisibleRange = 0f,
        float eyeHeight = 0f,
        LayerMask obstacleMask = default)
    {
        if (requester == null) return null;

        float bestSqrDistance = range * range;
        UnitController best = null;

        SearchNearestInList(requester, allies, range, viewAngle, closeVisibleRange, eyeHeight, obstacleMask, ref bestSqrDistance, ref best);
        SearchNearestInList(requester, enemies, range, viewAngle, closeVisibleRange, eyeHeight, obstacleMask, ref bestSqrDistance, ref best);
        SearchNearestInList(requester, neutrals, range, viewAngle, closeVisibleRange, eyeHeight, obstacleMask, ref bestSqrDistance, ref best);

        return best;
    }

    public static bool HasLineOfSight(Vector3 from, Vector3 to, float eyeHeight, LayerMask obstacleMask)
    {
        if (obstacleMask == 0) return true;

        Vector3 origin = from + Vector3.up * eyeHeight;
        Vector3 destination = to + Vector3.up * eyeHeight;
        Vector3 offset = destination - origin;
        float distance = offset.magnitude;
        if (distance <= 0.01f) return true;

        if (!Physics.Raycast(origin, offset / distance, out RaycastHit hit, distance - 0.05f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        // Other units' bodies shouldn't block line of sight - only static level geometry (walls) should.
        return hit.collider.GetComponentInParent<UnitController>() != null;
    }

    public static void AlertTeam(UnitController spotter, UnitController target)
    {
        if (!IsValidTarget(spotter, target)) return;

        List<UnitController> team = GetList(spotter.Team);
        for (int i = team.Count - 1; i >= 0; i--)
        {
            UnitController unit = team[i];
            if (unit == null || unit.IsDead || !unit.isActiveAndEnabled) continue;

            unit.ReceiveSharedTarget(target);
        }
    }

    public static void FindEnemiesInRange(UnitController requester, float range, List<UnitController> results)
    {
        if (results == null) return;
        results.Clear();

        if (requester == null) return;

        AddEnemiesInRange(requester, allies, range, results);
        AddEnemiesInRange(requester, enemies, range, results);
        AddEnemiesInRange(requester, neutrals, range, results);
    }

    public static bool HasLivingEnemy(UnitController requester)
    {
        if (requester == null) return false;

        return HasLivingEnemyInList(requester, allies) ||
               HasLivingEnemyInList(requester, enemies) ||
               HasLivingEnemyInList(requester, neutrals);
    }

    public static bool AreEnemies(UnitController a, UnitController b)
    {
        if (a == null || b == null || a == b) return false;
        if (a.Team == UnitTeam.Neutral) return b.Team != UnitTeam.Neutral;
        if (b.Team == UnitTeam.Neutral) return false;
        return a.Team != b.Team;
    }

    public static bool IsVisibleTo(
        UnitController requester,
        UnitController target,
        float range,
        float viewAngle,
        float closeVisibleRange = 0f)
    {
        if (!IsValidTarget(requester, target)) return false;

        Vector3 requesterPosition = requester.transform.position;
        Vector3 toTarget = target.transform.position - requesterPosition;
        toTarget.y = 0f;

        float sqrDistance = toTarget.sqrMagnitude;
        float rangeSqr = range * range;
        if (sqrDistance > rangeSqr || sqrDistance <= 0.0001f) return false;

        float closeVisibleRangeSqr = closeVisibleRange * closeVisibleRange;
        if (closeVisibleRange > 0f && sqrDistance <= closeVisibleRangeSqr) return true;

        if (viewAngle >= 359f) return true;

        Vector3 forward = requester.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f) return true;

        float dot = Vector3.Dot(forward.normalized, toTarget.normalized);
        float halfAngle = Mathf.Clamp(viewAngle * 0.5f, 0f, 180f);
        float minDot = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        return dot >= minDot;
    }

    private static void SearchNearestInList(
        UnitController requester,
        List<UnitController> list,
        float range,
        float viewAngle,
        float closeVisibleRange,
        float eyeHeight,
        LayerMask obstacleMask,
        ref float bestSqrDistance,
        ref UnitController best)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;
            if (!IsVisibleTo(requester, candidate, range, viewAngle, closeVisibleRange)) continue;

            // Distance check before the raycast so line-of-sight (the expensive part) only
            // runs for candidates that would actually improve on the current best.
            float sqrDistance = (candidate.transform.position - requester.transform.position).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance) continue;

            if (!HasLineOfSight(requester.transform.position, candidate.transform.position, eyeHeight, obstacleMask)) continue;

            bestSqrDistance = sqrDistance;
            best = candidate;
        }
    }

    private static bool HasLivingEnemyInList(UnitController requester, List<UnitController> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (AreEnemies(requester, candidate)) return true;
        }

        return false;
    }

    private static void AddEnemiesInRange(
        UnitController requester,
        List<UnitController> list,
        float range,
        List<UnitController> results)
    {
        float rangeSqr = range * range;
        Vector3 requesterPosition = requester.transform.position;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            UnitController candidate = list[i];
            if (!IsValidTarget(requester, candidate)) continue;
            if (!AreEnemies(requester, candidate)) continue;

            float sqrDistance = (candidate.transform.position - requesterPosition).sqrMagnitude;
            if (sqrDistance <= rangeSqr)
            {
                results.Add(candidate);
            }
        }
    }

    private static bool IsValidTarget(UnitController requester, UnitController target)
    {
        return requester != null &&
               target != null &&
               requester != target &&
               target.isActiveAndEnabled &&
               !target.IsDead;
    }

    private static List<UnitController> GetList(UnitTeam team)
    {
        switch (team)
        {
            case UnitTeam.Ally:
                return allies;
            case UnitTeam.Enemy:
                return enemies;
            default:
                return neutrals;
        }
    }
}
