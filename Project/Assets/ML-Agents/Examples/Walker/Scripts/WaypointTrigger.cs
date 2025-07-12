using UnityEngine;
using Unity.MLAgentsExamples;

public class WaypointTrigger : MonoBehaviour
{
    // Assign the WalkerAgent in the Inspector
    public WalkerAgent agent;

    private void OnTriggerEnter(Collider col)
    {
        // Check if it was an agent body part that entered the trigger
        if (col.CompareTag("AgentBody") || col.CompareTag("AgentFoot"))
        {
            // Tell the agent it has reached the waypoint
            if (agent != null)
            {
                agent.ReachedWaypoint();
            }
        }
    }
}