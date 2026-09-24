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

        // Derived from the retained sample, with no live physics query or allocation.
        public Vector3 SampleLeverArm0 => SampleLeverArm(0);
        public Vector3 SampleLeverArm1 => SampleLeverArm(1);
        public Vector3 SampleLeverArm2 => SampleLeverArm(2);
        public Vector3 SampleLeverArm3 => SampleLeverArm(3);
        public float SimulationDistance => SeasonalIceFloeWaves.WaterDistance;

        private Vector3 SampleLeverArm(int index) => Diagnostics != null && Diagnostics.Captured
            ? Diagnostics.Probes[index].AppliedWorld - Diagnostics.CenterOfMass : Vector3.zero;

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
