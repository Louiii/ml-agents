using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgentsExamples;
using Unity.MLAgents.Sensors;
using BodyPart = Unity.MLAgentsExamples.BodyPart;
using Random = UnityEngine.Random;

public class WalkerAgent : Agent
{
    [Header("Walk Speed")]
    [Range(0.1f, 10)]
    [SerializeField]
    //The walking speed to try and achieve
    private float m_TargetWalkingSpeed = 10;
    private float m_LastHipYPosition;
    private float m_HighestStepY = 0f;
    private bool m_IsLanding = false;
    private Transform m_StairApproachTarget; // An invisible waypoint
    private bool m_IsApproachingStairs;      // Tracks which stage we're in

    public float MTargetWalkingSpeed // property
    {
        get { return m_TargetWalkingSpeed; }
        set { m_TargetWalkingSpeed = Mathf.Clamp(value, .1f, m_maxWalkingSpeed); }
    }

    const float m_maxWalkingSpeed = 10; //The max walking speed

    //Should the agent sample a new goal velocity each episode?
    //If true, walkSpeed will be randomly set between zero and m_maxWalkingSpeed in OnEpisodeBegin()
    //If false, the goal velocity will be walkingSpeed
    public bool randomizeWalkSpeedEachEpisode;

    //The direction an agent will walk during training.
    private Vector3 m_WorldDirToWalk = Vector3.right;

    [Header("Target To Walk Towards")] public Transform target; //Target the agent will walk towards during training.

    [Header("Body Parts")] public Transform hips;
    public Transform chest;
    public Transform spine;
    public Transform head;
    public Transform thighL;
    public Transform shinL;
    public Transform footL;
    public Transform thighR;
    public Transform shinR;
    public Transform footR;
    public Transform armL;
    public Transform forearmL;
    public Transform handL;
    public Transform armR;
    public Transform forearmR;
    public Transform handR;
    public Transform staircase;
    public EnvironmentController environmentController;
    public TargetController targetController;
    public GameObject footTargetVisualizer;
    public GameObject waypointVisualizer;

    //This will be used as a stabilized model space reference point for observations
    //Because ragdolls can move erratically during training, using a stabilized reference transform improves learning
    OrientationCubeController m_OrientationCube;

    //The indicator graphic gameobject that points towards the target
    DirectionIndicator m_DirectionIndicator;
    JointDriveController m_JdController;
    EnvironmentParameters m_ResetParams;

    public override void Initialize()
    {
        m_OrientationCube = GetComponentInChildren<OrientationCubeController>();
        m_DirectionIndicator = GetComponentInChildren<DirectionIndicator>();

        // Create the invisible waypoint object
        m_StairApproachTarget = new GameObject("StairApproachTarget").transform;

        // Attach the yellow ball to our waypoint
        if (waypointVisualizer != null)
        {
            waypointVisualizer.transform.SetParent(m_StairApproachTarget);
            waypointVisualizer.transform.localPosition = Vector3.zero;
        }

        //Setup each body part
        m_JdController = GetComponent<JointDriveController>();
        m_JdController.SetupBodyPart(hips);
        m_JdController.SetupBodyPart(chest);
        m_JdController.SetupBodyPart(spine);
        m_JdController.SetupBodyPart(head);
        m_JdController.SetupBodyPart(thighL);
        m_JdController.SetupBodyPart(shinL);
        m_JdController.SetupBodyPart(footL);
        m_JdController.SetupBodyPart(thighR);
        m_JdController.SetupBodyPart(shinR);
        m_JdController.SetupBodyPart(footR);
        m_JdController.SetupBodyPart(armL);
        m_JdController.SetupBodyPart(forearmL);
        m_JdController.SetupBodyPart(handL);
        m_JdController.SetupBodyPart(armR);
        m_JdController.SetupBodyPart(forearmR);
        m_JdController.SetupBodyPart(handR);

        m_ResetParams = Academy.Instance.EnvironmentParameters;

        SetResetParameters();
    }

    /// <summary>
    /// Loop over body parts and reset them to initial conditions.
    /// </summary>
    public override void OnEpisodeBegin()
    {
        m_IsApproachingStairs = true; // Always start by approaching the stairs

        // Call the environment controller to reset the staircase position
        if (environmentController != null)
        {
            environmentController.ResetEnvironment();
        }
        // NOW, TELL THE TARGET TO MOVE ONTO THE NEW STAIRS
        if (targetController != null)
        {
            targetController.PlaceTargetOnStep();
        }

        //Reset all of the body parts
        foreach (var bodyPart in m_JdController.bodyPartsDict.Values)
        {
            bodyPart.Reset(bodyPart);
        }

        //Random start rotation to help generalize
        hips.rotation = Quaternion.Euler(0, Random.Range(0.0f, 360.0f), 0);

        UpdateOrientationObjects();

        //Set our goal walking speed
        MTargetWalkingSpeed =
            randomizeWalkSpeedEachEpisode ? Random.Range(0.1f, m_maxWalkingSpeed) : MTargetWalkingSpeed;

        SetResetParameters();
        m_LastHipYPosition = hips.position.y;
        m_HighestStepY = 0f; // Reset the high score for the new episode
    }

