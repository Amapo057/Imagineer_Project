using UnityEngine;

public class RockPaperScissors : MonoBehaviour
{
    [SerializeField] TMPro.TextMeshProUGUI message;
    [SerializeField] TMPro.TextMeshProUGUI RPSState;
    private float sceneTime = 0f;
    private float readyTime = 5f;
    private float playTime = 0.5f;
    private float checkTime = 0.2f;
    private bool hasPoseDetected = false;

    private RPSPose testEnemyPose = RPSPose.Scissors;

    public enum RPSPose
    {
        Rock,
        Paper,
        Scissors
    }

    private RPSPose currentPose;
    private RPSPose detectedPose = RPSPose.Scissors;

    // Update is called once per frame
    void Update()
    {
        sceneTime += Time.deltaTime;
        RPSState.text = currentPose.ToString();
        if (!hasPoseDetected){
            if (sceneTime <= readyTime) return;
            if (sceneTime < readyTime + playTime) message.text = "가위";
            else if (sceneTime < readyTime + playTime *2) message.text = "바위";
            else if (sceneTime < readyTime + playTime *3) message.text = "보";
            else if (sceneTime >= readyTime + playTime *3 + checkTime)
            {
                detectedPose = currentPose;
                hasPoseDetected = true;
            }
        }
        if (detectedPose == testEnemyPose)
        {
            message.text = "비겼습니다\n다시 진행합니다";
            sceneTime = 0f;
            hasPoseDetected = false;
        }
        else if ((detectedPose == RPSPose.Rock && testEnemyPose == RPSPose.Scissors) || 
        (detectedPose == RPSPose.Paper && testEnemyPose == RPSPose.Rock) ||
        (detectedPose == RPSPose.Scissors && testEnemyPose == RPSPose.Paper))
        {
            message.text = "이겼습니다\n선공으로 시작합니다";
        }
        else
        {
            message.text = "졌습니다\n후공으로 시작합니다";
        }

        
    }

    public void RockDetected()
    {
        currentPose = RPSPose.Rock;
    }
    public void PaperDetected()
    {
        currentPose = RPSPose.Paper;
    }
    public void ScissorsDetected()
    {
        currentPose = RPSPose.Scissors;
    }
    public void RPSPoseReleased()
    {
        currentPose = RPSPose.Scissors;
    }
}
