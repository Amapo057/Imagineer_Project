using UnityEngine;

public class BuildingPowerReceiver : MonoBehaviour, IElectricInteractable
{
    [Header("전기 공급 상태")]
    [SerializeField] private bool isPowered = false;

    [Header("건물 외형")]
    [SerializeField] private Renderer targetRenderer;

    [Header("전기 공급 전 색상")]
    [SerializeField] private Color unpoweredColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    [Header("전기 공급 후 색상")]
    [SerializeField] private Color poweredColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Header("건물 조명")]
    [SerializeField] private Light buildingLight;

    [Header("상태 유지")]
    [Tooltip("체크하면 한 번 전기가 들어온 뒤 계속 켜진 상태로 유지됩니다.")]
    [SerializeField] private bool keepPoweredState = true;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = true;

    public bool IsPowered => isPowered;

    private void Reset()
    {
        targetRenderer = GetComponent<Renderer>();
        buildingLight = GetComponentInChildren<Light>();
    }

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (buildingLight == null)
        {
            buildingLight = GetComponentInChildren<Light>();
        }

        ApplyPowerState();
    }

    public void OnElectricContact(ElectricInteractionDetector electricSource)
    {
        PowerOn();
    }

    public void PowerOn()
    {
        if (isPowered && keepPoweredState)
        {
            if (showDebugLog)
            {
                Debug.Log("[Building Power] 이미 전기가 켜진 건물입니다: " + gameObject.name);
            }

            return;
        }

        isPowered = true;
        ApplyPowerState();

        if (showDebugLog)
        {
            Debug.Log("[Building Power] 건물 전기 공급 완료: " + gameObject.name);
        }
    }

    public void PowerOff()
    {
        if (keepPoweredState)
        {
            return;
        }

        isPowered = false;
        ApplyPowerState();

        if (showDebugLog)
        {
            Debug.Log("[Building Power] 건물 전기 공급 해제: " + gameObject.name);
        }
    }

    private void ApplyPowerState()
    {
        if (targetRenderer != null)
        {
            Color targetColor = isPowered ? poweredColor : unpoweredColor;

            Material materialInstance = targetRenderer.material;
            materialInstance.color = targetColor;

            if (materialInstance.HasProperty("_EmissionColor"))
            {
                if (isPowered)
                {
                    materialInstance.EnableKeyword("_EMISSION");
                    materialInstance.SetColor("_EmissionColor", poweredColor * 1.5f);
                }
                else
                {
                    materialInstance.SetColor("_EmissionColor", Color.black);
                }
            }
        }

        if (buildingLight != null)
        {
            buildingLight.enabled = isPowered;
        }
    }
}