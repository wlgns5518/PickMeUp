using System.Collections.Generic;
using UnityEngine;

public class PartyFollowCamera : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Vector3 offset = new Vector3(8.958223f, 5.990178f, -0.102579117f);
    [SerializeField] private float followSmoothTime = 1f;
    [SerializeField] private float axisMoveThreshold = 1f;

    [Header("Look At")]
    [SerializeField] private Vector3 lookRotationEuler = new Vector3(36.1318054f, 269.175232f, 0f);
    [SerializeField] private float rotationSpeed = 5f;

    private Vector3 followVelocity;
    private Vector3 lastKnownCentroid;
    private bool hasCentroid;
    private Vector3 anchoredCentroid;
    private bool hasAnchor;

    private void LateUpdate()
    {
        if (TryGetPartyCentroid(out Vector3 centroid))
        {
            lastKnownCentroid = centroid;
            hasCentroid = true;
        }
        else if (!hasCentroid)
        {
            return;
        }
        else
        {
            centroid = lastKnownCentroid;
        }

        if (!hasAnchor)
        {
            anchoredCentroid = centroid;
            hasAnchor = true;
        }
        else
        {
            Vector3 delta = centroid - anchoredCentroid;
            if (Mathf.Abs(delta.x) >= axisMoveThreshold ||
                Mathf.Abs(delta.y) >= axisMoveThreshold ||
                Mathf.Abs(delta.z) >= axisMoveThreshold)
            {
                anchoredCentroid = centroid;
            }
        }

        Vector3 targetPosition = anchoredCentroid + offset;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref followVelocity, followSmoothTime);

        Quaternion targetRotation = Quaternion.Euler(lookRotationEuler);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private static bool TryGetPartyCentroid(out Vector3 centroid)
    {
        centroid = Vector3.zero;
        int count = 0;

        IReadOnlyList<UnitController> allies = UnitRegistry.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            UnitController ally = allies[i];
            if (ally == null || ally.IsDead || !ally.isActiveAndEnabled) continue;

            centroid += ally.transform.position;
            count++;
        }

        if (count == 0) return false;

        centroid /= count;
        return true;
    }
}
