using UnityEngine;

public class SpawnUI : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpawnCardUI(Vector3 pos, string hp, string atk)
    {
        Debug.Log($"Position: {pos}\nHP: {hp}\nATK: {atk}");
        // 카드 필드 배치시 해당 함수 알아서 호출
        // 카드 모델 옆에 보기 편하게 체력과 공격력을 띄우기, 모양은 노션 일정 페이지 참조
        // 인자로 전달받은 pos와 hp, atk를 활용해 해당 위치에 ui를 소환
        // ui는 카메라를 항상 바라보도록 해야함. 현재는 카메라 고정이라 나중에 테스트하기
    }
}
