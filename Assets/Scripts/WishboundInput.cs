using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Use the Editor's legacy devices when the Input System exposes only virtual devices.</summary>
internal static class WishboundInput
{
    public static bool Pressed(Key key, KeyCode fallback) => ReadKey(key,fallback,true);
    public static bool Held(Key key, KeyCode fallback) => ReadKey(key,fallback,false);

    static bool ReadKey(Key key, KeyCode fallback, bool edge)
    {
        foreach (var device in InputSystem.devices)
            if (device is Keyboard keyboard && keyboard.enabled &&
                (edge ? keyboard[key].wasPressedThisFrame : keyboard[key].isPressed)) return true;
#if ENABLE_LEGACY_INPUT_MANAGER
        return edge ? Input.GetKeyDown(fallback) : Input.GetKey(fallback);
#else
        return false;
#endif
    }

    public static Vector2 Pointer
    {
        get
        {
#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            if (UseLegacyMouse) return Input.mousePosition;
#endif
            return Mouse.current?.position.ReadValue() ?? new Vector2(Screen.width, Screen.height) * .5f;
        }
    }

    public static Vector2 MouseDelta
    {
        get
        {
#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            // The project's two legacy mouse axes have a sensitivity of 0.1.
            if (UseLegacyMouse) return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
#endif
            return Mouse.current?.delta.ReadValue() ?? Vector2.zero;
        }
    }

    public static bool PrimaryPressed
    {
        get
        {
#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            if (UseLegacyMouse) return Input.GetMouseButtonDown(0);
#endif
            return Mouse.current?.leftButton.wasPressedThisFrame ?? false;
        }
    }

#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
    static bool UseLegacyMouse => Mouse.current is not { native: true, enabled: true };
#endif
}
