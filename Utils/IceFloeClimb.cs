using UnityEngine;

namespace Seasons
{
    public partial class IceFloeClimb : MonoBehaviour, Hoverable, Interactable
    {
        public float m_useDistance = 3f;
        public float m_radius = 4f;
        public Floating m_floating;
        public ZNetView m_view;
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
            if (hold || !InUseDistance(character))
                return false;
            SeasonalIceFloeWaves.PrepareInteraction(m_floating);
            character.transform.position = Vector3.Lerp(character.transform.position, transform.position, 0.35f) + Vector3.up;
            Physics.SyncTransforms();
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            string text = InUseDistance(Player.m_localPlayer)
                ? "[<color=yellow><b>$KEY_Use</b></color>] $seasons_ice_floe_climb".Localize() : "";
            AppendWaveDiagnostics(ref text);
            return text;
        }

        // Implemented by the controller partial; climb distance never gates diagnostic hover.
        partial void AppendWaveDiagnostics(ref string text);
        internal void AppendHoverDiagnostics(ref string text) => AppendWaveDiagnostics(ref text);

        // Retained surface observations, not collider supports or force application points.
        public Vector3 SamplePosition0 => SamplePosition(0);
        public Vector3 SamplePosition1 => SamplePosition(1);
        public Vector3 SamplePosition2 => SamplePosition(2);
        public Vector3 SamplePosition3 => SamplePosition(3);
        public float SimulationDistance => SeasonalIceFloeWaves.WaterDistance;

        private Vector3 SamplePosition(int index)
        {
            WaveDiagnostics d = Diagnostics;
            if (d == null || !d.Captured || (uint)index >= 4u)
                return Vector3.zero;
            Vector3 offset = index < 2 ? d.Wind * (index == 0 ? d.AlongRadius : -d.AlongRadius) :
                Vector3.Cross(d.Wind, Vector3.up) * (index == 2 ? d.AcrossRadius : -d.AcrossRadius);
            Vector3 point = d.CenterOfMass + offset;
            point.y = d.Heights[index];
            return point;
        }

        public string GetHoverName() => "";
        public float GetHoverOffset() => 0f;

        public bool InUseDistance(Humanoid human)
        {
            if (human == null || m_view == null || !m_view.IsValid() ||
                !m_view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark))
                return false;
            if (transform.position.y - human.transform.position.y < 0.5f)
                return false;
            Vector3 distance = transform.InverseTransformPoint(human.transform.position);
            float ellipticalDistance = Mathf.Sqrt(distance.x * distance.x + distance.z * distance.z);
            return m_radius < ellipticalDistance && ellipticalDistance < m_radius + m_useDistance;
        }
    }
}
