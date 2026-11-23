using Game.Net;
using Game.Proto;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Player
{
    public class DeathGhostTransition : MonoBehaviour
    {
        [SerializeField] CharacterController cc;
        [SerializeField] Renderer[] renderers;
        [SerializeField] GhostForm ghostForm;
        [SerializeField] PlayerController player;
        [SerializeField] float ghostAlpha = 0.4f;
        [SerializeField] Color ghostTint = new(0.75f, 0.85f, 1f, 1f);

        struct RenderState
        {
            public Color Color;
            public float Surface;
            public int SrcBlend;
            public int DstBlend;
            public int ZWrite;
            public bool TransparentKeyword;
        }

        // 本地玩家是否处于鬼魂形态：祭坛复活等交互的唯一判定来源
        public static bool IsGhost { get; private set; }

        RenderState[] states;
        Collider[] colliders;
        float savedGravity;
        bool gravitySaved;

        void Awake()
        {
            IsGhost = false;

            if (cc == null) cc = GetComponent<CharacterController>();
            if (player == null) player = GetComponent<PlayerController>();
            if (ghostForm == null) ghostForm = GetComponent<GhostForm>();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);

            CacheOriginals();
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CDeath>(OnDeath);
            MsgDispatcher.Register<S2CRevived>(OnRevived);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CDeath>();
            MsgDispatcher.Unregister<S2CRevived>();
        }

        // 读取 sharedMaterial，避免 Awake 阶段就实例化材质
        void CacheOriginals()
        {
            if (renderers == null) return;

            states = new RenderState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                var state = new RenderState
                {
                    Color = Color.white,
                    Surface = 0f,
                    SrcBlend = (int)BlendMode.One,
                    DstBlend = (int)BlendMode.Zero,
                    ZWrite = 1,
                };

                if (r is SpriteRenderer sr)
                {
                    state.Color = sr.color;
                }
                else if (r.sharedMaterial != null)
                {
                    var m = r.sharedMaterial;
                    if (m.HasProperty("_BaseColor")) state.Color = m.GetColor("_BaseColor");
                    else if (m.HasProperty("_Color")) state.Color = m.GetColor("_Color");
                    if (m.HasProperty("_Surface")) state.Surface = m.GetFloat("_Surface");
                    if (m.HasProperty("_SrcBlend")) state.SrcBlend = m.GetInt("_SrcBlend");
                    if (m.HasProperty("_DstBlend")) state.DstBlend = m.GetInt("_DstBlend");
                    if (m.HasProperty("_ZWrite")) state.ZWrite = m.GetInt("_ZWrite");
                    state.TransparentKeyword = m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
                }

                states[i] = state;
            }
        }

        void OnDeath(S2CDeath msg)
        {
            if (msg.EntityId != LocalEntityId()) return;

            IsGhost = true;
            SetCollision(false);
            SetGravity(0f);
            if (ghostForm != null) ghostForm.enabled = true;
            ApplyGhostVisual(true);
        }

        void OnRevived(S2CRevived msg)
        {
            if (msg.EntityId != LocalEntityId()) return;

            IsGhost = false;
            SetCollision(true);
            RestoreGravity();
            if (ghostForm != null) ghostForm.enabled = false;
            ApplyGhostVisual(false);
        }

        // 鬼魂无碰撞：保留 CharacterController 以继续移动，只关掉碰撞检测
        void SetCollision(bool on)
        {
            if (cc != null) cc.detectCollisions = on;

            if (colliders == null) return;

            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || col is CharacterController) continue;
                col.enabled = on;
            }
        }

        // 无碰撞时关掉重力，避免鬼魂持续下沉
        void SetGravity(float value)
        {
            if (player == null || player.Settings == null) return;

            if (!gravitySaved)
            {
                savedGravity = player.Settings.gravity;
                gravitySaved = true;
            }

            player.Settings.gravity = value;
        }

        void RestoreGravity()
        {
            if (player == null || player.Settings == null || !gravitySaved) return;

            player.Settings.gravity = savedGravity;
            gravitySaved = false;
        }

        void ApplyGhostVisual(bool ghost)
        {
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                var origin = states != null && i < states.Length
                    ? states[i]
                    : new RenderState { Color = Color.white };

                // 2D 精灵走顶点色 alpha，不实例化材质，也不需要 URP 混合参数
                if (r is SpriteRenderer sr)
                {
                    sr.color = ghost
                        ? new Color(ghostTint.r, ghostTint.g, ghostTint.b, ghostAlpha)
                        : origin.Color;
                    continue;
                }

                var m = r.material;
                if (m == null) continue;

                if (ghost)
                {
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                    if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                    m.renderQueue = (int)RenderQueue.Transparent;
                    SetColor(m, new Color(ghostTint.r, ghostTint.g, ghostTint.b, ghostAlpha));
                    continue;
                }

                m.SetOverrideTag("RenderType", origin.TransparentKeyword ? "Transparent" : "Opaque");
                if (origin.TransparentKeyword) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", origin.Surface);
                if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", origin.SrcBlend);
                if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", origin.DstBlend);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", origin.ZWrite);
                m.renderQueue = origin.TransparentKeyword ? (int)RenderQueue.Transparent : -1;
                SetColor(m, origin.Color);
            }
        }

        static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        static ulong LocalEntityId()
            => GameRoot.Instance != null ? GameRoot.Instance.LocalEntityId : 0;
    }
}