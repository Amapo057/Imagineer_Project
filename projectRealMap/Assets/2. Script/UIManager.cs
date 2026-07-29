using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// 시작 화면과 종료 화면을 관리하고, 불을 켠 건물 수를 집계한다
//
// 이 스크립트는 반드시 꺼야 할 대상(Startmain) 바깥에 두어야 한다.
// 안에 두면 SetActive(false)로 자기 자신까지 꺼져서 다시 켤 방법이 없어진다.
public class UIManager : MonoBehaviour
{
    // 건물이 불을 켤 때 알려올 수 있도록 하나만 두고 공유한다
    public static UIManager Instance { get; private set; }

    [Header("시작 화면")]
    [Tooltip("START 버튼을 누르면 꺼질 오브젝트입니다. 자식 UI가 전부 함께 꺼집니다.")]
    [SerializeField] private GameObject startMain;

    [Header("종료 화면")]
    [Tooltip("게임이 끝나면 켜질 오브젝트입니다.")]
    [SerializeField] private GameObject endMain;

    [Tooltip("불을 켠 건물 수를 표시할 텍스트입니다.")]
    [SerializeField] private TMP_Text poweredCountText;

    [Tooltip("{0} 자리에 건물 수가 들어갑니다.")]
    [SerializeField] private string countMessageFormat = "불을 킨 건물은 {0}개입니다";

    [Tooltip("마지막 불을 켠 뒤 종료 화면이 뜰 때까지 기다리는 시간입니다.")]
    [SerializeField] private float endDelay = 3f;

    [Header("게임 종료 지점")]
    [Tooltip("종료 지점을 찾을 때 사용할 태그입니다. 시작할 때 한 번만 검색해 캐싱합니다.")]
    [SerializeField] private string distributionBoxTag = " distribution box";

    [Tooltip("배전함도 건물 수에 포함할지 여부입니다.")]
    [SerializeField] private bool countDistributionBox = false;

    [Header("시작할 때 켤 오브젝트")]
    [Tooltip("플레이어나 게임 UI처럼 시작 후에 등장해야 하는 것들을 넣습니다. 비워둬도 됩니다.")]
    [SerializeField] private GameObject[] activateOnStart;

    [Header("정지 설정")]
    [Tooltip("켜면 시작 화면이 떠 있는 동안 게임 시간을 멈춥니다. UI 클릭은 정지 중에도 동작합니다.")]
    [SerializeField] private bool pauseUntilStart = false;

    [Tooltip("켜면 종료 화면이 뜰 때 시간을 멈춥니다.")]
    [SerializeField] private bool pauseOnGameEnd = true;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = true;

    // 종료 지점 오브젝트들을 미리 찾아둔다
    // 이렇게 해두면 불을 켤 때마다 문자열 비교를 하지 않고 참조 비교만으로 판별할 수 있다
    private readonly HashSet<GameObject> distributionBoxObjects = new HashSet<GameObject>();

    private int poweredBuildingCount;
    private bool isGameEnded;
    private Coroutine endRoutine;

    public int PoweredBuildingCount => poweredBuildingCount;
    public bool IsGameEnded => isGameEnded;

    private void Awake()
    {
        Instance = this;

        // 이 스크립트가 꺼질 대상 안에 있으면 다시 켤 수 없게 되므로 미리 경고한다
        if (startMain != null && transform.IsChildOf(startMain.transform))
        {
            Debug.LogError(
                "UIManager가 " + startMain.name + " 안에 있습니다. " +
                "시작 화면을 끄면 이 스크립트도 함께 꺼져 되돌릴 수 없습니다. " +
                "바깥 오브젝트로 옮겨주세요."
            );
        }

        CacheDistributionBoxes();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // 멈춘 상태로 씬을 벗어나면 다음 씬까지 멈춘 채로 남으므로 되돌려 둔다
        Time.timeScale = 1f;
    }

    private void Start()
    {
        poweredBuildingCount = 0;
        isGameEnded = false;

        if (endMain != null)
        {
            endMain.SetActive(false);
        }

        ShowStartMain();
    }

