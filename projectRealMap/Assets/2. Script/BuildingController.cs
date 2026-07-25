using System.Collections.Generic;
using UnityEngine;

public class BuildingController : MonoBehaviour
{
    // 에디터에서 미리 생성한 건물도 참조를 유지하도록 직렬화
    [SerializeField] private List<MeshRenderer> rendererList = new List<MeshRenderer>();

    private readonly string emissionController = "_EmissionIntensity";

    public void RegisterRenderer(MeshRenderer renderer)
    {
        // 렌더러 있는지 검사
        if (renderer == null) return;
        // 임시로 마테리얼 잡음
        if (!rendererList.Contains(renderer))
        {
            rendererList.Add(renderer);
        }
    }

    public void SetEmission(float intensity)
    {
        // 혹시 렌더러가 비어있으면 돌려보냄
        if (rendererList.Count == 0) return;

        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
        
        foreach(MeshRenderer renderer in rendererList)
        {
            // 비어있는지 검사
            if (renderer == null) continue;

            renderer.GetPropertyBlock(propBlock);

            propBlock.SetFloat(emissionController, intensity);

            renderer.SetPropertyBlock(propBlock);
        }
    }
    
}
