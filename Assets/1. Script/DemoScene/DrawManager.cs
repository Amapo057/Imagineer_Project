using Unity.VisualScripting;
using UnityEngine;

public class DrawManager : MonoBehaviour
{
    [SerializeField] private GameObject cardPrefabs;
    [SerializeField] private Transform handCheckers;

    private OnCardChecker[] checkersTrigger;

    void Awake()
    {
        checkersTrigger = handCheckers.GetComponentsInChildren<OnCardChecker>();
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
        Instantiate(cardPrefabs, emptyPosition, Quaternion.identity);
    
    }
}
