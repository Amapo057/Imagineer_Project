using UnityEngine;

/// <summary>
/// 카드가 소속되는 "클래스"(진영/직업) 데이터.
/// 특정 클래스에 속하지 않는 카드(중립 카드)는 CardData의 cardClass를 비워두면 됨.
/// Unity 메뉴: Assets > Create > CardGame > Card Class
/// </summary>
[CreateAssetMenu(fileName = "NewCardClass", menuName = "CardGame/Card Class")]
public class CardClass : ScriptableObject
{
    [Tooltip("클래스 이름 (예: 전사, 마법사)")]
    public string className;

    [Tooltip("클래스를 상징하는 색상 (UI · 홀로그램 테마용)")]
    public Color themeColor = Color.white;

    [Tooltip("클래스 설명 (선택)")]
    [TextArea]
    public string description;
}
