using UnityEngine;
using Unity.MLAgentsExamples;

public class BodyContact : MonoBehaviour
{
    public WalkerAgent agent; // Drag your main agent script here in the Inspector

    void OnCollisionEnter(Collision col)
    {
        // Penalize for non-foot body parts touching stairs
        if (col.gameObject.CompareTag("StairStep"))
        {
            // Give a small penalty for scrambling
            agent.AddReward(-0.2f);
            // // Reset the success streak because the agent failed
            // TargetController.ResetSuccessCounter();

            // Call the method on the agent's specific target controller instance
            if (agent != null && agent.targetController != null)
            {
                agent.targetController.ResetSuccessCounter();
            }

            // End the episode immediately
            agent.EndEpisode();
        }
    }
}