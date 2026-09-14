using UnityEngine;
using Meta.XR;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using System.Threading;

public class MarkerDetecter : MonoBehaviour
{
    [SerializeField] private PassthroughCameraAccess passthroughCameraAccess;
    [SerializeField] private float markerSize = 19f;
    [SerializeField] private float detectionIntervalTime = 8f;

    // [SerializeField] private RawImage rawImage;

    // 쓰레드용 작동 변수
    private Thread cvThread;
    private bool isRunning;

    // 쓰레드 넘겨줄 변수
    private Color32[] latestPixels;
    private int imageWidth;
    private int imageHeight;
    // 새 프레임 정보 넘겼는지 여부
    private bool hasNewFrame = false;
    private readonly object frameLock = new object();

    // 쓰레드에서 받을 변수
    private bool hasNewResult = false;
    private readonly object resultLock = new object();


    // 마커의 크기인 19mm의 절반 사이즈를 기준으로 삼음
    private float markerHalfSize = 0.019f/2f;

    // 마커 크기 나타내는 변수
    private Point3f[] objectPoints;

    // 카메라 파라미터 나타내는 변수
    private double[,] cameraMatrix;
    // 렌즈 왜곡정보 변수0
    private double[] distCoeffs;
    // 이미지 크기
    private int width = 1280;
    private int height = 1280;
    
    private Dictionary dictionary;

    // 탐지 타이머
    private float timer;
    // 마커 탐지 기준(1f/목표 주사율f)
    private float detectionInterval = 1f/8f;

    // 값 저장
    // 넘겨줄 때 포지션
    private Vector3 latestCameraPosition;
    private Quaternion latestCameraRotation;
    private DetectorParameters detectorParameters;
    private MarkerDetectionResult markerDetectionResult; 

    void Start()
    {
        // 마커 절반크기 계산
        markerHalfSize = markerSize * 0.001f/2f;
        // 탐지 쿨다운 조절
        detectionInterval = 1f / detectionIntervalTime;

        // 마커 찾기용 변수 값 넣기
        detectorParameters = new DetectorParameters();
        dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);

        // 마커 네 꼭지점을 나타내는 배열
        // 평면이기에 z는 0으로 통일
        objectPoints = new[]
        {
            new Point3f(-markerHalfSize, markerHalfSize, 0), 
            new Point3f(markerHalfSize, markerHalfSize, 0), 
            new Point3f(markerHalfSize, -markerHalfSize, 0), 
            new Point3f(-markerHalfSize, -markerHalfSize, 0)
        };

        
        // quest 카메라 내부 파라이터 받기
        var intrinsics = passthroughCameraAccess.Intrinsics;

        // 메타 패스스루 비율은 1280x1280이나, 카메라로 받는 실제 비율이 다를경우 그 값을 계산해 추후 카메라 정보 보정에 사용
        float cropY = (intrinsics.SensorResolution.y - passthroughCameraAccess.CurrentResolution.y) / 2f;
        
        // openCV용 카메라 정보 행렬 만들기
        cameraMatrix = new double[,]
        {
            {intrinsics.FocalLength.x, 0, intrinsics.PrincipalPoint.x},
            {0, intrinsics.FocalLength.y, intrinsics.PrincipalPoint.y - cropY},
            {0, 0, 1}
        };

        width = passthroughCameraAccess.CurrentResolution.x;
        height = passthroughCameraAccess.CurrentResolution.y;

        distCoeffs = new double[] {0, 0, 0, 0};

