// using System.Collections.Generic;
// using UnityEngine;
// using Photon.Pun;
// using AlbuRIOT.AI.BehaviorTree;

// [RequireComponent(typeof(CharacterController))]
// public class EnemyAIController : MonoBehaviourPun
// {
//     [Header("stats & references")]
//     public EnemyStats stats;
//     public Animator animator;
//     public string speedParam = "Speed";
//     public string attackTrigger = "Attack";
//     public string hitTrigger = "Hit";
//     public string dieTrigger = "Die";
//     public string isDeadBool = "IsDead";
//     public bool useAnimationEventDamage = true;
//     public float attackMoveLock = 0.35f;

//     [Header("patrol settings")]
//     public bool enablePatrol = true;
//     public float patrolRadius = 8f;
//     public float patrolWait = 1.5f;

//     private CharacterController controller;
//     private Vector3 spawnPoint;
//     private float lastAttackTime;
//     private int currentHealth;
//     private float attackLockTimer = 0f;
//     private bool isDead = false;

//     // Behavior tree
//     private Blackboard bb;
//     private Node tree;

//     // Cached runtime state
//     private Transform targetPlayer;
//     private Vector3 patrolTarget;
//     private float patrolWaitTimer;

//     void Awake()
//     {
//         controller = GetComponent<CharacterController>();
//         if (animator == null) animator = GetComponentInChildren<Animator>();
//         spawnPoint = transform.position;
//         currentHealth = stats != null ? stats.maxHealth : 100;

//         bb = new Blackboard { owner = gameObject };
//         BuildTree();
//     }

//     void Update()
//     {
//         if (isDead) return;

//         // Network authority: only master client (or offline) drives ai
//         if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
//             return;

//         // Locomotion speed debug param
//         if (animator != null && HasFloatParam(speedParam))
//         {
//             Vector3 v = controller != null ? controller.velocity : Vector3.zero;
//             float planar = new Vector3(v.x, 0f, v.z).magnitude;
//             animator.SetFloat(speedParam, planar);
//         }

//         if (attackLockTimer > 0f)
//             attackLockTimer -= Time.deltaTime;

//         var result = tree != null ? tree.Tick() : NodeState.Failure;
//     }

//     private void BuildTree()
//     {
//         // Leaves/conditions are bound to instance methods using blackboard
//         var updateTarget = new ActionNode(bb, UpdateTarget, "update_target");
//         var hasTarget = new ConditionNode(bb, HasTarget, "has_target");
//         var targetInDetection = new ConditionNode(bb, TargetInDetectionRange, "in_detect_range");
//         var moveToTarget = new ActionNode(bb, MoveTowardsTarget, "move_to_target");
//         var targetInAttack = new ConditionNode(bb, TargetInAttackRange, "in_attack_range");
//         var attack = new ActionNode(bb, AttackTarget, "attack");
//         var patrol = new ActionNode(bb, Patrol, "patrol");

//         // Logic: update target -> if has target and in detection, chase; if in attack range, attack; else patrol
//         tree = new Selector(bb, "root")
//             .Add(
//                 new Sequence(bb, "combat").Add(updateTarget, hasTarget, targetInDetection,
//                     new Selector(bb, "chase_or_attack").Add(
//                         new Sequence(bb, "attack_seq").Add(targetInAttack, attack),
//                         moveToTarget
//                     )
//                 ),
//                 // Fallback to patrol
//                 patrol
//             );
//     }

//     // Leaf and helper methods
//     private float targetScanInterval = 0.5f;
//     private float targetScanTimer = 0f;

//     private NodeState UpdateTarget()
//     {
//         // Throttle scanning to reduce cost
//         if (targetScanTimer > 0f)
//         {
//             targetScanTimer -= Time.deltaTime;
//         }
//         if (targetScanTimer <= 0f)
//         {
//             targetScanTimer = targetScanInterval;
//             float best = float.MaxValue;
//             Transform nearest = null;
//             var players = GameObject.FindObjectsOfType<PlayerStats>();
//             foreach (var p in players)
//             {
//                 if (p == null) continue;
//                 float d = Vector3.Distance(transform.position, p.transform.position);
//                 if (d < best)
//                 {
//                     best = d; nearest = p.transform;
//                 }
//             }
//             targetPlayer = nearest;
//             bb.Set("target", targetPlayer);
//         }
//         return NodeState.Success;
//     }

