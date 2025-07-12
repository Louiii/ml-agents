using UnityEngine;

public class FootContact : MonoBehaviour
{
    public WalkerAgent agent;

    void OnCollisionEnter(Collision col)
    {
        if (col.gameObject.CompareTag("StairStep"))
        {
            // Get the world Y position of the step that was touched
            float stepY = col.transform.position.y;
            // Tell the agent about this achievement
            agent.AchievedNewStep(stepY);
        }
    }
}