        // 스레드 시작
        isRunning = true;
        cvThread = new Thread(CVLoop);
        cvThread.Start();

    }


    void Update()
    {
        // 프레임 타이머
        timer += Time.deltaTime;
        // 패스쓰루 카메라 작동 여부 확인
        if (passthroughCameraAccess == null) return;
        if (!passthroughCameraAccess.IsPlaying) return;

        // 프레임 사용 여부 체크
        bool needsFrame;
        lock (frameLock)
        {
            needsFrame = !hasNewFrame;
        }

        // 지정한 주사율로 제한, 프레임이 사용되었을경우 정보 전달
        if (timer >= detectionInterval && needsFrame)
        {
            // passthroug같은 meta sdk는 메인 스레드에서 사용
            // 카메라로부터 정보 받기
            var colors = passthroughCameraAccess.GetColors();
            var cameraPose = passthroughCameraAccess.GetCameraPose();

            // // 컬러32 배열형태로 변형
            Color32[] pixels = colors.ToArray();

            lock (frameLock)
            {
                latestPixels = pixels;
                imageWidth = width;
                imageHeight = height;

                latestCameraPosition = cameraPose.position;
                latestCameraRotation = cameraPose.rotation;

                hasNewFrame = true;
            }
            timer = 0;
        }
    }

    // passthrough로 받은 영상 opencv용으로 전처리
    Mat PreparationCV(Color32[] pixels, int width, int height)
    {
        // 이미지를 cv가 읽을 수 있는 mat형태로 변형
        // mat은 네이티브 메모리를 사용하기때문에 using을 붙여 함수 종료시 알아서 제거도록 사용
        using Mat frame = OpenCvSharp.Unity.PixelsToMat(pixels, width, height);

        // 흑백 변환을 담을 인스턴트 생성
        Mat gray = new Mat();

        // 흑백으로 변경
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

        return gray;
    }

    // 전처리된 흑백 영상을 받아 마커 탐색후 마커 아이디와 위치, 회전 반환
    (int[] ids, double[] tvec, double[] rvec) DetectMarker(Mat gray)
    {
        // 검출한 정보 담기용 변수
        int[] ids;
        Point2f[][] corners;
        Point2f[][] rejected;

        // 마커 감지
        CvAruco.DetectMarkers(gray, dictionary, out corners, out ids, detectorParameters, out rejected);

        // 마커 위치
        double[] tvec = null;
        // 마커 회전 정보
        double[] rvec = null;

        if (ids.Length > 0 && corners.Length > 0)
        {
            // 마커 정보, 코너 정보, 카메라 정보, 외곡정보, 출력받을 변수
            Cv2.SolvePnP(objectPoints, corners[0], cameraMatrix, distCoeffs, ref rvec, ref tvec);
        }        
        return (ids, tvec, rvec);
    }

    // 변환 완료한 좌표 및 회전 반환
    // out을 사용해 조건에 따라 result의 값을 다르게 배정
    public bool TryGetMarkerResult(out MarkerDetectionResult result)
    {
        lock (resultLock)
        {
            // 새 프레임이 없으면 false
            if (!hasNewResult)
            {
                result = null;
                return false;
            }
            result = markerDetectionResult;
            hasNewResult= false;

            return true;
        }
    }

    void CVLoop()
    {
        while (isRunning)
        {
            Color32[] pixels = null;
            int width = 0;
            int height = 0;
            Vector3 cameraPosition = Vector3.zero;
            Quaternion cameraRotation = Quaternion.identity;

            lock (frameLock)
            {
                if (hasNewFrame)
                {
                    pixels = latestPixels;
                    width = imageWidth;
                    height = imageHeight;

                    cameraPosition = latestCameraPosition;
                    cameraRotation = latestCameraRotation;
                    
                    hasNewFrame = false;
                }
            }

            // 프레임 정보 없을시 넘기기
            if(pixels == null)
            {
                Thread.Sleep(1);
                continue;
            }

            using Mat gray = PreparationCV(pixels, width, height);
            // 마커 찾는 함수로 마커 정보 받기
            var result = DetectMarker(gray);
            // 값 이상 유무 검사
            if (result.ids != null && result.ids.Length > 0 && result.tvec != null && result.rvec != null)
            {
                // 마커 정보용 객체 생성해 정보 담아 메인 스레드로 넘기기
                var resultInstance = new MarkerDetectionResult
                {
                    ids = result.ids, 
                    tvec = result.tvec, 
                    rvec = result.rvec, 
                    cameraPosition = cameraPosition, 
                    cameraRotation = cameraRotation
                };
                lock (resultLock)
                {
                    markerDetectionResult = resultInstance;

                    hasNewResult = true;
                }
            }
        }
    }
    // 코드 종료시 자동 호출
    void OnDestroy()
    {
        // 스레드 종료
        isRunning = false;
        if(cvThread != null && cvThread.IsAlive)
        {
            // cv 종료시 까지 500ms만 대기
            cvThread.Join(500);
        }
    }
}
