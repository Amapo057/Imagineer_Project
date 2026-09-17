using Unity.VisualScripting;
using UnityEngine;
using TMPro;

public class CardDrawer : MonoBehaviour
{
    [SerializeField] private GameObject cardPrefabs;
    [SerializeField] private Transform handCheckers;
    [SerializeField] private SpawnModel spawnModel;
    [SerializeField] private SpawnUI spawnUI;

    private CardTrigger[] checkersTrigger;

    private int cardCount = 0;
    CardStats[] cardInfo =
    {
        new(2, 2, 1, 4),
        new(3, 3, 2, 6),
        new(5, 5, 2, 8),
        new(8, 8, 5, 10)
    };

    public struct CardStats
    {
        public int attack;
        public int health;
        public int cost;
        public int id;
        public CardStats(int cardAtk, int cardHp, int cardCost, int cardId)
        {
            attack = cardAtk;
            health = cardHp;
            cost = cardCost;
            id = cardId;
        }
        
    }

    void Awake()
    {
        checkersTrigger = handCheckers.GetComponentsInChildren<CardTrigger>();

        
    }

    public void OnDrawButtonClick()
    {
        Vector3 emptyPosition = Vector3.zero;
        int i = 0;
        foreach(var checker in checkersTrigger)
        {
            if (!checker.IsTriggered)
            {
                emptyPosition = checker.transform.position;
                i = 0;
                break;
            }
            i++;
        }
        if (i > 7)
        {
            return;
        }
        // 프리팹 사용해 인스턴스 생성
        GameObject card = Instantiate(cardPrefabs, emptyPosition, Quaternion.identity);
        CardFieldMover mover = card.GetComponent<CardFieldMover>();
        mover.SetSpawnModel(spawnModel);
        mover.SetSpawnUI(spawnUI);
        if(cardCount < 3)
        {
            cardCount++;
        }
        // Debug.Log($"CardCount: {cardCount}");
        // 인스턴스로부터 텍스트 정보 받은 후 변경
        CardFieldMover cardText = card.GetComponent<CardFieldMover>();
    
        cardText.attackText.text = cardInfo[cardCount].attack.ToString();
        cardText.healthText.text = cardInfo[cardCount].health.ToString();
        cardText.manaText.text = cardInfo[cardCount].cost.ToString();
        cardText.idText.text = cardInfo[cardCount].id.ToString();
    }
}
