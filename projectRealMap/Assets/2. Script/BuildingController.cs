using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 건물의 불을 켜고 끄는 컨트롤러
//
// IElectricInteractable을 구현하므로 플레이어의 ElectricInteractionDetector가
// 충돌 시 자동으로 OnElectricContact를 호출해준다.
// 색상은 MaterialPropertyBlock으로 넣기 때문에 머티리얼이 복제되지 않고 배칭도 유지된다.
public class BuildingController : MonoBehaviour, IElectricInteractable
{
    // 에디터에서 미리 생성한 건물도 참조를 유지하도록 직렬화
    [SerializeField] private List<MeshRenderer> rendererList = new List<MeshRenderer>();

    [Header("불이 켜졌을 때 밝기")]
    [Tooltip("셰이더의 EmissionIntensity에 넣을 값입니다.")]
    [SerializeField] private float poweredEmission = 10f;

    [Header("불이 꺼졌을 때 밝기")]
    [SerializeField] private float unpoweredEmission = 0f;

    [Header("켜지는 연출")]
    [Tooltip("끄면 즉시 최대 밝기가 됩니다.")]
    [SerializeField] private bool useFadeIn = true;

    [Tooltip("불이 서서히 켜지는 데 걸리는 시간입니다.")]
    [SerializeField] private float fadeDuration = 0.6f;

    [Header("상태 유지")]
    [Tooltip("체크하면 한 번 켜진 뒤 다시 꺼지지 않습니다.")]
    [SerializeField] private bool keepPoweredState = true;

    [Header("현재 상태")]
    [SerializeField] private bool isPowered = false;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = false;

    private readonly string emissionController = "_EmissionIntensity";

    private float currentEmission;
    private Coroutine fadeRoutine;

    public bool IsPowered => isPowered;

    private void Awake()
    {
        // 생성기를 거치지 않고 직접 만든 건물도 동작하도록 보조 수집
        if (rendererList == null || rendererList.Count == 0)
        {
            rendererList = new List<MeshRenderer>(GetComponentsInChildren<MeshRenderer>());
        }

        currentEmission = isPowered ? poweredEmission : unpoweredEmission;
        ApplyEmission(currentEmission);
    }

    public void RegisterRenderer(MeshRenderer renderer)
    {
        // 렌더러 있는지 검사
        if (renderer == null) return;

        if (rendererList == null)
        {
            rendererList = new List<MeshRenderer>();
        }

        if (!rendererList.Contains(renderer))
        {
            rendererList.Add(renderer);
        }
    }

    // 플레이어가 부딪히면 ElectricInteractionDetector가 이 함수를 호출한다
    public void OnElectricContact(ElectricInteractionDetector electricSource)
    {
        PowerOn();
    }

    public void PowerOn()
    {
        // 이미 켜져 있고 상태를 유지하는 설정이면 연출을 다시 재생하지 않는다
        if (isPowered && keepPoweredState)
        {
            return;
        }

        isPowered = true;

        // 실제로 켜지는 순간에만 집계한다
        // 이미 켜진 건물에 다시 상호작용해도 위에서 걸러지므로 중복 계산되지 않는다
        if (UIManager.Instance != null)
        {
            UIManager.Instance.NotifyBuildingPowered(gameObject);
        }

        if (showDebugLog)
        {
            Debug.Log("[Building Light] 불 켜짐: " + gameObject.name);
        }

        StartEmissionTransition(poweredEmission);
    }

    public void PowerOff()
    {
        if (keepPoweredState)
        {
            return;
        }

        isPowered = false;

        if (showDebugLog)
        {
            Debug.Log("[Building Light] 불 꺼짐: " + gameObject.name);
        }

        StartEmissionTransition(unpoweredEmission);
    }

    private void StartEmissionTransition(float targetEmission)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        // 연출을 끄거나 오브젝트가 비활성이면 코루틴을 돌릴 수 없으므로 즉시 반영
        if (!useFadeIn || fadeDuration <= 0f || !gameObject.activeInHierarchy)
        {
            currentEmission = targetEmission;
            ApplyEmission(currentEmission);
            return;
        }

        fadeRoutine = StartCoroutine(FadeEmission(targetEmission));
    }

    private IEnumerator FadeEmission(float targetEmission)
    {
        float startEmission = currentEmission;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / fadeDuration);
            currentEmission = Mathf.Lerp(startEmission, targetEmission, t);

            ApplyEmission(currentEmission);

            yield return null;
        }

        currentEmission = targetEmission;
        ApplyEmission(currentEmission);

        fadeRoutine = null;
    }

    // 기존 외부 호출용 함수
    // 밝기를 직접 지정하고 싶을 때 사용한다
    public void SetEmission(float intensity)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        currentEmission = intensity;
        ApplyEmission(currentEmission);
    }

    private void ApplyEmission(float intensity)
    {
        // 혹시 렌더러가 비어있으면 돌려보냄
        if (rendererList == null || rendererList.Count == 0) return;

        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        foreach (MeshRenderer renderer in rendererList)
        {
            // 비어있는지 검사
            if (renderer == null) continue;

            // 기존에 넣어둔 건물 색상이 지워지지 않도록 현재 값을 먼저 가져온다
            renderer.GetPropertyBlock(propBlock);

            propBlock.SetFloat(emissionController, intensity);

            renderer.SetPropertyBlock(propBlock);
        }
    }
}
