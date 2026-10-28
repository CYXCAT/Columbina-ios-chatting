using UnityEngine;

namespace Companion
{
    // Per-environment settings shared by the runtime (CompanionApp) and the iOS post-build step.
    [CreateAssetMenu(menuName = "Companion/Config", fileName = "CompanionConfig")]
    public class CompanionConfig : ScriptableObject
    {
        [Tooltip("companion-backend/web base URL. localhost only works in the Editor; use your LAN IP or the Vercel URL on device.")]
        public string apiBaseUrl = "http://localhost:3000";

        [Tooltip("Google Cloud OAuth client of type iOS (bundle ID must match). Written to Info.plist as GIDClientID.")]
        public string googleIosClientId = "";

        [Tooltip("Google Cloud OAuth client of type Web application. ID tokens are issued for it (GIDServerClientID); the backend checks it as the audience.")]
        public string googleServerClientId = "";

        public static string ReversedClientId(string clientId) =>
            string.Join(".", System.Linq.Enumerable.Reverse(clientId.Split('.')));
    }
}
