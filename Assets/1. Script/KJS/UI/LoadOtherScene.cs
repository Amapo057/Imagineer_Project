using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadOtherScene : MonoBehaviour
{
    public void GoToStart()
    {
        GameNetworkManager.Instance.LeaveStartSceneRound();
        SceneManager.LoadScene("0 KJS_StartScene");
    }
    public void GoToMainGame()
    {
        var panel = FindFirstObjectByType<StartNetworkPanel>();
        if (panel != null)
        {
            panel.StartMainGame();
            return;
        }
        if (GameNetworkManager.Instance.DeveloperMode || GameNetworkManager.Instance.SendLoadMainGame())
            SceneManager.LoadScene("1 MainGame");
    }
    public void GoToViewMode()
    {
        SceneManager.LoadScene("2 ViewMode");
    }


    public void GoToEnd()
    {
        SceneManager.LoadScene("4 end");
    }

    public void ResetScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
