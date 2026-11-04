using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadOtherScene : MonoBehaviour
{
    public void GoToStart()
    {
        SceneManager.LoadScene("0 KJS_StartScene");
    }
    public void GoToLobby()
    {
        SceneManager.LoadScene("1 KJS_Lobby");
    }

    public void GoToAnchorInit()
    {
        SceneManager.LoadScene("2 KJS_AnchorInit");
    }

    public void GoToMainGame()
    {
        SceneManager.LoadScene("3 MainGame");
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
