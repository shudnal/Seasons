using UnityEngine;

namespace Seasons
{
    public class IceFloeClimb : MonoBehaviour, Hoverable, Interactable
    {
        public float m_useDistance = 3f;
        public float m_radius = 4f;

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

            float distance = Vector3.Distance(human.transform.position, base.transform.position);

            return m_radius < distance && distance < m_radius + m_useDistance;
        }
    }

}
