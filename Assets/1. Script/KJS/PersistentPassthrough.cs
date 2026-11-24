using UnityEngine;

[DefaultExecutionOrder(-200)]
public class PersistentPassthrough : MonoBehaviour
{
    private static PersistentPassthrough passthroughInstance;

    private void Awake()
    {
        // 이미 인스턴스가 있다면 자기 파괴
        if(passthroughInstance != null && passthroughInstance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        passthroughInstance = this;
    }

    private void Start() => DontDestroyOnLoad(gameObject);
}
