using UnityEngine;

namespace Seasons
{
    public class IceFloeClimb : MonoBehaviour, Hoverable, Interactable
    {
        public float m_useDistance = 3f;
        public float m_radius = 4f;

        public void Start()
        {
            ZNetView m_nview = GetComponent<ZNetView>();
            if (m_nview != null && m_nview.m_body != null)
            {
                float mass = m_nview.GetZDO().GetFloat(ZoneSystemVariantController.s_iceFloeMass);
                if (mass != 0f)
                    m_nview.m_body.mass = mass;
            }
        }

        public bool Interact(Humanoid character, bool hold, bool alt)
        {
            if (hold)
                return false;

            if (!InUseDistance(character))
                return false;

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

            return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] $piece_use");
        }

        public string GetHoverName()
        {
            return "";
        }

        public bool InUseDistance(Humanoid human)
        {
            if (base.transform.position.y - human.transform.position.y < 0.5f)
                return false;

            Vector3 distance = human.transform.position - transform.position;
            distance.y = 0f;

            float sx = Mathf.Max(0.0001f, transform.lossyScale.x);
            float sz = Mathf.Max(0.0001f, transform.lossyScale.z);

            float ellipticalDistance = Mathf.Sqrt(
                (distance.x * distance.x) / (sx * sx) +
                (distance.z * distance.z) / (sz * sz)
            );

            return m_radius < ellipticalDistance && ellipticalDistance < m_radius + m_useDistance;
        }
    }
}