    /// <summary>
    /// Add relevant information on each body part to observations.
    /// </summary>
    public void CollectObservationBodyPart(BodyPart bp, VectorSensor sensor)
    {
        //GROUND CHECK
        sensor.AddObservation(bp.groundContact.touchingGround); // Is this bp touching the ground

        //Get velocities in the context of our orientation cube's space
        //Note: You can get these velocities in world space as well but it may not train as well.
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.velocity));
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.angularVelocity));

        //Get position relative to hips in the context of our orientation cube's space
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(bp.rb.position - hips.position));

        if (bp.rb.transform != hips && bp.rb.transform != handL && bp.rb.transform != handR)
        {
            sensor.AddObservation(bp.rb.transform.localRotation);
            sensor.AddObservation(bp.currentStrength / m_JdController.maxJointForceLimit);
        }
    }

    // This method is called by a foot when it touches a step
    public void AchievedNewStep(float stepWorldY)
    {
        // A successful step ends the landing phase
        LandedOnNewStep();

        // Check if this step is higher than our previous record
        if (stepWorldY > m_HighestStepY + 0.1f) // The 0.1f is a small threshold
        {
            // Give a large, one-time bonus for making real progress
            AddReward(0.5f);
            // Update our new high score
            m_HighestStepY = stepWorldY;
            Debug.Log("LEVEL UP! Achieved a new higher step!");
        }
    }

    /// <summary>
    /// Loop over body parts to add them to observation.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // --- NEW: Add observation for the foot target ---
        if (footTargetVisualizer != null && footTargetVisualizer.activeInHierarchy)
        {
            // Vector from hips to the foot target, in the hips' local space
            sensor.AddObservation(hips.InverseTransformPoint(footTargetVisualizer.transform.position));
            // Vector from the left foot to the target
            sensor.AddObservation(footL.InverseTransformPoint(footTargetVisualizer.transform.position));
            // Vector from the right foot to the target
            sensor.AddObservation(footR.InverseTransformPoint(footTargetVisualizer.transform.position));
        }
        else
        {
            // If the target is inactive, send placeholder zeros to keep the observation size consistent
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(Vector3.zero);
        }

        var cubeForward = m_OrientationCube.transform.forward;

        //velocity we want to match
        var velGoal = cubeForward * MTargetWalkingSpeed;
        //ragdoll's avg vel
        var avgVel = GetAvgVelocity();

        //current ragdoll velocity. normalized
        sensor.AddObservation(Vector3.Distance(velGoal, avgVel));
        //avg body vel relative to cube
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(avgVel));
        //vel goal relative to cube
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformDirection(velGoal));

        //rotation deltas
        sensor.AddObservation(Quaternion.FromToRotation(hips.forward, cubeForward));
        sensor.AddObservation(Quaternion.FromToRotation(head.forward, cubeForward));

        //Position of target position relative to cube
        sensor.AddObservation(m_OrientationCube.transform.InverseTransformPoint(target.transform.position));

        // Add these new lines to observe the staircase
        // Position of staircase relative to the agent
        sensor.AddObservation(transform.InverseTransformPoint(staircase.position));

        // Rotation of the staircase RELATIVE to the agent's orientation cube
        Quaternion relativeRotation = Quaternion.Inverse(m_OrientationCube.transform.rotation) * staircase.rotation;
        sensor.AddObservation(relativeRotation);

        foreach (var bodyPart in m_JdController.bodyPartsList)
        {
            CollectObservationBodyPart(bodyPart, sensor);
        }
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)

    {
        var bpDict = m_JdController.bodyPartsDict;
        var i = -1;

        var continuousActions = actionBuffers.ContinuousActions;
        bpDict[chest].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);
        bpDict[spine].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);

        bpDict[thighL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[thighR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[shinL].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[shinR].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[footR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);
        bpDict[footL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], continuousActions[++i]);

        bpDict[armL].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[armR].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);
        bpDict[forearmL].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[forearmR].SetJointTargetRotation(continuousActions[++i], 0, 0);
        bpDict[head].SetJointTargetRotation(continuousActions[++i], continuousActions[++i], 0);

        //update joint strength settings
        bpDict[chest].SetJointStrength(continuousActions[++i]);
        bpDict[spine].SetJointStrength(continuousActions[++i]);
        bpDict[head].SetJointStrength(continuousActions[++i]);
        bpDict[thighL].SetJointStrength(continuousActions[++i]);
        bpDict[shinL].SetJointStrength(continuousActions[++i]);
        bpDict[footL].SetJointStrength(continuousActions[++i]);
        bpDict[thighR].SetJointStrength(continuousActions[++i]);
        bpDict[shinR].SetJointStrength(continuousActions[++i]);
        bpDict[footR].SetJointStrength(continuousActions[++i]);
        bpDict[armL].SetJointStrength(continuousActions[++i]);
        bpDict[forearmL].SetJointStrength(continuousActions[++i]);
        bpDict[armR].SetJointStrength(continuousActions[++i]);
        bpDict[forearmR].SetJointStrength(continuousActions[++i]);
    }

    void UpdateOrientationObjects()
    {
        Transform currentTarget = target; // Default to the final green target

        // If we are in the first stage (approaching the stairs)
        if (m_IsApproachingStairs)
        {
            // Make the yellow ball visible
            if (waypointVisualizer != null) waypointVisualizer.SetActive(true);

            // (The waypoint calculation logic is the same)
            Vector3 stairBackDirection = -staircase.right;
            float stepDepth = staircase.lossyScale.x / 2.0f;
            Vector3 calcPos = staircase.position + stairBackDirection * (stepDepth + 5.0f);
            Vector3 waypointPosition = new Vector3(calcPos.x, 2.0f, calcPos.z);
            m_StairApproachTarget.position = waypointPosition;

            currentTarget = m_StairApproachTarget;
        }
        else
        {
            // Hide the yellow ball when it's not the target
            if (waypointVisualizer != null) waypointVisualizer.SetActive(false);
        }

        // This now dynamically points the cube at the correct target
        m_WorldDirToWalk = currentTarget.position - hips.position;
        m_OrientationCube.UpdateOrientation(hips, currentTarget);
        if (m_DirectionIndicator)
        {
            m_DirectionIndicator.MatchOrientation(m_OrientationCube.transform);
        }
    }

    void FixedUpdate()
    {
        UpdateOrientationObjects();
        // --- VISIBILITY CONTROL ---
        // Turn the final green target's renderer ON only in Phase 2
        if(target != null) target.GetComponent<MeshRenderer>().enabled = !m_IsApproachingStairs;

        // --- ALWAYS-ON LOGIC (applies in all phases) ---
        AddReward(-0.001f);

        var cubeForward = m_OrientationCube.transform.forward;
        var lookAtTargetReward = (Vector3.Dot(cubeForward, head.forward) + 1) * .5F;
        AddReward(lookAtTargetReward * 0.1f);

        if (!m_IsLanding && Vector3.Dot(hips.up, Vector3.up) < 0.5f)
        {
            AddReward(-1.0f);
            targetController.ResetSuccessCounter();
            EndEpisode();
            return; 
        }

        // --- STATE-DEPENDENT LOGIC ---
        if (m_IsApproachingStairs)
        {
            // --- PHASE 1: APPROACHING THE WAYPOINT ---
            // Blue ball should be OFF
            if(footTargetVisualizer != null) footTargetVisualizer.SetActive(false);

            // --- PHASE 1: APPROACHING THE WAYPOINT WITH SPEED CONTROL ---
            Vector3 directionToWaypoint = m_StairApproachTarget.position - hips.position;
            directionToWaypoint.y = 0;
            float distanceToWaypoint = directionToWaypoint.magnitude;

            Vector3 avgVel = GetAvgVelocity();
            avgVel.y = 0;

            // 1. Calculate the agent's current speed component IN THE DIRECTION of the waypoint.
            // This is positive if moving towards, negative if moving away.
            float velocityTowardsWaypoint = Vector3.Dot(directionToWaypoint.normalized, avgVel);
            AddReward(velocityTowardsWaypoint * 0.5f);

            // 2. Define the ideal speed.
            // The target speed should not exceed the distance to the target.
            // This naturally encourages deceleration.
            // Debug.Log($"Distance: {distanceToWaypoint:F2}, Velocity Towards: {velocityTowardsWaypoint:F2}");
            float targetSpeed = Mathf.Min(velocityTowardsWaypoint, 0.5f * distanceToWaypoint);

            // 3. Calculate the error between the agent's actual speed and the ideal speed.
            float speedError = Mathf.Abs(avgVel.magnitude - targetSpeed);

            // 4. The reward is a penalty for this error. The reward is highest (zero) when the error is zero.
            float speedReward = -speedError;
            
            AddReward(speedReward * 0.3f); // Add a multiplier to tune its importance.
        }
        else
        {
            // --- PHASE 2: CLIMBING THE STAIRS ---
            // --- ADD THESE LINES TO CREATE THE "LANE" CHECK ---
            Vector3 vectorToAgent = hips.position - staircase.position;
            // The staircase's "sideways" direction is its local Z-axis (.forward)
            float sideDistance = Vector3.Dot(vectorToAgent, staircase.forward);
            // The width of the lane is based on the staircase's scale
            float halfStairWidth = staircase.lossyScale.z / 2.0f;

            // --- WRAP YOUR VELOCITY REWARD IN THIS IF STATEMENT ---
            // Only give the forward velocity reward if the agent is lined up with the stairs
            if (Mathf.Abs(sideDistance) < halfStairWidth)
            {
                // This is your existing velocity reward code
                Vector3 directionToTarget = target.position - hips.position;
                directionToTarget.y = 0;
                Vector3 avgVel = GetAvgVelocity();
                avgVel.y = 0;
                float velocityReward = Vector3.Dot(directionToTarget.normalized, avgVel);
                AddReward(velocityReward * 0.3f);
            }

            // Reward and visualize moving feet towards the next step
            GameObject nextStep = null;
            if (targetController != null && targetController.steps != null)
            {
                foreach (var step in targetController.steps)
                {
                    if (step.transform.position.y > m_HighestStepY + 0.1f)
                    {
                        nextStep = step;
                        break;
                    }
                }
            }
            if (nextStep != null)
            {
                // --- ADD THIS VISUALIZER LOGIC BACK ---
                if(footTargetVisualizer != null) footTargetVisualizer.SetActive(true);
                Vector3 targetPoint = nextStep.transform.position + Vector3.up * (nextStep.transform.localScale.y);
                if(footTargetVisualizer != null) footTargetVisualizer.transform.position = targetPoint;
                // --- END OF ADDED LOGIC ---

                float closestFootDistance = Mathf.Min(Vector3.Distance(footL.position, targetPoint), Vector3.Distance(footR.position, targetPoint));
                float footProximityReward = 1.0f / (1.0f + closestFootDistance);
                AddReward(footProximityReward * 0.4f);
            }
            else
            {
                // --- ADD THIS VISUALIZER LOGIC BACK ---
                if(footTargetVisualizer != null) footTargetVisualizer.SetActive(false);
                // --- END OF ADDED LOGIC ---
            }

            // Reward gaining height
            var hipHeight = hips.position.y;
            var heightChange = hipHeight - m_LastHipYPosition;
            AddReward(heightChange * 0.05f);
            m_LastHipYPosition = hipHeight;

            
        }
    }

    //Returns the average velocity of all of the body parts
    //Using the velocity of the hips only has shown to result in more erratic movement from the limbs, so...
    //...using the average helps prevent this erratic movement
    Vector3 GetAvgVelocity()
    {
        Vector3 velSum = Vector3.zero;

        //ALL RBS
        int numOfRb = 0;
        foreach (var item in m_JdController.bodyPartsList)
        {
            numOfRb++;
            velSum += item.rb.velocity;
        }

        var avgVel = velSum / numOfRb;
        return avgVel;
    }

    //normalized value of the difference in avg speed vs goal walking speed.
    public float GetMatchingVelocityReward(Vector3 velocityGoal, Vector3 actualVelocity)
    {
        //distance between our actual velocity and goal velocity
        var velDeltaMagnitude = Mathf.Clamp(Vector3.Distance(actualVelocity, velocityGoal), 0, MTargetWalkingSpeed);

        //return the value on a declining sigmoid shaped curve that decays from 1 to 0
        //This reward will approach 1 if it matches perfectly and approach zero as it deviates
        return Mathf.Pow(1 - Mathf.Pow(velDeltaMagnitude / MTargetWalkingSpeed, 2), 2);
    }

    /// <summary>
    /// Agent touched the target
    /// </summary>
    public void TouchedTarget()
    {
        AddReward(1f);
    }

    public void SetTorsoMass()
    {
        m_JdController.bodyPartsDict[chest].rb.mass = m_ResetParams.GetWithDefault("chest_mass", 8);
        m_JdController.bodyPartsDict[spine].rb.mass = m_ResetParams.GetWithDefault("spine_mass", 8);
        m_JdController.bodyPartsDict[hips].rb.mass = m_ResetParams.GetWithDefault("hip_mass", 8);
    }

    public void SetResetParameters()
    {
        SetTorsoMass();
    }

    public void StartLandingPhase()
    {
        m_IsLanding = true;
    }

    // We'll call this from FootContact to end the landing phase
    public void LandedOnNewStep()
    {
        m_IsLanding = false;
    }

    public void ResetStairProgress()
    {
        m_HighestStepY = 0f;
    }

    public void ReachedWaypoint()
    {
        // Only switch phases if we are currently in the approaching phase.
        // This prevents it from triggering multiple times.
        if (m_IsApproachingStairs)
        {
            m_IsApproachingStairs = false;
            Debug.Log("Waypoint reached! Switching to final target.");
        }
    }

    public void StartApproachPhase()
    {
        m_IsApproachingStairs = true;
    }
}
