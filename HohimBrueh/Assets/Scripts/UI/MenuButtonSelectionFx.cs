using UnityEngine;
using UnityEngine.EventSystems;

namespace FrogSmashers.UI
{
    public class MenuButtonSelectionFx : MonoBehaviour
    {
        public float scaleNormal = 1f;
        public float scaleSelected = 1.08f;
        public float lerpSpeed = 14f;

        void Update()
        {
            bool selected = EventSystem.current != null &&
                            EventSystem.current.currentSelectedGameObject == gameObject;
            float t = selected ? scaleSelected : scaleNormal;
            var target = new Vector3(t, t, 1f);
            transform.localScale = Vector3.Lerp(transform.localScale, target,
                                                 lerpSpeed * Time.unscaledDeltaTime);
        }
    }
}
