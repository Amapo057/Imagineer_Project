using UnityEngine;

public sealed class ManaManager : MonoBehaviour
{
    [SerializeField] private Transform myMana;
    [SerializeField] private Transform opponentMana;
    [SerializeField] private GameObject manaPrefab;
    [SerializeField] private Vector3 startPosition = new Vector3(0.0775f, 0f, 0.0102f);
    [SerializeField] private float spacing = 0.0242f;
    [SerializeField] private float zigzag = 0.0232f;
    [SerializeField] private float gemScale = 0.1f;
    [SerializeField] private Shader fadeShader;
    [SerializeField] private MeshRenderer spendGlowPrefab;
    [SerializeField, Min(0.01f)] private float effectDuration = 14f / 30f;
    [SerializeField, Min(0f)] private float liftHeight = 0.0262f;
    [SerializeField] private Vector3 liftTilt = new Vector3(-29.4f, -4f, -13.4f);
    [SerializeField, Min(0f)] private float spendEmission = 2.2f;

    private sealed class Gem
    {
        public Transform root;
        public Renderer renderer;
        public MeshRenderer glow;
        public Vector3 position;
        public Quaternion rotation;
        public float lift = 1f;
        public bool fading;
    }

    private const int GemMaterialIndex = 1;
    private static readonly int Opacity = Shader.PropertyToID("_Opacity");
    private static readonly int Emission = Shader.PropertyToID("_Emission_Color");
    private static readonly Color BlueLight = new Color(0.14f, 0.60f, 1f);
    private MaterialPropertyBlock properties;
    private Material[] restingMaterials, fadeMaterials;
    private Color restingEmission;
    private Camera viewCamera;
    private GameManager displayedGame;
    private Gem[] myGems, opponentGems;

    void Awake()
    {
        properties = new MaterialPropertyBlock();
        var renderer = manaPrefab != null ? manaPrefab.GetComponentInChildren<Renderer>(true) : null;
        if (renderer != null && fadeShader != null)
        {
            restingMaterials = renderer.sharedMaterials;
            fadeMaterials = new Material[restingMaterials.Length];
            for (int i = 0; i < fadeMaterials.Length; i++)
                fadeMaterials[i] = new Material(restingMaterials[i]) { shader = fadeShader };
            if (restingMaterials.Length > GemMaterialIndex)
                restingEmission = restingMaterials[GemMaterialIndex].GetColor(Emission);
        }
        myGems = CreateGems(myMana);
        opponentGems = CreateGems(opponentMana);
    }

    Gem[] CreateGems(Transform root)
    {
        if (root == null || manaPrefab == null) return System.Array.Empty<Gem>();
        var gems = new Gem[GameRules.MaxCost];
        for (int i = 0; i < gems.Length; i++)
        {
            var gem = new Gem
            {
                root = Instantiate(manaPrefab, root).transform,
                glow = spendGlowPrefab != null ? Instantiate(spendGlowPrefab, root) : null,
                position = startPosition + new Vector3(spacing * i, 0f, i % 2 * zigzag),
                rotation = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f)
            };
            gem.renderer = gem.root.GetComponentInChildren<Renderer>(true);
            gem.root.SetLocalPositionAndRotation(gem.position, gem.rotation);
            gem.root.localScale = Vector3.one * gemScale;
            Hide(gem);
            gems[i] = gem;
        }
        return gems;
    }

    void Update()
    {
        var game = VRGameSession.Game;
        var session = ProductionGameController.Current;
        bool ready = session == null || (session.Ready && session.HardwareReady);
        if (!ready || displayedGame != game)
        {
            if (displayedGame != null)
            {
                foreach (var gem in myGems) Hide(gem);
                foreach (var gem in opponentGems) Hide(gem);
            }
            displayedGame = ready ? game : null;
            if (!ready) return;
        }
        float step = Time.deltaTime / effectDuration;
        Show(myGems, game.Board.me.currentCost, step);
        Show(opponentGems, game.Board.opponent.currentCost, step);
    }

    void Show(Gem[] gems, int count, float step)
    {
        for (int i = 0; i < gems.Length; i++)
        {
            var gem = gems[i];
            bool present = i < count;
            if (present ? gem.lift == 0f : gem.lift == 1f) continue;
            if (!gem.fading && fadeMaterials != null)
            {
                gem.renderer.sharedMaterials = fadeMaterials;
                gem.fading = true;
            }
            if (gem.root.gameObject.activeSelf)
                gem.lift = Mathf.MoveTowards(gem.lift, present ? 0f : 1f, step);

            float pose = Mathf.SmoothStep(0f, 1f, gem.lift);
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 1f, gem.lift));
            float glow = present ? 0f : Mathf.Sin(Mathf.PI * gem.lift);
            gem.root.SetLocalPositionAndRotation(gem.position + Vector3.up * (liftHeight * pose),
                gem.rotation * Quaternion.Euler(liftTilt * pose));

            if (gem.fading)
            {
                properties.Clear();
                properties.SetFloat(Opacity, alpha);
                gem.renderer.SetPropertyBlock(properties);
                if (restingMaterials.Length > GemMaterialIndex)
                {
                    properties.SetColor(Emission, restingEmission + BlueLight * (spendEmission * glow));
                    gem.renderer.SetPropertyBlock(properties, GemMaterialIndex);
                }
                if (gem.lift == 0f)
                {
                    gem.renderer.SetPropertyBlock(null);
                    if (restingMaterials.Length > GemMaterialIndex)
                        gem.renderer.SetPropertyBlock(null, GemMaterialIndex);
                    gem.renderer.sharedMaterials = restingMaterials;
                    gem.fading = false;
                }
            }
            gem.root.gameObject.SetActive(gem.lift < 1f || present);
            SetGlow(gem, glow);
        }
    }

    void SetGlow(Gem gem, float amount)
    {
        if (gem.glow == null) return;
        bool visible = amount > 0.001f;
        gem.glow.gameObject.SetActive(visible);
        if (!visible) return;
        if (viewCamera == null) viewCamera = Camera.main;
        var position = gem.root.position + gem.root.parent.up * 0.004f;
        if (viewCamera != null)
        {
            var cameraTransform = viewCamera.transform;
            position += (position - cameraTransform.position).normalized * 0.004f;
            gem.glow.transform.rotation = cameraTransform.rotation;
        }
        gem.glow.transform.position = position;
        properties.Clear();
        properties.SetFloat(Opacity, amount * 0.55f);
        gem.glow.SetPropertyBlock(properties);
    }

    static void Hide(Gem gem)
    {
        gem.lift = 1f;
        gem.root.gameObject.SetActive(false);
        if (gem.glow != null) gem.glow.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (fadeMaterials == null) return;
        foreach (var material in fadeMaterials) Destroy(material);
    }
}
