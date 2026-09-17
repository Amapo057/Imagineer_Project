using UnityEngine;

public class SpawnModel : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpawnCardModel(Vector3 pos, string id)
    {
        Debug.Log($"Position: {pos}\nID: {id}");
        // 카드 필드 배치시 해당 함수 알아서 호출
        // prefabs 폴더 내부 모델을 인스턴스로 호출할 수 있도록 구성
        // 인자로 전달받은 pos를 활용해 해당 위치에 모델을 소환
        // id를 사용해 id마다 다른 모델 소환할 수 있도로 구성
    }

    
}
