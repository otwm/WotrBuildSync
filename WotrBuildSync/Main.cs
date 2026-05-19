using UnityModManagerNet;

namespace WotrBuildSync
{
    public class Main
    {
        internal static UnityModManager.ModEntry ModEntry;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            return true;
        }
    }
}
