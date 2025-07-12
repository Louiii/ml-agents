using UnityEngine;
using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine.Events;

namespace Unity.MLAgentsExamples
{
    /// <summary>
    /// Utility class to allow target placement and collision detection with an agent.
    /// This modified version places the target on a series of steps with increasing difficulty.
    /// </summary>
    public class TargetController : MonoBehaviour
    {
        [Header("Collider Tag To Detect")]
        public string tagToDetect = "AgentBody";

        [Header("Stair & Curriculum Settings")]
        [Tooltip("Drag all your Slab GameObjects here in order of difficulty")]
        public List<GameObject> steps;
        [Tooltip("How many episodes pass before the task gets harder")]
        public int episodesPerDifficultyIncrease = 2000;

        [Header("Target Fell Protection")]
        public bool respawnIfFallsOffPlatform = true;
        public float fallDistance = 5;

        // Add references to the other main components
        public EnvironmentController environmentController;
        public WalkerAgent agent;

        // We no longer use spawnRadius, but keep these for the fall protection logic
        private Vector3 m_startingPos;
        private int m_activeStepCount = 1;
        private Academy m_academy;

        // --- We keep all the original UnityEvent callbacks as they are essential ---
        [System.Serializable]
        public class TriggerEvent : UnityEvent<Collider> { }
        [Header("Trigger Callbacks")]
        public TriggerEvent onTriggerEnterEvent = new TriggerEvent();
        public TriggerEvent onTriggerStayEvent = new TriggerEvent();
        public TriggerEvent onTriggerExitEvent = new TriggerEvent();
        [System.Serializable]
        public class CollisionEvent : UnityEvent<Collision> { }
        [Header("Collision Callbacks")]
        public CollisionEvent onCollisionEnterEvent = new CollisionEvent();
        public CollisionEvent onCollisionStayEvent = new CollisionEvent();
        public CollisionEvent onCollisionExitEvent = new CollisionEvent();


        /// We make this static so the count is shared across all arenas
        private int consecutiveSuccesses = 0;
        public const int successesNeeded = 4;

        void Awake()
        {
            m_academy = Academy.Instance;
            // The ground's position is a good reference for fall detection
            m_startingPos = new Vector3(transform.position.x, 0, transform.position.z); 
        }

        void Update()
        {
            if (respawnIfFallsOffPlatform)
            {
                if (transform.position.y < m_startingPos.y - fallDistance)
                {
                    Debug.Log($"{transform.name} Fell Off Platform, respawning.");
                    PlaceTargetOnStep();
                }
            }
        }

        public void ResetSuccessCounter()
        {
            if (consecutiveSuccesses > 0)
            {
                Debug.Log("Success streak broken! Resetting counter.");
            }
            consecutiveSuccesses = 0;
        }

        public void PlaceTargetOnStep()
        {
            if (steps == null || steps.Count == 0)
            {
                Debug.LogError("Steps list is empty!");
                return;
            }

            // Pick the highest currently active step
            int randomIndex = m_activeStepCount - 1;
            if (randomIndex < 0 || randomIndex >= steps.Count)
            {
                Debug.LogError($"Calculated invalid step index: {randomIndex}");
                return;
            }
            GameObject selectedStep = steps[randomIndex];

            if (selectedStep == null)
            {
                Debug.LogError($"The step at index {randomIndex} is null!");
                return;
            }

            // Calculate the position on TOP of the selected step
            float headHeight = 3.0f;
            float stepHeight = selectedStep.transform.localScale.y;
            float targetHeight = transform.localScale.y;
            float yOffset = (stepHeight / 2f) + (targetHeight / 2f) + headHeight;

            Vector3 newTargetPosition = selectedStep.transform.position + new Vector3(0, yOffset, 0);

            // Set this object's (the target's) position
            transform.position = newTargetPosition;

            // Turn the target back on now that it's in its new, safe position.
            gameObject.SetActive(true);
        }

        // --- All the original collision/trigger methods are kept exactly the same ---
        // They will now call our new PlaceTargetOnStep() method.
        private void OnCollisionEnter(Collision col)
        {
            if (col.transform.CompareTag(tagToDetect))
            {
                onCollisionEnterEvent.Invoke(col);
            }
        }

        // (The rest of the OnCollisionStay, OnTriggerEnter, etc. methods remain unchanged)
        private void OnCollisionStay(Collision col)
        {
            if (col.transform.CompareTag(tagToDetect)) { onCollisionStayEvent.Invoke(col); }
        }
        private void OnCollisionExit(Collision col)
        {
            if (col.transform.CompareTag(tagToDetect)) { onCollisionExitEvent.Invoke(col); }
        }
        private void OnTriggerEnter(Collider col)
        {
            if (col.CompareTag("AgentBody") || col.CompareTag("AgentFoot"))
            {
                Debug.Log($"Trigger on Instance ID: {GetInstanceID()}. Current difficulty (m_activeStepCount) is: {m_activeStepCount}");
                // Let the original UnityEvent fire if anything is using it
                onTriggerEnterEvent.Invoke(col);

                // Check if the agent has reached the TOP step of the whole staircase
                if (m_activeStepCount >= steps.Count)
                {
                    // Immediately deactivate the target to prevent double triggers.
                    gameObject.SetActive(false);
                    Debug.Log("FINAL STEP REACHED! Reconfiguring world.");

                    // Tell the agent to reset all its states for the new challenge.
                    if (agent != null)
                    {
                        agent.ResetStairProgress();
                        agent.StartLandingPhase();
                        agent.StartApproachPhase();
                    }
                    
                    // Tell the environment to move the staircase.
                    environmentController.RepositionStaircase(steps[steps.Count - 1].transform);

                    // Reset the success counter for the new curriculum goal.
                    consecutiveSuccesses = 0;

                    // Place the target on the new staircase. This will also reactivate it.
                    PlaceTargetOnStep();
                }
                else
                {
                    // This is the normal "level up" logic
                    consecutiveSuccesses++;
                    Debug.Log($"Consecutive successes: {consecutiveSuccesses} / {successesNeeded}");

                    // Check if the agent has succeeded enough times to advance
                    if (consecutiveSuccesses >= successesNeeded)
                    {
                        // Tell the global controller to advance its training phase (e.g., from 2 to 3)
                        EnvironmentController.AdvanceTrainingPhase();

                        // Increase the internal step-climbing difficulty for this arena
                        if (m_activeStepCount < steps.Count)
                        {
                            m_activeStepCount++;
                            Debug.Log($"DIFFICULTY INCREASED to level {m_activeStepCount}!");
                        }
                        
                        // Reset the success counter for the next challenge
                        consecutiveSuccesses = 0;
                    }

                    // After all checks, place the target on the correct step
                    PlaceTargetOnStep();
                }
            }
        }
        private void OnTriggerStay(Collider col)
        {
            if (col.CompareTag(tagToDetect)) { onTriggerStayEvent.Invoke(col); }
        }
        private void OnTriggerExit(Collider col)
        {
            if (col.CompareTag(tagToDetect)) { onTriggerExitEvent.Invoke(col); }
        }
    }
}