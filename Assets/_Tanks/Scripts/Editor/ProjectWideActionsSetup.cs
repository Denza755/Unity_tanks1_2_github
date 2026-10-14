using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tanks.Complete
{
    // When the project is imported as a .unitypackage, the Project-wide Actions setting is applied before
    // Tank_Actions is imported, so the reference is lost and the game can't find its actions (e.g. "Pause").
    // This assigns Tank_Actions as the Project-wide Actions whenever the current ones are missing or lack them.
    [InitializeOnLoad]
    public static class ProjectWideActionsSetup
    {
        // GUID of Assets/_Tanks/Settings/Tank_Actions.inputactions
        private const string k_TankActionsGuid = "7fb6c1c790fa04865b379a6e8ccd7e9c";

        static ProjectWideActionsSetup()
        {
            // Wait until the AssetDatabase has finished importing before looking up the asset
            EditorApplication.delayCall += AssignActionsIfMissing;
        }

        private static void AssignActionsIfMissing()
        {
            // Already set up with an asset that has the actions the game needs
            var current = InputSystem.actions;
            if (current != null && current.FindAction("Pause") != null)
                return;

            string path = AssetDatabase.GUIDToAssetPath(k_TankActionsGuid);
            var tankActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);

            // The asset may not be imported yet; this runs again on the next domain reload
            if (tankActions == null)
                return;

            InputSystem.actions = tankActions;
            Debug.Log($"[Tanks] Assigned {path} as the Project-wide Input Actions.");
        }
    }
}
