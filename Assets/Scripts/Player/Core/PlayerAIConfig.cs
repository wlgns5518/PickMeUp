using UnityEngine;

[CreateAssetMenu(menuName = "PickMeUp/PlayerAIConfig", fileName = "PlayerAIConfig")]
public class PlayerAIConfig : ScriptableObject
{
    [Header("HP")]
    [Range(0f, 1f)]
    [Tooltip("Enter flee behavior at or below this HP ratio.")]
    public float fleeHpRatio = 0.2f;

    [Range(0f, 1f)]
    [Tooltip("Allow combat recovery at or above this HP ratio.")]
    public float safeHpRatio = 0.5f;

    [Header("Cooldowns")]
    public float dodgeCooldown = 1.2f;
    public float potionCooldown = 8.0f;

    [Header("Movement")]
    [Tooltip("Turn speed toward the movement target.")]
    public float moveTurnSpeed = 540f;

    [Tooltip("Start moving when the angle to the target is below this value.")]
    public float moveStartAngle = 5f;

    [Tooltip("Distance used when choosing a flee destination.")]
    public float fleeDistance = 8.75f;

    [Tooltip("Patrol radius around the spawn position.")]
    public float patrolRadius = 5f;

    [Header("Danger")]
    [Tooltip("Projectile radius considered dangerous.")]
    public float dangerSkillRadius = 3f;

    [Header("Combat")]
    [Tooltip("Minimum wait time after a normal attack finishes.")]
    public float postAttackDelay = 1.5f;

    [Tooltip("Delay before damage for attacks that still use timed damage.")]
    public float attackHitDelay = 0.2f;

    [Tooltip("Heavy attack motion duration.")]
    public float heavyAttackMotionDuration = 0.8f;

    [Tooltip("Skill motion duration.")]
    public float skillMotionDuration = 0.5f;

    [Tooltip("Dodge duration.")]
    public float dodgeDuration = 0.3f;

    [Tooltip("Time before considering the character stuck.")]
    public float stuckThreshold = 1.2f;

    [Tooltip("Maximum flee duration before returning to combat.")]
    public float maxKiteTime = 3f;

    [Header("Reaction")]
    [Tooltip("Hit reaction duration.")]
    public float hitReactionDuration = 0.35f;

    [Tooltip("Block duration.")]
    public float blockDuration = 1.2f;

    [Range(0f, 1f)]
    [Tooltip("Chance to enter block while kiting.")]
    public float blockChance = 0.3f;
}
