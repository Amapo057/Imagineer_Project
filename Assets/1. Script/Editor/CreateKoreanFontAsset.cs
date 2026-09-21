using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 프로젝트 기본 TMP 폰트(Liberation Sans SDF)가 한글 글리프를 지원하지 않아서, 카드 이름/능력
/// 텍스트에 한글을 쓰면 네모(tofu)로 깨져 보이는 문제를 해결하기 위한 에디터 전용 도구.
///
/// 프로젝트에 실제로 임포트되어 있는 "Noto Sans KR" 폰트 파일(글리프 외곽선 데이터를 직접
/// 담고 있는 진짜 폰트 애셋)을 소스로 사용해서 TMP Font Asset을 만듦. AtlasPopulationMode.Dynamic
/// 방식이라, 런타임에 실제로 필요한 글자만 그때그때 아틀라스에 채워 넣으므로 한글처럼 글자 수가
/// 아주 많은 폰트도 아틀라스 크기 걱정 없이 안전하게 쓸 수 있음.
///
/// (참고: 이전에는 OS의 "맑은 고딕"을 Font.CreateDynamicFontFromOSFont로 가리키는 방식을
/// 시도했지만, 이 방식으로 만들어진 Font 객체는 실제 글리프 외곽선 데이터에 접근할 수 없어서
/// "Unable to load font face... Make sure Include Font Data is enabled" 오류로 실패함.
/// 그래서 프로젝트에 직접 임포트된 실제 폰트 파일(Include Font Data가 기본적으로 켜져 있음)을
/// 쓰는 방식으로 변경함.)
///
/// 사용법: Unity 메뉴 CardGame > 한글 TMP 폰트 에셋 만들기 (Noto Sans KR)
/// </summary>
public static class CreateKoreanFontAsset
{
    private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/NotoSansKR-Regular.otf";
    private const string OutputPath = "Assets/TextMesh Pro/Fonts/NotoSansKR SDF.asset";

    [MenuItem("CardGame/한글 TMP 폰트 에셋 만들기 (Noto Sans KR)")]
    public static void CreateAsset()
    {
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            Debug.LogError($"[CreateKoreanFontAsset] '{SourceFontPath}' 경로에서 폰트 파일을 찾지 못했습니다. " +
                            "NotoSansKR-Regular.otf 파일이 해당 경로에 임포트되어 있는지 확인해주세요.");
            return;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,                                   // samplingPointSize
            9,                                     // atlasPadding
            GlyphRenderMode.SDFAA,
            1024, 1024,                             // atlas width/height (동적이라 시작 크기일 뿐, 필요하면 자동으로 늘어남)
            AtlasPopulationMode.Dynamic,
            true);                                  // enableMultiAtlasSupport

        if (fontAsset == null)
        {
            Debug.LogError("[CreateKoreanFontAsset] TMP_FontAsset 생성에 실패했습니다.");
            return;
        }

        fontAsset.name = "NotoSansKR SDF";

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutputPath));

        // 기존에 같은 경로에 만들어둔 게 있으면 지우고 새로 만듦(재실행 대비)
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(OutputPath);
        }

        AssetDatabase.CreateAsset(fontAsset, OutputPath);

        // 아틀라스 텍스처와 머티리얼도 같이 저장해둬야 나중에 다시 열었을 때도 정상 동작함
        if (fontAsset.atlasTexture != null)
        {
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
        }
        if (fontAsset.material != null)
        {
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[CreateKoreanFontAsset] 한글 TMP 폰트 에셋 생성 완료: {OutputPath}");

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = fontAsset;
    }
}
