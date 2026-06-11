using TMPro;

namespace Seasons
{
    public static class StatusEffectHud
    {
        public static void EnsureTimeTextRichText()
        {
            if (Hud.instance?.m_statusEffectTemplate == null)
                return;

            if (Hud.instance.m_statusEffectTemplate.Find("TimeText") is not { } timeTextTransform)
                return;

            if (timeTextTransform.GetComponent<TMP_Text>() is TMP_Text timeText)
                timeText.richText = true;
        }
    }
}