    // 태그 검색은 시작할 때 한 번만 수행한다
    private void CacheDistributionBoxes()
    {
        distributionBoxObjects.Clear();

        if (string.IsNullOrEmpty(distributionBoxTag))
        {
            Debug.LogWarning("UIManager: 종료 지점 태그가 비어 있어 게임이 끝나지 않습니다.");
            return;
        }

        GameObject[] found;

        try
        {
            found = GameObject.FindGameObjectsWithTag(distributionBoxTag);
        }
        catch (UnityException)
        {
            // 등록되지 않은 태그를 넘기면 예외가 발생한다
            Debug.LogError(
                "UIManager: '" + distributionBoxTag + "' 태그가 등록되어 있지 않습니다. " +
                "Tag 이름의 공백까지 정확히 일치해야 합니다."
            );
            return;
        }

        foreach (GameObject target in found)
        {
            distributionBoxObjects.Add(target);
        }

        if (distributionBoxObjects.Count == 0)
        {
            Debug.LogWarning(
                "UIManager: '" + distributionBoxTag + "' 태그를 가진 오브젝트를 찾지 못했습니다. " +
                "배전함에 태그를 지정해주세요."
            );
        }
        else if (showDebugLog)
        {
            Debug.Log("[UI] 종료 지점 " + distributionBoxObjects.Count + "개를 찾았습니다.");
        }
    }

    // START 버튼의 On Click에 연결할 함수
    // 버튼 목록에 나타나려면 public / void / 매개변수 없음이어야 한다
    public void StartGame()
    {
        if (startMain == null)
        {
            Debug.LogError("UIManager: Start Main이 비어 있습니다. 인스펙터에서 Startmain을 넣어주세요.");
            return;
        }

        // 부모를 끄면 자식 UI가 전부 함께 꺼진다
        startMain.SetActive(false);

        SetActivateOnStartObjects(true);

        Time.timeScale = 1f;
    }

    // 다시 시작 화면으로 돌아올 때 사용한다
    public void ShowStartMain()
    {
        if (startMain == null)
        {
            Debug.LogError("UIManager: Start Main이 비어 있습니다. 인스펙터에서 Startmain을 넣어주세요.");
            return;
        }

        startMain.SetActive(true);

        SetActivateOnStartObjects(false);

        if (pauseUntilStart)
        {
            Time.timeScale = 0f;
        }
    }

    // BuildingController가 불을 켤 때 호출한다
    // 태그 비교 없이 미리 캐싱해둔 참조와만 대조한다
    public void NotifyBuildingPowered(GameObject source)
    {
        // 이미 끝난 뒤에 들어온 신호는 무시한다
        if (isGameEnded)
        {
            return;
        }

        bool isEndPoint = source != null && distributionBoxObjects.Contains(source);

        if (!isEndPoint || countDistributionBox)
        {
            poweredBuildingCount++;
        }

        if (showDebugLog)
        {
            Debug.Log("[UI] 불 켜진 건물 수: " + poweredBuildingCount);
        }

        if (isEndPoint)
        {
            EndGame();
        }
    }

    // 종료 처리를 시작한다
    // 마지막 불이 켜지는 연출을 볼 수 있도록 잠시 기다린 뒤 화면을 띄운다
    public void EndGame()
    {
        if (isGameEnded)
        {
            return;
        }

        // 여기서 바로 잠가야 대기 중에 다른 건물을 켜도 카운트가 더해지지 않는다
        isGameEnded = true;

        if (showDebugLog)
        {
            Debug.Log("[UI] 종료 지점 도달 / 최종 건물 수: " + poweredBuildingCount);
        }

        if (endRoutine != null)
        {
            StopCoroutine(endRoutine);
        }

        endRoutine = StartCoroutine(ShowEndScreenAfterDelay());
    }

    private IEnumerator ShowEndScreenAfterDelay()
    {
        if (endDelay > 0f)
        {
            // Realtime을 쓰면 시간이 멈춰 있어도 대기가 진행된다
            yield return new WaitForSecondsRealtime(endDelay);
        }

        if (poweredCountText != null)
        {
            poweredCountText.text = string.Format(countMessageFormat, poweredBuildingCount);
        }

        if (endMain != null)
        {
            endMain.SetActive(true);
        }
        else
        {
            Debug.LogWarning("UIManager: End Main이 비어 있어 종료 화면을 띄우지 못했습니다.");
        }

        if (pauseOnGameEnd)
        {
            Time.timeScale = 0f;
        }

        endRoutine = null;
    }

    // 다시하기 버튼의 On Click에 연결할 함수
    public void RestartGame()
    {
        // 정지 상태로 씬을 다시 불러오면 새 씬도 멈춘 채 시작되므로 먼저 되돌린다
        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    private void SetActivateOnStartObjects(bool isActive)
    {
        if (activateOnStart == null)
        {
            return;
        }

        foreach (GameObject target in activateOnStart)
        {
            if (target == null)
            {
                continue;
            }

            target.SetActive(isActive);
        }
    }
}
