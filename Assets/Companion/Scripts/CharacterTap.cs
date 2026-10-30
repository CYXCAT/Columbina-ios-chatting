using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Companion
{
    // Tapping the character interrupts what she is doing on her own (an idle, a song). A tap is a short
    // press that barely moves; presses that start on the HUD are left to the UI.
    public class CharacterTap : MonoBehaviour
    {
        [SerializeField] CompanionMotion motion;
        [SerializeField] Camera targetCamera;
        [SerializeField] UIDocument hud;
        [Tooltip("Hit area around the character.")]
        [SerializeField] Collider target;
        [SerializeField] float maxDuration = 0.35f;
        [SerializeField] float maxMovement = 12f;

        Vector2 downPosition;
        float downTime;
        bool pressed;

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var position = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                pressed = !OverHud(position);
                downPosition = position;
                downTime = Time.unscaledTime;
            }
            else if (pressed && pointer.press.wasReleasedThisFrame)
            {
                pressed = false;
                if (Time.unscaledTime - downTime <= maxDuration && (position - downPosition).magnitude <= maxMovement
                    && HitsCharacter(position))
                    motion.NotifyActivity();
            }
        }

        bool OverHud(Vector2 screen)
        {
            var panel = hud != null ? hud.rootVisualElement?.panel : null;
            if (panel == null) return false;
            // Panel coordinates start at the top of the screen.
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(p);
            return picked != null && picked != hud.rootVisualElement;
        }

        bool HitsCharacter(Vector2 screen) =>
            target != null && targetCamera != null && target.Raycast(targetCamera.ScreenPointToRay(screen), out _, 100f);
    }
}
