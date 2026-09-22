using UnityEngine;

namespace Seasons
{
    public class IceFloeClimb : MonoBehaviour, Hoverable, Interactable
    {
        public float m_useDistance = 3f;
        public float m_radius = 4f;
        private Floating m_floating;
        private ZNetView m_view;
        private bool m_started;
        internal bool Started => m_started;

        public void Start()
        {
            m_started = true;
            m_floating = GetComponent<Floating>();
            m_view = GetComponent<ZNetView>();
            if (m_view != null && m_view.IsValid() && m_view.m_body != null)
            {
                float mass = m_view.GetZDO().GetFloat(SeasonsVars.s_iceFloeMass);
                if (mass != 0f)
                    m_view.m_body.mass = mass;
            }
            SeasonalIceFloeWaves.Track(m_floating);
        }

        private void OnEnable()
        {
            if (m_started)
                SeasonalIceFloeWaves.Track(m_floating);
        }

        private void OnDisable() => SeasonalIceFloeWaves.Untrack(m_floating);

        private void OnDestroy() => SeasonalIceFloeWaves.Untrack(m_floating);

        public bool Interact(Humanoid character, bool hold, bool alt)
        {
            if (hold)
                return false;

            if (!InUseDistance(character))
                return false;

            SeasonalIceFloeWaves.PrepareInteraction(m_floating);
            character.transform.position = Vector3.Lerp(character.transform.position, base.transform.position, 0.35f) + Vector3.up;
            Physics.SyncTransforms();
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        public string GetHoverText()
        {
            if (!InUseDistance(Player.m_localPlayer))
                return "";

            return "[<color=yellow><b>$KEY_Use</b></color>] $seasons_ice_floe_climb".Localize();
        }

        public string GetHoverName()
        {
            return "";
        }

        public float GetHoverOffset() => 0f;

        public bool InUseDistance(Humanoid human)
        {
            if (human == null)
                return false;

            if (m_view == null || !m_view.IsValid() || !m_view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark))
                return false;

            if (base.transform.position.y - human.transform.position.y < 0.5f)
                return false;

            Vector3 distance = transform.InverseTransformPoint(human.transform.position);
            float ellipticalDistance = Mathf.Sqrt(distance.x * distance.x + distance.z * distance.z);

            return m_radius < ellipticalDistance && ellipticalDistance < m_radius + m_useDistance;
        }
    }
}
