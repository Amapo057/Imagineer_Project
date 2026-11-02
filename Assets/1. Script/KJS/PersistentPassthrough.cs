using UnityEngine;

public class PersistentPassthrough : MonoBehaviour
{
    private static PersistentPassthrough passthroughInstance;

    private void Awake()
    {
        // 이미 인스턴스가 있다면 자기 파괴
        if(passthroughInstance != null && passthroughInstance != this)
        {
            Destroy(gameObject);
            return;
        }
        // 씬 넘어가도 파괴 안되게 설정
        passthroughInstance = this;
        DontDestroyOnLoad(gameObject);
    }
}
