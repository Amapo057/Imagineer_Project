using UnityEngine;
using TMPro;

public class CardFieldMover : MonoBehaviour
{
    private SpawnModel spawnModel;
    private SpawnUI spawnUI;
    public TMP_Text idText;
    public TMP_Text attackText;
    public TMP_Text healthText;
    public TMP_Text manaText;

    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Field"))
        {
            Vector3 pos = other.transform.position;
            pos.y += 0.05f;

            transform.position = pos;

            spawnModel.SpawnCardModel(transform.position, idText.text);
            spawnUI.SpawnCardUI(transform.position, healthText.text, attackText.text);
            
        }
    }

    public void SetSpawnModel(SpawnModel spawnModel)
    {
        this.spawnModel = spawnModel;
    }
    public void SetSpawnUI(SpawnUI spawnUI)
    {
        this.spawnUI = spawnUI;
    }
}