//     private bool HasTarget()
//     {
//         var t = bb.Get<Transform>("target");
//         return t != null;
//     }

//     private bool TargetInDetectionRange()
//     {
//         var t = bb.Get<Transform>("target");
//         if (t == null || stats == null) return false;
//         return Vector3.Distance(transform.position, t.position) <= stats.detectionRange;
//     }

//     private bool TargetInAttackRange()
//     {
//         var t = bb.Get<Transform>("target");
//         if (t == null || stats == null) return false;
//         return Vector3.Distance(transform.position, t.position) <= stats.attackRange;
//     }

//     private NodeState MoveTowardsTarget()
//     {
//         var t = bb.Get<Transform>("target");
//         if (t == null || controller == null || stats == null) return NodeState.Failure;
//         if (attackLockTimer > 0f) return NodeState.Running;

//         Vector3 dir = (t.position - transform.position);
//         dir.y = 0f;
//         var dist = dir.magnitude;
//         if (dist <= stats.attackRange * 0.9f) return NodeState.Success;
//         dir.Normalize();
//         if (controller != null && controller.enabled)
//             controller.SimpleMove(dir * stats.moveSpeed);
//         // Rotate towards target
//         var look = new Vector3(t.position.x, transform.position.y, t.position.z);
//         transform.LookAt(look);

//         return NodeState.Running;
//     }

//     private NodeState AttackTarget()
//     {
//         if (stats == null) return NodeState.Failure;
//         if (Time.time - lastAttackTime < stats.attackCooldown) return NodeState.Running;
//         var t = bb.Get<Transform>("target");
//         if (t == null) return NodeState.Failure;

//         lastAttackTime = Time.time;
//         if (animator != null && HasTrigger(attackTrigger)) animator.SetTrigger(attackTrigger);

//         if (!useAnimationEventDamage)
//         {
//             // Apply damage immediately and briefly lock movement for animation
//             DamageRelay.ApplyToPlayer(t.gameObject, stats.damage);
//             attackLockTimer = attackMoveLock;
//         }
//         return NodeState.Success;
//     }

//     private NodeState Patrol()
//     {
//         if (!enablePatrol) return NodeState.Failure;

//         // Pick a target point if none or reached
//         Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
//         Vector3 flatTar = new Vector3(patrolTarget.x, 0f, patrolTarget.z);
//         if (patrolTarget == Vector3.zero || Vector3.Distance(flatPos, flatTar) < 0.5f)
//         {
//             if (patrolWaitTimer <= 0f)
//             {
//                 patrolTarget = spawnPoint + Random.insideUnitSphere * patrolRadius;
//                 patrolTarget.y = transform.position.y;
//                 patrolWaitTimer = patrolWait;
//             }
//             else
//             {
//                 patrolWaitTimer -= Time.deltaTime;
//                 return NodeState.Running;
//             }
//         }

//         if (controller != null)
//         {
//             Vector3 dir = (patrolTarget - transform.position);
//             dir.y = 0f;
//             if (dir.magnitude > 0.1f)
//             {
//                 dir.Normalize();
//                 if (controller != null && controller.enabled)
//                     controller.SimpleMove(dir * (stats != null ? stats.moveSpeed * 0.75f : 2.5f));
//                 transform.LookAt(new Vector3(patrolTarget.x, transform.position.y, patrolTarget.z));
//                 return NodeState.Running;
//             }
//         }
//         return NodeState.Success;
//     }

//     private bool HasFloatParam(string param)
//     {
//         if (animator == null || string.IsNullOrEmpty(param)) return false;
//         foreach (var p in animator.parameters)
//             if (p.type == AnimatorControllerParameterType.Float && p.name == param) return true;
//         return false;
//     }

//     private bool HasTrigger(string param)
//     {
//         if (animator == null || string.IsNullOrEmpty(param)) return false;
//         foreach (var p in animator.parameters)
//             if (p.type == AnimatorControllerParameterType.Trigger && p.name == param) return true;
//         return false;
//     }
// }
