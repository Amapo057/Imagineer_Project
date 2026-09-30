using UnityEngine;
using Meta.XR;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using System.Threading;
using System.Collections.Generic;

public class MarkerDetecter : MonoBehaviour
{
    [SerializeField] private PassthroughCameraAccess passthroughCameraAccess;
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

    // 마커의 크기를 실측한 18.7mm로 설정
    private float markerSize = 18.7f;
    // 크게 만들어긴 기준마커는 40mm로 설정
    private float middleAnchorMarkerSize = 40f;
    // 마커 절반사이즈 저장 변수
    private float markerHalfSize;
    private float middleAnchorMarkerHalfSize;

    // 마커 크기 나타내는 변수
    private Point3f[] objectPoints;
    private Point3f[] middleAnchorObjectPoints;

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
    private float detectionInterval;

    // 값 저장
    // 넘겨줄 때 포지션
    private Vector3 latestCameraPosition;
    private Quaternion latestCameraRotation;
    private DetectorParameters detectorParameters;
    private List<MarkerDetectionResult> markerDetectionResult = new List<MarkerDetectionResult>(); 

    void Start()
    {
        // 탐지 쿨다운 조절
        detectionInterval = 1f / detectionIntervalTime;
        // 마커 찾기용 변수 값 넣기
        detectorParameters = new DetectorParameters();
        dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);

        // 마커 절반크기 계산
        markerHalfSize = markerSize * 0.001f/2f;
        // 마커 네 꼭지점을 나타내는 배열
        // 평면이기에 z는 0으로 통일
        objectPoints = new[]
        {
            new Point3f(-markerHalfSize, markerHalfSize, 0), 
            new Point3f(markerHalfSize, markerHalfSize, 0), 
            new Point3f(markerHalfSize, -markerHalfSize, 0), 
            new Point3f(-markerHalfSize, -markerHalfSize, 0)
        };

        // 0번 마커용 정보 생성
        middleAnchorMarkerHalfSize = middleAnchorMarkerSize *  0.001f/2f;
        middleAnchorObjectPoints = new[]
        {
            new Point3f(-middleAnchorMarkerHalfSize, middleAnchorMarkerHalfSize, 0), 
            new Point3f(middleAnchorMarkerHalfSize, middleAnchorMarkerHalfSize, 0), 
            new Point3f(middleAnchorMarkerHalfSize, -middleAnchorMarkerHalfSize, 0), 
            new Point3f(-middleAnchorMarkerHalfSize, -middleAnchorMarkerHalfSize, 0)
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
    // 변환 완료한 좌표 및 회전 반환
    // out을 사용해 조건에 따라 result의 값을 다르게 배정
    public bool TryGetMarkerResult(out List<MarkerDetectionResult> result)
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

    // opencv로 마커 탐색하는 쓰레드용 함수
    void CVLoop()
    {
        // 종료용 변수 isRunnign사용
        while (isRunning)
        {
            Color32[] pixels = null;
            int width = 0;
            int height = 0;
            Vector3 cameraPosition = Vector3.zero;
            Quaternion cameraRotation = Quaternion.identity;

            // 메인 스레드에서 카메라 정보 받기
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
            // 받은 패스스루 정보 활용해 opencv용으로 전처리
            using Mat gray = PreparationCV(pixels, width, height);
            // 마커 찾는 함수로 마커 정보 받기
            var result = DetectMarker(gray);
            // 값 이상 유무 검사
            if (result.ids != null && result.ids.Length > 0 && result.tvec != null && result.rvec != null)
            {
                // 마커 정보용 객체 리스트 생성해 정보 담아 메인 스레드로 넘기기
                var resultInstance = new List<MarkerDetectionResult>(result.ids.Length);
                for(int i = 0; i < result.ids.Length; i++)
                {
                    // 첫번째 값에만 카메라 정보 담기
                    if (i == 0)
                    {
                        resultInstance.Add(new MarkerDetectionResult{id = result.ids[i], tvec = result.tvec[i], rvec = result.rvec[i], cameraPosition = cameraPosition, cameraRotation = cameraRotation});
                    }
                    // 이후 마커는 마커 정보만 삽입
                    else
                    {
                        resultInstance.Add(new MarkerDetectionResult{id = result.ids[i], tvec = result.tvec[i], rvec = result.rvec[i]});
                    }
                }
                // 정리한 마커 정보 반환
                lock (resultLock)
                {
                    markerDetectionResult = resultInstance;

                    hasNewResult = true;
                }
            }
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
    (int[] ids, double[][] tvec, double[][] rvec) DetectMarker(Mat gray)
    {
        // 검출한 정보 담기용 변수
        int[] ids;
        Point2f[][] corners;
        Point2f[][] rejected;

        // 마커 감지
        CvAruco.DetectMarkers(gray, dictionary, out corners, out ids, detectorParameters, out rejected);

        // solvePnP 계산 결과 저장용 2차원 배열 생성
        double[][] tvec = new double[ids.Length][];
        double[][] rvec = new double[ids.Length][];

        if (ids.Length > 0 && corners.Length > 0)
        {
            for(int i = 0; i < ids.Length; i++)
            {
                // 2차원 배열 내부 배열 크기 할당
                tvec[i] = new double[3];
                rvec[i] = new double[3];

                // 마커 번호 이용해 사용할 마커 크기 변경 (0번은 중앙 기준마커라 더 큰 사이즈 사용)
                var targetObjectPoints = ids[i] == 0 ? middleAnchorObjectPoints : objectPoints;

                // 마커 정보, 코너 정보, 카메라 정보, 외곡정보, 출력받을 변수
                Cv2.SolvePnP(targetObjectPoints, corners[i], cameraMatrix, distCoeffs, ref rvec[i], ref tvec[i]);
            }
        }
        // 계산된 마커 정보 반환
        return (ids, tvec, rvec);
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