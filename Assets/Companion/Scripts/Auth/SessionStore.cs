using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Companion
{
    // Persists the refresh token: iOS Keychain on device, PlayerPrefs elsewhere (Editor/dev only).
    public static class SessionStore
    {
        const string RefreshKey = "companion.refreshToken";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _CompanionKeychainSet(string key, string value);
        [DllImport("__Internal")] static extern IntPtr _CompanionKeychainGet(string key);
        [DllImport("__Internal")] static extern void _CompanionKeychainDelete(string key);

        public static string RefreshToken
        {
            get
            {
                var p = _CompanionKeychainGet(RefreshKey);
                if (p == IntPtr.Zero) return null;
                var s = Marshal.PtrToStringUTF8(p);
                Marshal.FreeHGlobal(p);
                return s;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) _CompanionKeychainDelete(RefreshKey);
                else _CompanionKeychainSet(RefreshKey, value);
            }
        }
#else
        public static string RefreshToken
        {
            get => PlayerPrefs.GetString(RefreshKey, null);
            set
            {
                if (string.IsNullOrEmpty(value)) PlayerPrefs.DeleteKey(RefreshKey);
                else PlayerPrefs.SetString(RefreshKey, value);
                PlayerPrefs.Save();
            }
        }
#endif
    }
}
