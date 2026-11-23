using UnityEngine;

namespace Game.Combat
{
    public class ElementFxDemo : MonoBehaviour
    {
        [SerializeField] uint elementA = 1;
        [SerializeField] uint elementB = 2;
        [SerializeField] float attachDuration = 3f;
        [SerializeField] float interval = 5f;
        [SerializeField] float startDelay = 1f;

        float timer;

        void Start()
        {
            timer = interval - startDelay;
        }

        void Update()
        {
            timer += Time.deltaTime;
            if (timer < interval) return;

            timer = 0f;

            var fx = ElementFxController.Instance;
            if (fx == null) return;

            fx.PlayAttach(transform, elementA, attachDuration);
            fx.PlayAttach(transform, elementB, attachDuration);
            fx.PlayCombo(transform, elementA, elementB);
        }
    }
}