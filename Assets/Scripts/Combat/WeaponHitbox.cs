using System.Collections.Generic;
using UnityEngine;

public enum HitboxOwnerType
{
    Player,
    Enemy,
}

[RequireComponent(typeof(Rigidbody))]
public class WeaponHitbox : MonoBehaviour
{
    [SerializeField] private HitboxOwnerType ownerType;
    [SerializeField] private Collider[] hitColliders;
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private bool showDebugLogs;

    private readonly HashSet<int> hitTargets = new HashSet<int>();
    private readonly Collider[] overlapResults = new Collider[32];
    private GameObject owner;
    private int damage;
    private float activeEndTime;
    private bool isActive;

    private void Awake()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        if (hitColliders == null || hitColliders.Length == 0)
            hitColliders = GetComponentsInChildren<Collider>();

        if (showDebugLogs && (hitColliders == null || hitColliders.Length == 0))
            Debug.LogWarning($"[WeaponHitbox] No hit colliders found on {name}.", this);

        SetCollidersEnabled(false);
    }

    private void Update()
    {
        if (isActive && Time.time >= activeEndTime)
            EndAttack();
    }

    private void FixedUpdate()
    {
        if (isActive)
            CheckOverlaps();
    }

    public void Configure(GameObject ownerObject, HitboxOwnerType newOwnerType)
    {
        owner = ownerObject;
        ownerType = newOwnerType;
    }

    public void BeginAttack(int attackDamage, float duration)
    {
        damage = attackDamage;
        activeEndTime = Time.time + Mathf.Max(0.01f, duration);
        hitTargets.Clear();
        isActive = true;
        SetCollidersEnabled(true);
        CheckOverlaps();
    }

    public void EndAttack()
    {
        isActive = false;
        SetCollidersEnabled(false);
        hitTargets.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryHit(other);
    }

    private void TryHit(Collider other)
    {
        if (!isActive || other == null) return;
        if (owner != null && other.transform.IsChildOf(owner.transform)) return;

        if (ownerType == HitboxOwnerType.Player)
            TryHitEnemy(other);
        else
            TryHitPlayer(other);
    }

    private void TryHitEnemy(Collider other)
    {
        IEnemy enemy = FindInParents<IEnemy>(other.transform);
        if (enemy == null || enemy.IsDead || enemy.GameObject == owner) return;

        int id = enemy.GameObject.GetInstanceID();
        if (!hitTargets.Add(id)) return;

        if (showDebugLogs)
            Debug.Log($"[WeaponHitbox] {ownerType} hit enemy {enemy.GameObject.name} for {damage}.", this);

        enemy.TakeDamage(damage);
    }

    private void TryHitPlayer(Collider other)
    {
        PlayerAIController player = other.GetComponentInParent<PlayerAIController>();
        if (player == null || player.gameObject == owner) return;
        if (player.Context != null && player.Context.IsDead()) return;

        int id = player.gameObject.GetInstanceID();
        if (!hitTargets.Add(id)) return;

        if (showDebugLogs)
            Debug.Log($"[WeaponHitbox] {ownerType} hit player {player.name} for {damage}.", this);

        player.TakeDamage(damage);
    }

    private void CheckOverlaps()
    {
        if (hitColliders == null) return;

        for (int i = 0; i < hitColliders.Length; i++)
        {
            Collider hitCollider = hitColliders[i];
            if (hitCollider == null || !hitCollider.enabled) continue;

            int count = OverlapCollider(hitCollider);
            for (int j = 0; j < count; j++)
                TryHit(overlapResults[j]);
        }
    }

    private int OverlapCollider(Collider hitCollider)
    {
        if (hitCollider is BoxCollider box)
        {
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 halfExtents = Vector3.Scale(box.size, Abs(box.transform.lossyScale)) * 0.5f;
            return Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                overlapResults,
                box.transform.rotation,
                hitLayers,
                QueryTriggerInteraction.Collide);
        }

        if (hitCollider is SphereCollider sphere)
        {
            Vector3 center = sphere.transform.TransformPoint(sphere.center);
            float radius = sphere.radius * MaxAbsComponent(sphere.transform.lossyScale);
            return Physics.OverlapSphereNonAlloc(
                center,
                radius,
                overlapResults,
                hitLayers,
                QueryTriggerInteraction.Collide);
        }

        if (hitCollider is CapsuleCollider capsule)
        {
            GetCapsulePoints(capsule, out Vector3 pointA, out Vector3 pointB, out float radius);
            return Physics.OverlapCapsuleNonAlloc(
                pointA,
                pointB,
                radius,
                overlapResults,
                hitLayers,
                QueryTriggerInteraction.Collide);
        }

        Bounds bounds = hitCollider.bounds;
        return Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            overlapResults,
            Quaternion.identity,
            hitLayers,
            QueryTriggerInteraction.Collide);
    }

    private static void GetCapsulePoints(CapsuleCollider capsule, out Vector3 pointA, out Vector3 pointB, out float radius)
    {
        Transform capsuleTransform = capsule.transform;
        Vector3 scale = Abs(capsuleTransform.lossyScale);
        int direction = capsule.direction;
        float heightScale = direction == 0 ? scale.x : direction == 1 ? scale.y : scale.z;
        float radiusScale = direction == 0 ? Mathf.Max(scale.y, scale.z) :
                            direction == 1 ? Mathf.Max(scale.x, scale.z) :
                                             Mathf.Max(scale.x, scale.y);

        radius = capsule.radius * radiusScale;
        float height = Mathf.Max(capsule.height * heightScale, radius * 2f);
        Vector3 axis = direction == 0 ? capsuleTransform.right :
                       direction == 1 ? capsuleTransform.up :
                                        capsuleTransform.forward;
        Vector3 center = capsuleTransform.TransformPoint(capsule.center);
        Vector3 offset = axis * ((height * 0.5f) - radius);
        pointA = center + offset;
        pointB = center - offset;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static float MaxAbsComponent(Vector3 value)
    {
        return Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static T FindInParents<T>(Transform start) where T : class
    {
        Transform current = start;
        while (current != null)
        {
            MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is T match)
                    return match;
            }

            current = current.parent;
        }

        return null;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (hitColliders == null) return;

        for (int i = 0; i < hitColliders.Length; i++)
        {
            Collider hitCollider = hitColliders[i];
            if (hitCollider == null) continue;

            hitCollider.isTrigger = true;
            hitCollider.enabled = enabled;
        }
    }
}
