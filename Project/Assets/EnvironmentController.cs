using UnityEngine;

public class EnvironmentController : MonoBehaviour
{
    public Transform staircase;
    public float spawnRadius = 10f;
    public float minSpawnRadius = 6f;
    public float radius = 20.0f;
    private static int trainingPhase = 3; // originally set to 1, but it's better to start at 3.

    // This method can be called by the TargetController to level up the environment.
    public static void AdvanceTrainingPhase()
    {
        if (trainingPhase < 3)
        {
            trainingPhase++;
            Debug.LogWarning($"TRAINING PHASE ADVANCED TO: {trainingPhase}");
        }
    }

    public void ResetEnvironment()
    {
        Vector3 randomDirection;
        Quaternion lookRotation;

        switch (trainingPhase)
        {
            // PHASE 1: Fixed position and orientation.
            case 1:
                staircase.position = this.transform.position + new Vector3(0.0f, 0.25f, radius);
                staircase.rotation = Quaternion.Euler(0, -90, 0);
                break;

            // PHASE 2: Random position, fixed orientation.
            case 2:
                // Find a random direction on the positive-Z semicircle.
                Vector2 randomCirclePos = Random.insideUnitCircle.normalized;
                randomDirection = new Vector3(randomCirclePos.x, 0, Mathf.Abs(randomCirclePos.y)).normalized;
                staircase.position = this.transform.position + (randomDirection * radius);

                // Orient the stairs to point along that direction.
                lookRotation = Quaternion.LookRotation(randomDirection);
                staircase.rotation = lookRotation * Quaternion.Euler(0, -90, 0);
                break;

            // PHASE 3: Random position, random (but constrained) orientation.
            case 3:
                // Find a random direction on the positive-Z semicircle.
                randomCirclePos = Random.insideUnitCircle.normalized;
                randomDirection = new Vector3(randomCirclePos.x, 0, Mathf.Abs(randomCirclePos.y)).normalized;
                staircase.position = this.transform.position + (randomDirection * radius);

                // Find a random rotation that generally faces away from the center.
                Quaternion randomRotation;
                do
                {
                    randomRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    // Check if the stairs' "forward" (local X-axis) has a positive dot product with the direction away from the center.
                } while (Vector3.Dot(randomDirection, randomRotation * Vector3.right) < 0);
                staircase.rotation = randomRotation;
                break;
        }
    }

    public void RepositionStaircase(Transform topStepOfOldStairs)
    {
        // The first time we complete a staircase, we advance the training phase.
        if (trainingPhase == 1)
        {
            AdvanceTrainingPhase();
        }

        // Find the direction of the current staircase
        Vector3 currentStaircaseVector = staircase.position - this.transform.position;
        currentStaircaseVector.y = 0; // Ignore height
        Vector3 currentDirection = currentStaircaseVector.normalized;

        // Find a new random direction on the OPPOSITE side of the circle
        Vector3 newDirection;
        int attempts = 0;
        do
        {
            Vector2 randomCirclePos = Random.insideUnitCircle.normalized;
            newDirection = new Vector3(randomCirclePos.x, 0, randomCirclePos.y);
            attempts++;
            // The dot product will be negative if the new direction is in the opposite hemisphere
        } while (Vector3.Dot(currentDirection, newDirection) > 0 && attempts < 100);


        // Set the new position and a completely random rotation
        staircase.position = this.transform.position + (newDirection * radius);
        staircase.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
    }
}