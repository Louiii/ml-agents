using UnityEngine;
using Unity.MLAgentsExamples;

public class FallDetector : MonoBehaviour
{
    public WalkerAgent agent;

    void OnCollisionEnter(Collision col)
    {
        if (col.gameObject.CompareTag("ground"))
        {
            // Tell our specific agent that it failed
            if (agent != null && agent.targetController != null)
            {
                // The agent will tell its own target controller to reset the counter
                agent.targetController.ResetSuccessCounter();
            }
            // End the episode for the agent
            agent.EndEpisode();
        }
    }
}