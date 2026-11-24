using UnityEngine;

public sealed class HeroHealthCrystal : MonoBehaviour
{
    [SerializeField] private PlayerSide side;
    [SerializeField] private MeshRenderer inside;
    [SerializeField] private MeshRenderer outside;
    [SerializeField] private GameObject brokenOutside;
    [SerializeField] private ParticleSystem breakParticles;

    private const float BreakEdgeTime = 0.09f;
    private const short BreakParticleCount = 96;
    [SerializeField, Min(0f)] private float floatHeight = 0.004f;
    [SerializeField, Min(0.01f)] private float impactDuration = 0.30f;

    private static readonly int FillHeight = Shader.PropertyToID("_FillHeight");
    private MaterialPropertyBlock properties;
    private Animator collapseAnimator;
    private Transform model;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private ProductionTableFeedback feedback;
    private GameManager displayedGame;
    private float bottom, top, fill, startFill, targetFill, progress;
    private float hitTime = float.NegativeInfinity;
    private bool isBroken;
    private bool breakPending;
    private ParticleSystem breakBurst;

    private void Awake()
    {
        if (inside == null) { enabled = false; return; }
        if (brokenOutside != null) collapseAnimator = brokenOutside.GetComponent<Animator>();
        model = inside.transform.parent;
        restPosition = model.localPosition;
        restRotation = model.localRotation;
        // The imported crystal's local Z points up.
        bottom = inside.localBounds.min.z;
        top = inside.localBounds.max.z;
        properties = new MaterialPropertyBlock();
        inside.GetPropertyBlock(properties);
    }

    private void LateUpdate()
    {
        var session = ProductionGameController.Current;
        var currentFeedback = session != null ? session.TableFeedback : null;
        if (currentFeedback != feedback)
        {
            if (feedback != null) feedback.HeroHitShown -= Shake;
            feedback = currentFeedback;
            if (feedback != null) feedback.HeroHitShown += Shake;
        }

        var game = VRGameSession.Game;
        var board = game.Board.GetBoard(side);
        int health = feedback?.DisplayedHeroHealth(side) ?? board.HeroHealth;
        float ratio = Mathf.Clamp01((float)health / board.maxHeroHealth);
        if (displayedGame != game)
        {
            ResetBreak();
            displayedGame = game;
            targetFill = ratio;
            progress = 1f;
            hitTime = float.NegativeInfinity;
            SetFill(ratio);
        }

        if (health <= 0)
        {
            if (!isBroken) UpdateBreak();
            return;
        }
        if (isBroken || breakPending) ResetBreak();

        if (ratio != targetFill)
        {
            startFill = fill;
            targetFill = ratio;
            progress = 0f;
        }
        if (progress < 1f)
        {
            progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / impactDuration);
            SetFill(Mathf.SmoothStep(startFill, targetFill, progress));
        }

        float phase = Mathf.Clamp01((Time.time - hitTime) / impactDuration);
        float shake = Mathf.Sin(phase * 4f * Mathf.PI) * (1f - phase);
        float bob = Mathf.Sin(Time.time * 2.2f + (int)side) * floatHeight;
        model.SetLocalPositionAndRotation(restPosition + new Vector3(0.005f * shake, bob, 0f),
            restRotation * Quaternion.Euler(3f * shake, 0f, 4f * shake));
    }

    private void SetFill(float ratio)
    {
        fill = ratio;
        properties.SetFloat(FillHeight, fill > 0f ? Mathf.Lerp(bottom, top, fill) : bottom - 0.0001f);
        inside.SetPropertyBlock(properties);
    }

    private void UpdateBreak()
    {
        if (!breakPending)
        {
            if (breakParticles == null)
            {
                SetBroken(true);
                return;
            }

            if (breakBurst == null)
                breakBurst = Instantiate(breakParticles);

            var shell = outside != null ? outside : inside;
            var extents = shell.localBounds.extents;
            float halfWidth = shell.transform.TransformVector(new Vector3(extents.x, 0f, 0f)).magnitude;
            float halfDepth = shell.transform.TransformVector(new Vector3(0f, extents.y, 0f)).magnitude;
            float radius = Mathf.Max(0.005f, (halfWidth + halfDepth) * 0.5f);
            float startRadius = radius * 0.08f;
            breakBurst.transform.SetPositionAndRotation(shell.bounds.center,
                transform.rotation * Quaternion.Euler(-90f, 0f, 0f));

            var main = breakBurst.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.20f;
            main.startDelay = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.19f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.07f, radius * 0.13f);
            main.startSpeed = (radius - startRadius) / BreakEdgeTime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var shape = breakBurst.shape;
            shape.radius = startRadius;
            shape.radiusThickness = 0f;
            shape.arcSpread = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;

            var emission = breakBurst.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, BreakParticleCount) });

            // Keep the intact shell at the same pose while the ring crosses it.
            breakPending = true;
            breakBurst.gameObject.SetActive(true);
            breakBurst.Play();
            return;
        }

        if (breakBurst.time >= BreakEdgeTime)
        {
            breakPending = false;
            SetBroken(true);
        }
    }

    private void ResetBreak()
    {
        breakPending = false;
        if (breakBurst != null)
            breakBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        SetBroken(false);
    }

    private void SetBroken(bool broken)
    {
        if (isBroken == broken) return;
        isBroken = broken;
        inside.enabled = !broken;
        if (outside == null || brokenOutside == null) return;
        outside.enabled = !broken;
        brokenOutside.SetActive(broken);
        if (!broken || collapseAnimator == null) return;
        collapseAnimator.Play("Stone Fall", 0, 0f);
        collapseAnimator.Update(0f);
    }

    private void Shake(PlayerSide hitSide)
    {
        if (side == hitSide) hitTime = Time.time;
    }

    private void OnDestroy()
    {
        if (breakBurst != null) Destroy(breakBurst.gameObject);
    }

    private void OnDisable()
    {
        if (feedback != null) feedback.HeroHitShown -= Shake;
        feedback = null;
        displayedGame = null;
        if (model != null) ResetBreak();
        if (model != null) model.SetLocalPositionAndRotation(restPosition, restRotation);
    }
}
