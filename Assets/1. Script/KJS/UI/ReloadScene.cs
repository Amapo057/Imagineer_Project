using UnityEngine;
using UnityEngine.SceneManagement;

public class ReloadScene : MonoBehaviour
{
    public void RestScene()
    {
        // static 정보 초기화
        VRGameSession.ResetGame();
        // 현재 씬 정보 받아와 초기화
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        
    }
}